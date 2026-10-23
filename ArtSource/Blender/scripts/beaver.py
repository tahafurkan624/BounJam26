"""Humanoid beaver: cute chibi builder (after the 'Chibi Builder Turnaround' sheet, minus the tool belt).

  SK_Beaver   one skinned mesh (body, eyes, nose, teeth, trucker cap, tail) with a baked hand-painted albedo: warm top
              light, AO, painted shadow/highlight tints, fur clumps and streaks, brows, smile, the tail's
              waffle grid. Lighting lives in the texture, so use an unlit (or very soft toon) shader.
  ARM_Beaver  armature with Unity Humanoid-mappable names (Hips, Spine, Chest, Neck, Head,
              Shoulder/UpperArm/LowerArm/Hand, UpperLeg/LowerLeg/Foot/Toes .L/.R) + Tail_1..3.

Pipeline: metaball sculpt -> dense "high" mesh vertex-painted with region masks -> decimated "low" mesh
(heat-weighted, smart-UV'd) -> high-to-low emission bake of the paint shader into T_Beaver_Albedo.
~1.15 m tall in an A-pose, pivot between the feet, faces -Y. The Astroneer gear lives in beaver_gear.py.
"""
import math
import os
import sys

import bmesh
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

bpy = C.bpy
TEXTURE_DIR = os.path.join(C.REPO, "Assets", "_Project", "Art", "Textures")
TEX_SIZE = 2048
BODY_TRIS = 8000
MB_SURFACE = 0.575  # a metaball's visible surface sits at 0.575 x its radius (threshold 0.6, stiffness 2)
LIGHT = Vector((-0.4, -0.5, 0.77)).normalized()  # painted key light: top, front, viewer's left
BACKDROP = (0.80, 0.76, 0.71)  # warm paper grey for previews


def srgb(h):
    """'#rrggbb' -> linear RGB (paint is stored linear)."""
    return tuple((int(h[i:i + 2], 16) / 255) ** 2.2 for i in (1, 3, 5))


FUR = srgb("#8e5634")
FUR_BACK = srgb("#6a3b22")
BELLY = srgb("#e8c596")
MUZZLE = srgb("#f2d5ac")
PAWS = srgb("#4f2d1c")
INNER_EAR = srgb("#cf8a7a")
BLUSH = srgb("#e6907c")
BROW = srgb("#5a301c")
MOUTH = srgb("#4a2216")
EYE = srgb("#1a110d")
EYE_BOUNCE = srgb("#5e3b2c")
NOSE = srgb("#3e2420")
NOSE_HL = srgb("#a07a6c")
TEETH = srgb("#fbf7ec")
TEETH_TIP = srgb("#eadbbc")
TAIL = srgb("#74482e")
TAIL_DARK = srgb("#4c2b1a")
CAP_ORANGE = srgb("#e58a26")
CAP_CREAM = srgb("#f1e6cc")
CAP_SEAM = srgb("#a8581a")

# Joints (left side, +X). Shared by the sculpt and the rig.
SHOULDER, ELBOW, WRIST, HAND = (0.19, 0.0, 0.60), (0.31, -0.02, 0.485), (0.375, -0.035, 0.37), (0.40, -0.045, 0.325)
THUMB = (0.38, -0.093, 0.345)
HIP, KNEE, ANKLE = (0.115, 0.0, 0.25), (0.12, -0.015, 0.155), (0.125, 0.0, 0.075)
FOOT_C, FOOT_R = (0.13, -0.06, 0.035), (0.068, 0.105, 0.035)
HEAD_C, HEAD_R = (0.0, -0.01, 0.90), (0.255, 0.225, 0.215)
EAR_C = (0.222, 0.0, 0.985)  # low on the sides, peeking out under the cap
MUZZLE_BALLS = [((0.064, -0.205, 0.815), 0.084), ((-0.064, -0.205, 0.815), 0.084),
                ((0.0, -0.215, 0.862), 0.056), ((0.0, -0.165, 0.745), 0.05)]
EYE_X, EYE_Z = 0.098, 0.935
TAIL_BASE, TAIL_LEN, TAIL_W, TAIL_H = (0.0, 0.15, 0.22), 0.74, 0.175, 0.03
TAIL_PITCH, TAIL_CURL = math.radians(37), math.radians(22)  # dips to the ground, then the tip lifts
CAP_TILT, CAP_RIM, CAP_T = 0.2, math.radians(61), 0.02  # tipped back (rad), crown edge angle, thickness


def smooth(e0, e1, x):
    """Smoothstep from e0 to e1 (e0 > e1 flips it). Works on floats and numpy arrays."""
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def mirrored(p, side):
    return (p[0] * side, p[1], p[2])


# --------------------------------------------------------------------------------------------- sculpt

