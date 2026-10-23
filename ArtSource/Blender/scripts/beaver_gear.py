"""Astroneer-style gear for the beaver: backpack with a glass back dome + terrain tool on a ribbed hose.

Everything is built around the beaver's rest-pose body (sampled with ray casts against its sculpt), so the
pack's front face and the straps hug its back, shoulders and belly, and the tool's grip sits in the right
paw. Flat PBR materials (white shell, orange trim, dark rubber, glass, emissive core), no bake.

beaver.py skins these to the rig and also exports two stand-alone props:
  SM_BeaverBackpack     pivot = centre of the pack's contact face, glass dome facing +Y
  SM_BeaverTerrainTool  pivot = grip centre, barrel along -Y, top +Z
"""
import math
import os
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

bpy = C.bpy

PACK_W, PACK_H, PACK_D, PACK_Z, PACK_GAP = 0.155, 0.185, 0.12, 0.50, 0.008
TOOL_BZ = 0.095  # barrel axis height above the grip centre (clears the forearm)
TOOL_PORT = Vector((0.0, 0.13, TOOL_BZ))  # hose socket at the back of the tool, local


def glow(name, color, strength):
    return C.material(name, color, 0.3, **{"Emission Color": (*color, 1.0), "Emission Strength": strength})


def materials():
    return {
        "white": C.material("M_GearWhite", (0.86, 0.87, 0.88), 0.32, **{"Coat Weight": 0.4}),
        "orange": C.material("M_GearOrange", (1.0, 0.36, 0.04), 0.38, **{"Coat Weight": 0.3}),
        "dark": C.material("M_GearDark", (0.045, 0.05, 0.06), 0.55),
        "metal": C.material("M_GearMetal", (0.62, 0.64, 0.68), 0.28, **{"Metallic": 1.0}),
        "glass": C.material("M_GearGlass", (0.9, 0.97, 1.0), 0.02, **{"Transmission Weight": 1.0, "IOR": 1.45}),
        "core": glow("M_GearCore", (0.15, 1.0, 0.85), 8.0),
        "oxygen": glow("M_GearOxygen", (0.2, 0.6, 1.0), 4.0),
        "power": glow("M_GearPower", (1.0, 0.75, 0.1), 4.0),
    }


# --------------------------------------------------------------------------------------------- mesh helpers