def sculpt_body(resolution):
    """Metaball body (head, cheek fluff, muzzle, ears, torso, arms, paws, legs, feet) polygonised into a mesh."""
    mb = bpy.data.metaballs.new("BeaverSculpt")
    mb.resolution = mb.render_resolution = resolution
    mb.threshold = 0.6
    mb_obj = bpy.data.objects.new("BeaverSculpt", mb)
    bpy.context.scene.collection.objects.link(mb_obj)

    def ellipsoid(co, radii, rot=None):
        e = mb.elements.new()
        e.type = 'ELLIPSOID'
        e.co = co
        big = max(radii)
        e.radius = big / MB_SURFACE
        e.size_x, e.size_y, e.size_z = (r / big for r in radii)
        if rot:
            e.rotation = rot

    def ball(co, r):
        ellipsoid(co, (r, r, r))

    def capsule(a, b, r):
        a, b = Vector(a), Vector(b)
        e = mb.elements.new()
        e.type = 'CAPSULE'
        e.co = (a + b) / 2
        e.size_x = (b - a).length / 2
        e.radius = r / MB_SURFACE
        e.rotation = Vector((1, 0, 0)).rotation_difference((b - a).normalized())

    ellipsoid((0, 0.01, 0.43), (0.235, 0.205, 0.255))   # pear-shaped torso
    ball((0, -0.07, 0.38), 0.185)                         # soft round belly
    ball((0, 0.03, 0.29), 0.18)                           # hips
    ellipsoid((0, 0, 0.6), (0.165, 0.135, 0.10))          # shoulders
    ellipsoid(HEAD_C, HEAD_R)
    for co, r in MUZZLE_BALLS:
        ball(co, r)
    ellipsoid((0, -0.03, 0.81), (0.238, 0.165, 0.11))    # wide, chubby lower face
    for side in (1, -1):
        m = lambda p: mirrored(p, side)  # noqa: E731
        for co, tilt in (((0.24, -0.05, 0.83), 0.45), ((0.235, -0.03, 0.78), 0.85)):  # fur points on the jowls
            ellipsoid(m(co), (0.036, 0.024, 0.016), Quaternion((0, 1, 0), -side * tilt))
        ellipsoid(m(EAR_C), (0.062, 0.032, 0.062), Quaternion((0, 1, 0), side * 0.9))
        capsule(m(SHOULDER), m(ELBOW), 0.055)
        capsule(m(ELBOW), m(WRIST), 0.05)
        ball(m(HAND), 0.058)
        ball(m(THUMB), 0.022)
        reach = (Vector(HAND) - Vector(WRIST)).normalized()
        for dy in (-0.036, -0.012, 0.012, 0.036):  # four little fingers, splayed
            root = Vector(HAND) + reach * 0.03 + Vector((0, dy * 0.8, 0))
            tip = root + (reach + Vector((0, dy * 4, 0))).normalized() * (0.034 - abs(dy) * 0.2)
            capsule(m(tuple(root)), m(tuple(tip)), 0.0155)
        capsule(m(HIP), m(KNEE), 0.075)
        capsule(m(KNEE), m(ANKLE), 0.065)
        ellipsoid(m(FOOT_C), FOOT_R)
        for dx in (-0.045, -0.015, 0.015, 0.045):  # four toes
            ball(m((FOOT_C[0] + dx, -0.158 - 0.008 * (abs(dx) < 0.02), 0.022)), 0.022)

    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(mb_obj.evaluated_get(deps))
    bpy.data.objects.remove(mb_obj)
    bpy.data.metaballs.remove(mb)
    me.name = "Body"
    obj = bpy.data.objects.new("Body", me)
    bpy.context.scene.collection.objects.link(obj)
    C.clean_mesh(obj)
    min_z = min(v.co.z for v in me.vertices)
    for v in me.vertices:  # stand the soles exactly on z=0
        v.co.z -= min_z
    me.shade_smooth()
    return obj


PAINT_ATTRS = ("TailU", "TailV", "TailMask", "Fur")


def set_paint(obj, colors, tail=None, fur=0.0):
    """Per-vertex paint (linear RGBA; alpha = how much painted lighting applies), tail pattern coords and
    a fur mask (where the clump/streak brushwork goes)."""
    me = obj.data
    n = len(me.vertices)
    attr = me.color_attributes.new("Paint", 'FLOAT_COLOR', 'POINT')
    attr.data.foreach_set("color", np.asarray(colors, dtype=np.float32).ravel())
    tail = np.zeros((n, 3), dtype=np.float32) if tail is None else np.asarray(tail, dtype=np.float32)
    values = [tail[:, 0], tail[:, 1], tail[:, 2], np.full(n, fur, dtype=np.float32)]
    for name, vals in zip(PAINT_ATTRS, values):
        me.attributes.new(name, 'FLOAT', 'POINT').data.foreach_set("value", np.ascontiguousarray(vals))


def paint_body(obj):
    """Region masks painted straight onto the dense sculpt, soft-edged like brush work."""
    me = obj.data
    n = len(me.vertices)
    P = np.empty(n * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(n, 3)
    N = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", N)
    N = N.reshape(n, 3)
    x, y, z = P.T
    ax = np.abs(x)
    Pm = np.column_stack((ax, y, z))  # mirrored to the left side for symmetric masks
    front = smooth(-0.1, -0.16, y)

    col = np.tile(np.array(FUR), (n, 1))

    def mix(c, m):
        nonlocal col
        col = col + (np.array(c) - col) * np.clip(m, 0, 1)[:, None]

    def dist(c, pts=Pm):
        return np.linalg.norm(pts - np.array(c), axis=1)

    def tufts(cx, cz, count):
        """0..1 sawtooth around (cx, cz): pushes a mask edge out into pointed fur tufts."""
        return np.abs(((np.arctan2(z - cz, x - cx) / (2 * np.pi) * count) % 1.0) * 2 - 1)

    mix(FUR_BACK, smooth(-0.02, 0.17, y) * 0.65)
    belly = (x / 0.18) ** 2 + ((z - 0.40) / 0.22) ** 2 - 0.1 * tufts(0, 0.40, 15)
    mix(BELLY, smooth(0.98, 0.78, belly) * smooth(0.0, -0.3, N[:, 1]))
    # Muzzle pads: the balls' outlines seen from the front (fusion moves the surface, not the outline).
    muzzle = np.max([smooth(r + 0.008, r - 0.008, np.hypot(x - c[0], z - c[2])) for c, r in MUZZLE_BALLS], axis=0)
    mix(MUZZLE, muzzle * smooth(-0.15, -0.2, y))
    mix(BLUSH, smooth(0.045, 0.0, dist((0.15, -0.17, 0.855))) * 0.45)
    # Smile: philtrum under the nose, then a curve under each cheek pad with up-turned corners.
    curve = 0.788 - 0.036 * np.sin(np.pi * np.minimum(ax, 0.12) / 0.12)
    smile = smooth(0.0065, 0.0025, np.abs(z - curve)) * smooth(0.13, 0.115, ax)
    philtrum = smooth(0.0055, 0.0025, ax) * smooth(0.784, 0.792, z) * smooth(0.84, 0.83, z)
    mix(MOUTH, np.maximum(smile, philtrum) * front)
    mix(MOUTH, smooth(0.024, 0.016, np.hypot(x / 1.6, z - 0.772)) * front)  # dark mouth behind the teeth
    # Painted brows: short arcs above the eyes.
    arc = np.abs(np.hypot(ax - EYE_X, z - (EYE_Z + 0.012)) - 0.055)
    mix(BROW, smooth(0.0065, 0.003, arc) * smooth(0.98, 0.99, z) * smooth(0.055, 0.03, np.abs(ax - EYE_X)) * front)
    ear = smooth(0.042, 0.026, np.hypot(ax - EAR_C[0], z - EAR_C[2])) * smooth(-0.1, -0.5, N[:, 1])
    mix(INNER_EAR, ear * (dist(EAR_C) < 0.08))
    mix(PAWS, smooth(0.085, 0.07, dist(HAND)))
    mix(PAWS, smooth(0.075, 0.055, z))
    set_paint(obj, np.column_stack((col, np.ones(n))), fur=1.0)


# --------------------------------------------------------------------------------------------- face + tail parts

def sphere_part(name, segments, rings, shape, paint=None, tail=None):
    """UV sphere whose unit-sphere points u go through shape(u) -> position. paint(u) -> RGBA, tail(u) -> (U, V, mask)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    units = [v.co.copy() for v in bm.verts]
    for v, u in zip(bm.verts, units):
        v.co = shape(u)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.shade_smooth()
    if paint:
        set_paint(obj, [paint(u) for u in units], [tail(u) for u in units] if tail else None)
    return obj, units


def surface_hit(tree, x, z):
    """Front-facing surface point of the body at (x, z), shooting the ray from the front (-Y)."""
    loc, normal, _, _ = tree.ray_cast(Vector((x, -2.0, z)), Vector((0, 1, 0)))
    return loc, normal


def placed(center, facing, radii, warp=None):
    """Shape fn: scale unit sphere to radii (optionally warped), turn local -Y towards facing, move to center."""
    rot = Vector((0, -1, 0)).rotation_difference(facing)

    def shape(u):
        p = Vector((u.x * radii[0], u.y * radii[1], u.z * radii[2]))
        if warp:
            p = warp(u, p)
        return center + rot @ p
    return shape


def make_eyes(tree, seg, rings, painted):
    """Shiny black bead eyes: big soft catch-light, small second one, warm bounce at the bottom."""
    hl_main = Vector((-0.4, -0.75, 0.52)).normalized()
    hl_small = Vector((0.32, -0.86, -0.4)).normalized()

    def paint(u):
        c = lerp(EYE, EYE_BOUNCE, 0.6 * smooth(-0.2, -0.8, u.z) * smooth(0.2, -0.4, u.y))
        hl = max(smooth(math.cos(0.36), math.cos(0.28), u.dot(hl_main)),
                 smooth(math.cos(0.15), math.cos(0.10), u.dot(hl_small)))
        return (*lerp(c, (1, 1, 1), hl), 1 - hl)

    eyes = []
    for side in (1, -1):
        loc, n = surface_hit(tree, side * EYE_X, EYE_Z)
        facing = (n + Vector((0, -1.3, 0))).normalized()
        shape = placed(loc - facing * 0.006, facing, (0.034, 0.023, 0.041))
        eyes.append(sphere_part("Eye", seg, rings, shape, paint if painted else None)[0])
    return eyes


def make_nose(tree, seg, rings, painted):
    """Wide, soft rounded-triangle nose with a glossy top highlight."""
    hl = Vector((-0.3, -0.45, 0.84)).normalized()
    nostrils = [Vector((s * 0.5, -0.78, -0.38)).normalized() for s in (1, -1)]

    def paint(u):
        h = smooth(math.cos(0.5), math.cos(0.26), u.dot(hl))
        c = lerp(NOSE, NOSE_HL, 0.9 * h)
        dark = max(smooth(math.cos(0.3), math.cos(0.2), u.dot(d)) for d in nostrils)
        return (*lerp(c, (0.0, 0.0, 0.0), 0.5 * dark), 1 - 0.6 * h)

    loc, n = surface_hit(tree, 0.0, 0.868)
    facing = (n + Vector((0, -1, 0.35))).normalized()
    shape = placed(loc - facing * 0.014, facing, (0.062, 0.036, 0.04),
                   warp=lambda u, p: Vector((p.x * (0.62 + 0.38 * (u.z + 1) / 2), p.y, p.z)))
    return sphere_part("Nose", seg, rings, shape, paint if painted else None)[0]


def make_teeth(tree, seg, rings, painted):
    """Two big buck teeth hanging under the cheek pads."""
    def boxy(u, p):  # superellipsoid: flat front/sides, rounded corners, rounder at the bottom
        sq = lambda a, e: math.copysign(abs(a) ** e, a)  # noqa: E731
        return Vector((sq(u.x, 0.55) * 0.021, p.y, sq(u.z, 0.55 if u.z > 0 else 0.8) * 0.038))

    def paint(u):
        c = lerp(TEETH, TEETH_TIP, 0.5 * smooth(-0.3, -0.95, u.z))
        shine = smooth(0.25, 0.0, abs(u.x + 0.4)) * smooth(-0.3, -0.7, u.y) * smooth(-0.5, 0.0, u.z)
        return (*lerp(c, (1, 1, 1), 0.8 * shine), 1 - 0.6 * shine)

    teeth = []
    for side in (1, -1):
        loc, n = surface_hit(tree, side * 0.0215, 0.79)
        facing = (n + Vector((0, -2.5, 0))).normalized()
        center = Vector((side * 0.0215, loc.y + 0.004, 0.758))
        teeth.append(sphere_part("Tooth", seg, rings, placed(center, facing, (0.021, 0.011, 0.038), boxy),
                                 paint if painted else None)[0])
    return teeth


# Fur tufts: (guide point near the surface, flow direction, length, colour). Left side; mirrored unless x == 0.
TUFTS = [
    ((0.248, -0.07, 0.865), (1.0, -0.25, -0.55), 0.075, FUR),     # cheek fluff, the reference's spiky jowls
    ((0.25, -0.04, 0.81), (1.0, -0.1, -0.85), 0.07, FUR),
    ((0.235, -0.02, 0.755), (0.6, 0.0, -1.0), 0.06, FUR),
    ((0.0, -0.2, 0.685), (0.0, -0.35, -1.0), 0.07, BELLY),        # chest bib under the chin
    ((0.07, -0.19, 0.675), (0.35, -0.35, -1.0), 0.065, BELLY),
    ((0.215, 0.0, 0.66), (0.8, 0.1, -1.0), 0.05, FUR),            # shoulder
    ((0.325, 0.01, 0.49), (0.6, 0.35, -1.0), 0.05, FUR),          # elbow
    ((0.24, -0.02, 0.43), (0.45, 0.0, -1.0), 0.06, FUR),          # waist
    ((0.225, 0.02, 0.31), (0.35, 0.1, -1.0), 0.055, FUR),         # hip
    ((0.11, 0.2, 0.78), (0.25, 0.6, -1.0), 0.06, FUR_BACK),       # nape, under the cap
    ((0.0, 0.215, 0.79), (0.0, 0.6, -1.0), 0.065, FUR_BACK),
    ((0.0, 0.225, 0.5), (0.0, 0.5, -1.0), 0.06, FUR_BACK),        # back
]


def make_tufts(tree, seg, rings, painted):
    """Pointed, slightly curling fur clumps rooted in the surface: they break the silhouette like the
    painted tufts on the reference. Root painted dark, tip light."""
    tufts = []
    for guide, flow, length, color in TUFTS:
        for side in ((1,) if guide[0] == 0 else (1, -1)):
            at, n, _, _ = tree.find_nearest(Vector(mirrored(guide, side)))
            f = Vector(mirrored(flow, side))
            t = (f - n * f.dot(n)).normalized()
            b = t.cross(n)
            w, h = length * 0.42, length * 0.13
            center = at + t * (length * 0.45) - n * (h * 0.3)

            def shape(u, t=t, b=b, n=n, w=w, h=h, center=center, length=length):
                pinch = min(1.0, u.y + 1.0) ** 0.8  # round root (+y), sharp tip (-y)
                tip = (1 - u.y) / 2
                return (center + b * (u.x * w * pinch) - t * (u.y * length * 0.5)
                        + n * (u.z * h * pinch + 0.45 * length * tip ** 2.5))

            def paint(u, color=color):  # same fur as around it, a touch darker at the root, light tip
                s = (1 - u.y) / 2
                return (*lerp(tuple(c * 0.85 for c in color), lerp(color, (1, 0.93, 0.8), 0.3), s ** 1.5), 1.0)

            obj, _ = sphere_part("Tuft", seg, rings, shape, paint if painted else None)
            if painted:
                obj.data.attributes["Fur"].data.foreach_set("value", np.ones(len(obj.data.vertices), dtype=np.float32))
            tufts.append(obj)
    return tufts


def copy_weights(src, dst):
    """Give every dst vertex the bone weights of the nearest src vertex (tufts follow the skin)."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(src.data.vertices))
    for v in src.data.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    names = {g.index: g.name for g in src.vertex_groups}
    groups = {}
    for v in dst.data.vertices:
        _, i, _ = kd.find(v.co)
        for g in src.data.vertices[i].groups:
            name = names[g.group]
            if name not in groups:
                groups[name] = dst.vertex_groups.new(name=name)
            groups[name].add([v.index], g.weight, 'REPLACE')


def make_cap(tree, seg, rings, painted):
    """Trucker cap tipped back on the head: crown fitted to the skull by ray casts (cream front panel,
    orange side/back panels, darker seams and band), top button and a curved orange brim."""
    center = Vector(HEAD_C)
    tilt = Quaternion((1, 0, 0), -CAP_TILT)  # cap frame (z = crown axis) -> world
    cos_rim, sin_rim = math.cos(CAP_RIM), math.sin(CAP_RIM)

    def skull(d):
        hit = tree.ray_cast(center, d)[0]
        return (hit - center).length if hit else 0.22

    def crown_point(u):
        if u.z >= cos_rim:
            d = tilt @ u
            puff = 0.055 * u.z ** 2 + 0.03 * max(0.0, -u.y) * u.z  # tall domed crown, high front panel
            return center + d * (skull(d) + CAP_T + puff)
        # Below the rim the sphere folds back into the head, closing the shell out of sight.
        h = Vector((u.x, u.y)).normalized() if u.xy.length > 1e-6 else Vector((1, 0))
        d = tilt @ Vector((h.x * sin_rim, h.y * sin_rim, cos_rim))
        f = (cos_rim - u.z) / (cos_rim + 1)
        return center + d * (skull(d) + CAP_T * (1 - f) - 0.02 * f)

    def crown_paint(u):
        if u.z < cos_rim:
            return (*CAP_SEAM, 1.0)
        az = math.atan2(u.x, -u.y)  # 0 = front
        ring = math.sqrt(max(0.0, 1 - u.z * u.z))
        c = CAP_CREAM if abs(az) < 0.95 else CAP_ORANGE
        seam = max(smooth(0.03, 0.012, abs(abs(az) - a) * ring) for a in (0.95, 2.05, math.pi))
        band = smooth(cos_rim + 0.07, cos_rim + 0.05, u.z)
        return (*lerp(lerp(c, CAP_SEAM, 0.75 * seam), lerp(c, CAP_SEAM, 0.35), band), 1.0)

    parts = [sphere_part("CapCrown", seg, rings, crown_point, crown_paint if painted else None)[0]]

    button = crown_point(Vector((0, 0, 1))) - tilt @ Vector((0, 0, 0.002))
    parts.append(sphere_part("CapButton", max(8, seg // 3), max(6, rings // 3),
                             lambda u: button + Vector((u.x * 0.016, u.y * 0.016, u.z * 0.009)),
                             (lambda u: (*CAP_ORANGE, 1.0)) if painted else None)[0])

    # Brim: a flattened ellipse in front of the forehead, sides drooping, pitched up a little.
    rim = crown_point(Vector((0, -sin_rim, cos_rim))) - tilt @ Vector((0, -sin_rim, cos_rim)) * 0.008
    pitch = 0.25
    fwd, up = Vector((0, -math.cos(pitch), math.sin(pitch))), Vector((0, math.sin(pitch), math.cos(pitch)))
    brim_c = rim + fwd * 0.1
    w, l, t = 0.21, 0.15, 0.011

    def brim_point(u):
        x, f = u.x * w, -u.y * l
        droop = 0.05 * (u.x ** 2) + 0.02 * max(0.0, u.y) ** 2
        return brim_c + Vector((x, 0, 0)) + fwd * f + up * (u.z * t - droop)

    def brim_paint(u):
        return (*lerp(CAP_ORANGE, CAP_SEAM, 0.45 * smooth(0.0, -0.6, u.z) + 0.5 * smooth(0.82, 0.95, -u.y)
                      * smooth(-0.3, 0.3, u.z)), 1.0)

    parts.append(sphere_part("CapBrim", seg, max(8, rings // 2), brim_point, brim_paint if painted else None)[0])
    return parts


_TS = np.linspace(0.0, TAIL_LEN, 401)
_TA = TAIL_PITCH * (1 - _TS / TAIL_LEN) ** 1.6 - TAIL_CURL * smooth(0.62, 1.0, _TS / TAIL_LEN)
_TY = TAIL_BASE[1] + np.concatenate(([0.0], np.cumsum(np.cos(_TA[:-1]) * np.diff(_TS))))
_TZ = TAIL_BASE[2] - np.concatenate(([0.0], np.cumsum(np.sin(_TA[:-1]) * np.diff(_TS))))


def tail_frame(s):
    """Centre point and 'up' (paddle normal) of the tail centre-line at arc length s."""
    a = float(np.interp(s, _TS, _TA))
    return Vector((0.0, float(np.interp(s, _TS, _TY)), float(np.interp(s, _TS, _TZ)))), \
        Vector((0.0, math.sin(a), math.cos(a)))


def tail_t(u):
    return (1 - u.z) / 2  # sphere pole +Z is the root, -Z the tip


def tail_width(t):
    return TAIL_W * (0.38 + 0.62 * smooth(0.0, 0.42, t))


def make_tail(seg, rings, painted):
    """Broad oval paddle: dips from the lower back and lies along the ground, tip lifting a little."""
    def shape(u):
        t = tail_t(u)
        c, up = tail_frame(t * TAIL_LEN)
        return c + Vector((u.x * tail_width(t), 0, 0)) + up * (u.y * TAIL_H * (1.35 - 0.55 * t))

    def paint(u):
        t = tail_t(u)
        rim = smooth(0.45, 0.12, abs(u.y)) * smooth(0.12, 0.25, t)  # darker thin edge around the paddle
        return (*lerp(lerp(FUR, TAIL, smooth(0.08, 0.2, t)), TAIL_DARK, 0.75 * rim), 1.0)

    def pattern(u):
        t = tail_t(u)
        flat = smooth(0.25, 0.5, abs(u.y))
        return (u.x * tail_width(t), t * TAIL_LEN, smooth(0.14, 0.26, t) * flat)

    return sphere_part("Tail", seg, rings, shape, paint if painted else None, pattern)


# --------------------------------------------------------------------------------------------- materials

def paint_shader():
    """Emission shader that paints lighting into the albedo: half-lambert key + AO through a warm shadow ramp,
    soft highlights, bounce light, fur clumps + streaks, and the tail's waffle grid."""
    mat = bpy.data.materials.new("M_BeaverPaint")
    try:
        mat.use_nodes = True
    except AttributeError:
        pass
    nt = mat.node_tree
    nodes, links = nt.nodes, nt.links
    nodes.clear()

    def node(kind, **props):
        n = nodes.new(kind)
        for k, v in props.items():
            setattr(n, k, v)
        return n

    def feed(sock, val):
        if isinstance(val, (int, float, tuple)):
            sock.default_value = val
        else:
            links.new(val, sock)

    def math_(op, *args):
        n = node("ShaderNodeMath", operation=op)
        for i, a in enumerate(args):
            feed(n.inputs[i], a)
        return n.outputs[0]

    def vec(op, a, b=None, scale=None):
        n = node("ShaderNodeVectorMath", operation=op)
        feed(n.inputs[0], a)
        if b is not None:
            feed(n.inputs[1], b)
        if scale is not None:
            feed(n.inputs["Scale"], scale)
        return n.outputs["Value" if op == 'DOT_PRODUCT' else "Vector"]

    def remap(val, a, b, lo, hi):
        n = node("ShaderNodeMapRange", clamp=True)
        feed(n.inputs["Value"], val)
        for key, v in (("From Min", a), ("From Max", b), ("To Min", lo), ("To Max", hi)):
            n.inputs[key].default_value = v
        return n.outputs["Result"]

    def attr(name, out="Fac"):
        return node("ShaderNodeAttribute", attribute_name=name).outputs[out]

    def scaled(vector, scale):
        m = node("ShaderNodeMapping")
        m.inputs["Scale"].default_value = scale
        links.new(vector, m.inputs["Vector"])
        return m.outputs["Vector"]

    paint, paint_a, fur = attr("Paint", "Color"), attr("Paint", "Alpha"), attr("Fur")

    # Tail: waffle grid in tail space, dark grooves + puffy lighter squares.
    u, v, tmask = attr("TailU"), attr("TailV"), attr("TailMask")
    du = math_('PINGPONG', math_('MULTIPLY', u, 15.0), 0.5)
    dv = math_('PINGPONG', math_('MULTIPLY', v, 15.0), 0.5)
    grooves = math_('MAXIMUM', remap(du, 0.04, 0.1, 1, 0), remap(dv, 0.04, 0.1, 1, 0))
    puff = math_('MULTIPLY', remap(du, 0.15, 0.5, 0, 1), remap(dv, 0.15, 0.5, 0, 1))
    hatch = math_('MULTIPLY_ADD', math_('SUBTRACT', math_('MULTIPLY', puff, 0.22), math_('MULTIPLY', grooves, 0.5)),
                  tmask, 1.0)
    base = vec('SCALE', paint, scale=hatch)

    normal = node("ShaderNodeNewGeometry").outputs["Normal"]
    half = math_('MULTIPLY_ADD', vec('DOT_PRODUCT', normal, tuple(LIGHT)), 0.5, 0.5)
    ao = node("ShaderNodeAmbientOcclusion", samples=16, only_local=True)
    ao.inputs["Distance"].default_value = 0.12
    crevice = node("ShaderNodeAmbientOcclusion", samples=16, only_local=True)  # dark painted creases
    crevice.inputs["Distance"].default_value = 0.03
    ao_term = math_('MULTIPLY', math_('MULTIPLY_ADD', ao.outputs["AO"], 0.55, 0.45),
                    math_('MULTIPLY_ADD', crevice.outputs["AO"], 0.6, 0.4))

    coords = node("ShaderNodeTexCoord").outputs["Object"]
    xyz = node("ShaderNodeSeparateXYZ")
    links.new(coords, xyz.inputs[0])
    height = remap(xyz.outputs["Z"], 0.0, 1.1, 0.86, 1.04)  # a touch darker towards the feet

    # Fur brushwork: clumps (stretched Voronoi cells lit on top, dark at their lower edge) + fine streaks.
    clump_space = scaled(coords, (34.0, 34.0, 13.0))
    vor = node("ShaderNodeTexVoronoi")
    links.new(clump_space, vor.inputs["Vector"])
    vor.inputs["Randomness"].default_value = 0.85
    offset = node("ShaderNodeSeparateXYZ")
    links.new(vec('SUBTRACT', clump_space, vor.outputs["Position"]), offset.inputs[0])
    clump = math_('ADD', remap(offset.outputs["Z"], -0.55, 0.45, -0.15, 0.09),
                  remap(vor.outputs["Distance"], 0.25, 0.6, 0.0, -0.07))
    streak_noise = node("ShaderNodeTexNoise")
    links.new(scaled(coords, (90.0, 90.0, 22.0)), streak_noise.inputs["Vector"])
    streak_noise.inputs["Scale"].default_value = 1.0
    streak_noise.inputs["Detail"].default_value = 2.0
    streaks = math_('MULTIPLY_ADD', streak_noise.outputs["Fac"], 0.2, -0.1)
    blot = node("ShaderNodeTexNoise")
    links.new(coords, blot.inputs["Vector"])
    blot.inputs["Scale"].default_value = 6.0
    blotches = math_('MULTIPLY_ADD', blot.outputs["Fac"], 0.12, -0.06)
    brush = math_('MULTIPLY', math_('ADD', math_('ADD', clump, streaks), blotches), fur)

    shade = math_('ADD', math_('MULTIPLY', math_('MULTIPLY', half, ao_term), height), brush)

    ramp = node("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = 'EASE'
    cr.elements[0].position, cr.elements[0].color = 0.05, (0.26, 0.16, 0.15, 1)  # warm, deep shadow
    cr.elements[1].position, cr.elements[1].color = 0.86, (1.0, 0.96, 0.9, 1)
    for pos, c in ((0.34, (0.52, 0.38, 0.35, 1)), (0.58, (0.84, 0.74, 0.69, 1))):
        cr.elements.new(pos).color = c
    links.new(shade, ramp.inputs["Fac"])

    lit = vec('MULTIPLY', base, ramp.outputs["Color"])
    warm = vec('MULTIPLY', base, (1.55, 1.42, 1.2))
    lit = vec('ADD', lit, vec('SCALE', warm, scale=remap(shade, 0.66, 0.98, 0.0, 0.26)))       # highlight
    nz = node("ShaderNodeSeparateXYZ")
    links.new(normal, nz.inputs[0])
    lit = vec('ADD', lit, vec('SCALE', warm, scale=remap(nz.outputs["Z"], -0.2, -0.9, 0.0, 0.08)))  # bounce
    final = vec('ADD', base, vec('SCALE', vec('SUBTRACT', lit, base), scale=paint_a))

    emit = node("ShaderNodeEmission")
    links.new(final, emit.inputs["Color"])
    out = node("ShaderNodeOutputMaterial")
    links.new(emit.outputs["Emission"], out.inputs["Surface"])
    return mat


def final_material(image):
    """Albedo -> Principled for export; the Emission node is only wired up for unlit previews."""
    mat = C.material("M_Beaver", (1, 1, 1), 0.85, **{"Specular IOR Level": 0.15})
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.nodes.active = tex  # bake target
    return mat


def show_unlit(mat, unlit):
    nt = mat.node_tree
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    src = next(n for n in nt.nodes if n.type == ('EMISSION' if unlit else 'BSDF_PRINCIPLED'))
    nt.links.new(src.outputs[0], out.inputs["Surface"])


def single_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.material_index = 0


# --------------------------------------------------------------------------------------------- rig

def bone_list():
    bones = [("Hips", (0, 0, 0.27), (0, 0, 0.40), None),
             ("Spine", (0, 0, 0.40), (0, 0, 0.52), "Hips"),
             ("Chest", (0, 0, 0.52), (0, 0, 0.65), "Spine"),
             ("Neck", (0, -0.01, 0.65), (0, -0.01, 0.72), "Chest"),
             ("Head", (0, -0.01, 0.72), (0, -0.01, 1.18), "Neck")]
    for side, sfx in ((1, ".L"), (-1, ".R")):
        m = lambda p: mirrored(p, side)  # noqa: E731
        toe = (FOOT_C[0], -0.10, 0.03)
        bones += [("Shoulder" + sfx, m((0.05, 0, 0.61)), m(SHOULDER), "Chest"),
                  ("UpperArm" + sfx, m(SHOULDER), m(ELBOW), "Shoulder" + sfx),
                  ("LowerArm" + sfx, m(ELBOW), m(WRIST), "UpperArm" + sfx),
                  ("Hand" + sfx, m(WRIST), m((0.425, -0.055, 0.275)), "LowerArm" + sfx),
                  ("UpperLeg" + sfx, m(HIP), m(KNEE), "Hips"),
                  ("LowerLeg" + sfx, m(KNEE), m(ANKLE), "UpperLeg" + sfx),
                  ("Foot" + sfx, m(ANKLE), m(toe), "LowerLeg" + sfx),
                  ("Toes" + sfx, m(toe), m((FOOT_C[0], -0.17, 0.03)), "Foot" + sfx)]
    parent = "Hips"
    for i in range(3):
        head, tail = tail_frame(TAIL_LEN * i / 3)[0], tail_frame(TAIL_LEN * (i + 1) / 3)[0]
        bones.append(("Tail_%d" % (i + 1), tuple(head), tuple(tail), parent))
        parent = "Tail_%d" % (i + 1)
    return bones


def build_armature():
    data = bpy.data.armatures.new("ARM_Beaver")
    arm = bpy.data.objects.new("ARM_Beaver", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, head, tail, parent in bone_list():
        b = data.edit_bones.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = data.edit_bones[parent]
            b.use_connect = (b.parent.tail - b.head).length < 1e-6
    bpy.ops.object.mode_set(mode='OBJECT')
    data.display_type = 'STICK'
    arm.show_in_front = True
    return arm


def skin_body(body, arm):
    """Bone-heat weights on the (single, closed) body shell; any vertex heat misses goes to its nearest bone."""
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (body, arm))
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    groups = {g.index: g.name for g in body.vertex_groups}
    segs = {b.name: (b.head_local, b.tail_local) for b in arm.data.bones}
    orphans = 0
    for v in body.data.vertices:
        if sum(g.weight for g in v.groups if g.group in groups) > 1e-4:
            continue
        orphans += 1

        def d(name):
            a, b = segs[name]
            t = max(0.0, min(1.0, (v.co - a).dot(b - a) / (b - a).length_squared))
            return (v.co - (a + (b - a) * t)).length
        body.vertex_groups[min(segs, key=d)].add([v.index], 1.0, 'REPLACE')
    print("SKIN orphan verts fixed:", orphans)


def rigid_to(obj, bone):
    obj.vertex_groups.new(name=bone).add(range(len(obj.data.vertices)), 1.0, 'REPLACE')


def skin_tail(obj, units):
    knots = [(0.0, "Hips"), (1 / 6, "Tail_1"), (0.5, "Tail_2"), (5 / 6, "Tail_3")]
    groups = {name: obj.vertex_groups.new(name=name) for _, name in knots}
    for i, u in enumerate(units):
        t = tail_t(u)
        if t >= knots[-1][0]:
            groups["Tail_3"].add([i], 1.0, 'REPLACE')
            continue
        for (t0, b0), (t1, b1) in zip(knots, knots[1:]):
            if t0 <= t < t1:
                f = (t - t0) / (t1 - t0)
                groups[b0].add([i], 1 - f, 'REPLACE')
                groups[b1].add([i], f, 'REPLACE')
                break


def join(objs, name):
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs):
        bpy.ops.object.join()
    objs[0].name = objs[0].data.name = name
    return objs[0]


# --------------------------------------------------------------------------------------------- bake / export / preview

def smart_uv(obj):
    for o in bpy.context.view_layer.objects:
        o.select_set(o is obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake(high, low, image):
    scene = bpy.context.scene
    C.setup_cycles(scene, samples=48)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (high, low))
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(type='EMIT', use_selected_to_active=True, cage_extrusion=0.015,
                        max_ray_distance=0.06, margin=12, use_clear=True)
    os.makedirs(TEXTURE_DIR, exist_ok=True)
    image.filepath_raw = os.path.join(TEXTURE_DIR, "T_Beaver_Albedo.png")
    image.file_format = 'PNG'
    image.save()


def export_fbx(arm, mesh):
    os.makedirs(C.MODEL_DIR, exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (arm, mesh))
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(C.MODEL_DIR, "SK_Beaver.fbx"),
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=False,  # buggy with armatures; tick "Bake Axis Conversion" on the Unity importer
        use_mesh_modifiers=False,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode='RELATIVE',
    )


def pose(arm, world_rotations):
    """world_rotations: {bone: Quaternion} = each bone's total rotation from rest, in armature space."""
    world = {}
    for pb in arm.pose.bones:  # creation order = parents first
        parent_q = world.get(pb.parent.name, Quaternion()) if pb.parent else Quaternion()
        q = world_rotations.get(pb.name, parent_q)
        rest = pb.bone.matrix_local.to_quaternion()
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = rest.inverted() @ parent_q.inverted() @ q @ rest
        world[pb.name] = q
    bpy.context.view_layer.update()


def aim(arm, bone, direction):
    b = arm.data.bones[bone]
    return (b.tail_local - b.head_local).rotation_difference(Vector(direction).normalized())


def render_turnaround(mesh):
    """Front / 3-4 / side / back copies of the rest pose in one orthographic strip (copies removed after)."""
    scene = bpy.context.scene
    stage = bpy.data.collections["_Preview"]
    copies = []
    for i, deg in enumerate((0, 35, 90, 180)):
        c = mesh.copy()
        c.parent = None
        c.modifiers.clear()
        c.matrix_world = Matrix.Translation(((i - 1.5) * 1.15, 0, 0)) @ Matrix.Rotation(math.radians(deg), 4, 'Z')
        stage.objects.link(c)
        copies.append(c)
    mesh.hide_render = True
    cam_data = bpy.data.cameras.new("TurnCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 4.8
    cam = bpy.data.objects.new("TurnCam", cam_data)
    stage.objects.link(cam)
    cam.location = (0, -6, 1.3)
    cam.rotation_euler = (Vector((0, 0, 0.55)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    C.render_to("Beaver_turnaround", (2000, 640))
    for c in copies:
        bpy.data.objects.remove(c)
    mesh.hide_render = False


def render_face(mesh):
    """Close-up of the face, for judging the paint."""
    stage = bpy.data.collections["_Preview"]
    cam = bpy.data.objects.new("FaceCam", bpy.data.cameras.new("FaceCam"))
    cam.data.lens = 85
    stage.objects.link(cam)
    cam.location = (-0.45, -1.5, 1.05)
    cam.rotation_euler = (Vector((0, -0.1, 0.84)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    C.render_to("Beaver_face", (900, 900))


# --------------------------------------------------------------------------------------------- main

def main():
    C.reset()
    deps = bpy.context.evaluated_depsgraph_get

    # High: dense sculpt + face/tail parts, all painted.
    body_hi = sculpt_body(0.0045)
    paint_body(body_hi)
    tree = BVHTree.FromObject(body_hi, deps())
    hi_parts = (make_eyes(tree, 64, 32, True) + [make_nose(tree, 48, 24, True)] + make_teeth(tree, 40, 24, True)
                + make_cap(tree, 96, 64, True) + make_tufts(tree, 8, 6, True) + [make_tail(80, 72, True)[0]])

    # Low: decimated body + light versions of the parts, skinned per part, then joined.
    body_lo = body_hi.copy()
    body_lo.data = body_hi.data.copy()
    bpy.context.scene.collection.objects.link(body_lo)
    for name in ("Paint",) + PAINT_ATTRS:
        body_lo.data.attributes.remove(body_lo.data.attributes[name])
    mod = body_lo.modifiers.new("Decimate", 'DECIMATE')
    mod.ratio = BODY_TRIS / sum(len(p.vertices) - 2 for p in body_lo.data.polygons)
    C.apply_modifiers(body_lo)
    body_lo.data.shade_smooth()

    high = join([body_hi] + hi_parts, "Beaver_High")
    single_material(high, paint_shader())

    arm = build_armature()
    skin_body(body_lo, arm)
    lo_parts = (make_eyes(tree, 18, 12, False) + [make_nose(tree, 16, 10, False)] + make_teeth(tree, 12, 10, False)
                + make_cap(tree, 32, 20, False))
    for p in lo_parts:
        rigid_to(p, "Head")
    tufts_lo = make_tufts(tree, 8, 6, False)  # deliberately chunky: low-poly spikes, the paint does the rest
    for p in tufts_lo:
        copy_weights(body_lo, p)
    tail_lo, tail_units = make_tail(18, 32, False)
    skin_tail(tail_lo, tail_units)
    body = join([body_lo] + lo_parts + tufts_lo + [tail_lo], "SK_Beaver")

    image = bpy.data.images.new("T_Beaver_Albedo", TEX_SIZE, TEX_SIZE, alpha=False)
    mat = final_material(image)
    single_material(body, mat)
    smart_uv(body)
    bake(high, body, image)

    bake_src = bpy.data.collections.new("_BakeSource")  # kept in the .blend for re-bakes
    bpy.context.scene.collection.children.link(bake_src)
    bake_src.objects.link(high)
    bpy.context.scene.collection.objects.unlink(high)
    high.hide_render = True
    high.hide_set(True)

    export_fbx(arm, body)

    show_unlit(mat, True)
    C.render_preview("Beaver", [body], view_dir=(-0.9, -1.6, 0.5), backdrop_color=BACKDROP)
    hero_cam = bpy.context.scene.camera
    render_turnaround(body)
    render_face(body)
    pose(arm, {"UpperArm.L": aim(arm, "UpperArm.L", (0.55, -0.1, 0.83)),
               "LowerArm.L": aim(arm, "LowerArm.L", (0.2, -0.3, 0.93)),
               "UpperArm.R": aim(arm, "UpperArm.R", (-0.35, -0.2, -0.9)),
               "Head": Quaternion((0, 1, 0), 0.14),
               "Tail_1": Quaternion((1, 0, 0), 0.55), "Tail_2": Quaternion((1, 0, 0), 1.05),
               "Tail_3": Quaternion((1, 0, 0), 1.2)})
    bpy.context.scene.camera = hero_cam
    C.render_to("Beaver_pose", (1200, 900))
    pose(arm, {})
    show_unlit(mat, False)

    image.filepath = "//" + os.path.relpath(image.filepath_raw, C.BLEND_DIR).replace("\\", "/")
    C.save_blend("Beaver")
    print("TRIS", sum(len(p.vertices) - 2 for p in body.data.polygons), "VERTS", len(body.data.vertices),
          "HEIGHT", round(max(v.co.z for v in body.data.vertices), 3),
          "TAIL CENTRE MIN Z", round(min(tail_frame(s)[0].z for s in np.linspace(0, TAIL_LEN, 80)), 3))


if __name__ == "__main__":
    main()