def _obj(name, bm, mat, smooth_angle=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(mat)
    if smooth_angle:
        C.shade_auto(obj, smooth_angle)
    else:
        me.shade_smooth()
    return obj


def sq(a, e):
    return math.copysign(abs(a) ** e, a)


def blob(name, mat, matrix, radii, exps=(1.0, 1.0, 1.0), seg=24, rings=12, warp=None):
    """Super-ellipsoid (exps < 1 = boxier) through matrix. warp(u, p) may replace the local point."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        u = v.co.copy()
        p = Vector((sq(u.x, exps[0]) * radii[0], sq(u.y, exps[1]) * radii[1], sq(u.z, exps[2]) * radii[2]))
        v.co = matrix @ (warp(u, p) if warp else p)
    return _obj(name, bm, mat)


def lathe(name, mat, matrix, profile, seg=36):
    """Closed solid of revolution around local Y. profile: [(radius, y), ...] pole to pole (r = 0 ends)."""
    bm = bmesh.new()
    rings = []
    for r, y in profile:
        if r < 1e-6:
            rings.append([bm.verts.new(matrix @ Vector((0, y, 0)))])
        else:
            rings.append([bm.verts.new(matrix @ Vector((r * math.cos(a), y, r * math.sin(a))))
                          for a in (2 * math.pi * i / seg for i in range(seg))])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            if len(a) == 1:
                bm.faces.new((a[0], b[i], b[j]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, mat, smooth_angle=35)


def tube(name, mat, points, radius, seg=12, closed=False):
    """Round tube along points, radius(i) per station. Parallel-transport frames, so it never twists."""
    n = len(points)
    bm = bmesh.new()
    rings = []
    ref = None
    for i, p in enumerate(points):
        a = points[i - 1] if (closed or i > 0) else p
        b = points[(i + 1) % n] if (closed or i < n - 1) else p
        t = (b - a).normalized()
        ref = t.orthogonal().normalized() if ref is None else (ref - t * ref.dot(t)).normalized()
        side = t.cross(ref)
        r = radius(i)
        rings.append([bm.verts.new(p + (ref * math.cos(k * 2 * math.pi / seg) + side * math.sin(k * 2 * math.pi / seg)) * r)
                      for k in range(seg)])
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        for k in range(seg):
            bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    if not closed:
        bm.faces.new(rings[0])
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, mat)


def ring(name, mat, matrix, radius, thickness, y=0.0, z_scale=1.0, seg=32):
    """Torus around local Y at height y (optionally stretched along Z)."""
    pts = [matrix @ Vector((radius * math.cos(a), y, radius * z_scale * math.sin(a)))
           for a in (2 * math.pi * i / seg for i in range(seg))]
    return tube(name, mat, pts, lambda i: thickness, seg=8, closed=True)


def strap(name, mat, points, normals, half_w, half_t):
    """Flat webbing strap swept along points, lying on the surface given by normals."""
    n = len(points)
    bm = bmesh.new()
    rings = []
    for i, (p, nrm) in enumerate(zip(points, normals)):
        t = (points[min(i + 1, n - 1)] - points[max(i - 1, 0)]).normalized()
        nn = (nrm - t * nrm.dot(t)).normalized()
        b = t.cross(nn)
        rings.append([bm.verts.new(p + b * sx * half_w + nn * st * half_t)
                      for sx, st in ((1, 1), (-1, 1), (-1, -1), (1, -1))])
    for a, b in zip(rings, rings[1:]):
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, mat, smooth_angle=40)


def on_surface(pos, normal):
    """Matrix at pos whose local +Y points along normal."""
    m = Vector((0, 1, 0)).rotation_difference(normal).to_matrix().to_4x4()
    m.translation = pos
    return m


def paint_faces(obj, mat, pick):
    """Give the faces whose (centre, normal) pass pick() an extra material slot."""
    me = obj.data
    me.materials.append(mat)
    idx = len(me.materials) - 1
    for poly in me.polygons:
        if pick(poly.center, poly.normal):
            poly.material_index = idx


def cubic(p0, p1, p2, p3, t):
    s = 1 - t
    return p0 * s ** 3 + p1 * 3 * s * s * t + p2 * 3 * s * t * t + p3 * t ** 3


# --------------------------------------------------------------------------------------------- body sampling

def ray(tree, origin, direction, from_inside=False):
    """(hit, outward normal) or (None, None). Rays from inside the body exit through the surface."""
    hit, n, _, _ = tree.ray_cast(Vector(origin), Vector(direction))
    if hit is None:
        return None, None
    facing_ray = n.dot(Vector(direction)) < 0
    return hit, (n if facing_ray != from_inside else -n)


def pack_y(tree, x, z, depth_t):
    """Y of the pack at (x, z): its front face rides the beaver's back, depth_t 0..1 front to back."""
    hit, _ = ray(tree, (x, 2.0, min(max(z, 0.38), 0.62)), (0, -1, 0))
    return (hit.y if hit else 0.15) + PACK_GAP + depth_t * PACK_D


# --------------------------------------------------------------------------------------------- backpack

def build_backpack(tree, mats):
    """Returns (parts, hose port position, hose port direction)."""
    def shell(u, p):
        ux, uy, uz = sq(u.x, 0.45), sq(u.y, 0.6), sq(u.z, 0.45)
        x = ux * PACK_W * (1 - 0.1 * (uz + 1) / 2)  # a touch narrower at the top
        z = PACK_Z + uz * PACK_H
        return Vector((x, pack_y(tree, x, z, (uy + 1) / 2), z))

    body = blob("PackShell", mats["white"], Matrix(), (1, 1, 1), seg=40, rings=26, warp=shell)
    paint_faces(body, mats["orange"], lambda c, n: PACK_Z - 0.135 < c.z < PACK_Z - 0.10)
    parts = [body]

    # Glass back dome with a glowing reactor core and coils inside.
    c = Vector((0.0, pack_y(tree, 0.0, PACK_Z, 1.0), PACK_Z + 0.01))
    at = Matrix.Translation(c)
    parts += [blob("PackPlate", mats["dark"], at, (0.08, 0.012, 0.115)),
              blob("PackCore", mats["core"], Matrix.Translation(c + Vector((0, 0.026, 0))), (0.03, 0.03, 0.03), seg=24, rings=12),
              ring("PackCoil", mats["metal"], at, 0.046, 0.0045, y=0.016, z_scale=1.4),
              ring("PackCoil", mats["metal"], at, 0.062, 0.0045, y=0.008, z_scale=1.4),
              blob("PackDome", mats["glass"], at, (0.085, 0.07, 0.122), seg=36, rings=18),
              ring("PackRim", mats["orange"], at, 0.087, 0.011, y=0.004, z_scale=1.42)]

    # Oxygen (blue) and power (yellow) gauges either side of the dome.
    for side, gauge in ((1, "oxygen"), (-1, "power")):
        x = side * 0.124
        y = pack_y(tree, x, PACK_Z, 1.0)
        parts += [blob("PackSlot", mats["dark"], Matrix.Translation((x, y - 0.004, PACK_Z)), (0.018, 0.012, 0.1), (0.5, 1, 0.5)),
                  blob("PackGauge", mats[gauge], Matrix.Translation((x, y + 0.004, PACK_Z - 0.012)), (0.009, 0.008, 0.075),
                       seg=16, rings=12)]

    # Carry handle and a little antenna on top.
    top = PACK_Z + PACK_H
    ym = pack_y(tree, 0.0, top, 0.55)
    handle = [Vector((x, ym, top - 0.018 + 0.042 * math.sqrt(max(0.0, 1 - (x / 0.06) ** 2))))
              for x in (-0.06 + 0.12 * i / 20 for i in range(21))]
    parts.append(tube("PackHandle", mats["dark"], handle, lambda i: 0.008, seg=10))
    ay = pack_y(tree, 0.105, top, 0.6)
    antenna = [Vector((0.105, ay + 0.03 * (i / 16) ** 2, top - 0.03 + 0.12 * i / 16)) for i in range(17)]
    parts += [tube("PackAntenna", mats["metal"], antenna, lambda i: 0.0055 - 0.0025 * i / 16, seg=8),
              blob("PackAntennaTip", mats["orange"], Matrix.Translation(antenna[-1]), (0.014, 0.014, 0.014), seg=16, rings=8),
              blob("PackAntennaBase", mats["dark"], Matrix.Translation(antenna[0] + Vector((0, 0, 0.02))), (0.016, 0.016, 0.012))]

    # Hose socket on the right side (the tool hand), pointing outwards.
    port_z = 0.40
    py = pack_y(tree, -0.14, port_z, 0.5)
    sock = Vector((-PACK_W * 0.98, py, port_z))
    parts += [blob("PackSocket", mats["orange"], Matrix.Translation(sock), (0.02, 0.025, 0.025), (0.3, 1, 1)),
              blob("PackSocketCap", mats["dark"], Matrix.Translation(sock + Vector((-0.016, 0, 0))), (0.008, 0.018, 0.018), (0.4, 1, 1))]
    return parts, sock + Vector((-0.018, 0, 0)), Vector((-1, 0, 0))


def build_straps(tree, mats):
    """Shoulder straps traced over the body from the pack to the chest, plus a chest strap with a clip."""
    parts, ends = [], []
    for side in (1, -1):
        pts, nrms = [], []
        for i in range(41):
            th = math.pi * (0.22 + 0.86 * i / 40)  # from the upper back, over the shoulder, down the chest
            # Swing out to x=0.195 on top: nearer the middle the head is fused onto the shoulders.
            origin = Vector((side * (0.13 + 0.065 * math.sin(th) ** 2), 0.02, 0.50))
            hit, n = ray(tree, origin, (0, math.cos(th), math.sin(th)), from_inside=True)
            if hit and hit.z < 0.74:
                pts.append(hit + n * 0.011)
                nrms.append(n)
        parts.append(strap("Strap", mats["dark"], pts, nrms, 0.022, 0.006))
        parts.append(blob("Buckle", mats["orange"], on_surface(pts[-1] + nrms[-1] * 0.004, nrms[-1]),
                          (0.027, 0.009, 0.03), (0.5, 1, 0.5)))
        ends.append(pts[-1])

    zc = (ends[0].z + ends[1].z) / 2
    pts, nrms = [], []
    for i in range(25):
        hit, n = ray(tree, (-0.125 + 0.25 * i / 24, -2.0, zc), (0, 1, 0))
        if hit:
            pts.append(hit + n * 0.009)
            nrms.append(n)
    parts.append(strap("ChestStrap", mats["dark"], pts, nrms, 0.012, 0.004))
    mid, mn = pts[len(pts) // 2], nrms[len(nrms) // 2]
    parts += [blob("ChestClip", mats["white"], on_surface(mid + mn * 0.006, mn), (0.026, 0.01, 0.02), (0.5, 1, 0.5)),
              blob("ChestClipLight", mats["core"], on_surface(mid + mn * 0.015, mn), (0.007, 0.004, 0.007), seg=12, rings=8)]
    return parts


# --------------------------------------------------------------------------------------------- terrain tool

def build_tool(mats, matrix):
    """Terrain tool in its own frame (grip centre at origin, barrel -Y, top +Z), placed by matrix."""
    def at(v, rot=None):
        return matrix @ Matrix.Translation(v) @ (rot or Matrix())

    bz = TOOL_BZ
    axis = at((0, 0, bz))
    parts = [
        blob("ToolBody", mats["white"], at((0, -0.01, bz)), (0.042, 0.105, 0.046), (0.75, 0.6, 0.75), 32, 18),
        blob("ToolRidge", mats["orange"], at((0, 0.005, bz + 0.042)), (0.017, 0.07, 0.016), (0.6, 0.6, 0.8)),
        blob("ToolStrip", mats["core"], at((0, -0.005, bz + 0.057)), (0.006, 0.045, 0.004), seg=16, rings=8),
        blob("ToolGrip", mats["dark"], at((0, 0.014, 0.0), Matrix.Rotation(math.radians(-14), 4, 'X')),
             (0.021, 0.028, 0.075), (0.8, 0.8, 0.6)),
        blob("ToolTrigger", mats["orange"], at((0, -0.032, 0.04)), (0.006, 0.009, 0.016), seg=16, rings=8),
        # Flared emitter with an orange rim, a glowing lens and a dark focus ring.
        lathe("ToolEmitter", mats["white"], axis,
              [(0, -0.06), (0.040, -0.06), (0.046, -0.095), (0.066, -0.15), (0.074, -0.162), (0.070, -0.172), (0, -0.172)]),
        lathe("ToolLens", mats["core"], axis, [(0, -0.171), (0.056, -0.171), (0.054, -0.176), (0, -0.177)]),
        ring("ToolRim", mats["orange"], axis, 0.071, 0.007, y=-0.165),
        ring("ToolFocus", mats["dark"], axis, 0.034, 0.004, y=-0.177),
        blob("ToolNub", mats["dark"], at((0, -0.178, bz)), (0.012, 0.006, 0.012), seg=16, rings=8),
        # Rear hose socket.
        lathe("ToolSocket", mats["dark"], axis, [(0, 0.085), (0.03, 0.085), (0.031, 0.12), (0.022, 0.132), (0, 0.132)]),
        ring("ToolSocketRing", mats["orange"], axis, 0.036, 0.006, y=0.088),
    ]
    for sx in (1, -1):
        for k in range(3):
            parts.append(blob("ToolVent", mats["dark"], at((sx * 0.04, 0.002 + 0.022 * k, bz)), (0.004, 0.007, 0.018),
                              seg=12, rings=8))
    return parts


def build_hose(mats, start, start_dir, end, end_dir, stations=180):
    """Ribbed hose from the pack socket to the tool. Returns (object, station t 0..1 per ring, verts per ring)."""
    p0, p3 = Vector(start), Vector(end)
    p1 = p0 + Vector(start_dir) * 0.12 + Vector((0, 0.03, -0.14))
    p2 = p3 + Vector(end_dir) * 0.14 + Vector((0, 0.02, -0.06))
    pts = [cubic(p0, p1, p2, p3, i / (stations - 1)) for i in range(stations)]
    arc = [0.0]
    for a, b in zip(pts, pts[1:]):
        arc.append(arc[-1] + (b - a).length)
    seg = 10
    obj = tube("Hose", mats["dark"], pts,
               lambda i: 0.0115 + 0.0025 * (0.5 + 0.5 * math.cos(2 * math.pi * arc[i] / 0.016)), seg=seg)
    return obj, [i / (stations - 1) for i in range(stations)], seg
