"""Bevelled ramp slabs for the levels' plain ramps (a cube scaled to width x 0.4 x slope length): the same rounded
edges as the platform blocks (0.16 m, 6 segments), one mesh per ramp size.

Each mesh is the real-size slab squeezed back into a unit cube (-0.5..0.5) with its normals set for the squeeze, so
it drops into the ramp's MeshFilter in place of Unity's cube: the ramp keeps its transform and BoxCollider, and once
Unity scales it the bevel is the same 0.16 m on every edge and the shading is right.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

THICKNESS = 0.4          # LevelParts.RampThickness
BEVEL = 0.16
SEGMENTS = 6

# Unity scale (x width, y thickness, z slope length) of each ramp in the levels.
RAMPS = [
    ("SM_Ramp_W150_L427", 1.5, 4.2720017),      # Level03 (Ramp.prefab)
    ("SM_Ramp_W200_L468", 2.0, 4.678087),       # Sandbox, Pool Ramp
    ("SM_Ramp_W400_L773", 4.0, 7.7274065),      # Sandbox, Ramp And Platform
    ("SM_Ramp_W400_L580", 4.0, 5.795555),       # TESTLevel01
]


def corner_normals(me):
    if hasattr(me, "corner_normals"):
        return [Vector(n.vector) for n in me.corner_normals]
    me.calc_normals_split()
    return [Vector(l.normal) for l in me.loops]


def slab(name, width, length, mat):
    # Blender x = Unity x (width), Blender y = Unity z (length), Blender z = Unity y (thickness)
    size = Vector((width, length, THICKNESS))
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=BEVEL, offset_type='OFFSET', segments=SEGMENTS,
                    profile=0.5, affect='EDGES', clamp_overlap=True)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(mat)
    me.shade_smooth()

    # Flat faces stay flat, the rounded edges blend: normals weighted by face area, as on the platform blocks.
    mod = obj.modifiers.new("Weighted", 'WEIGHTED_NORMAL')
    mod.mode = 'FACE_AREA'
    mod.keep_sharp = True
    C.apply_modifiers(obj)
    me = obj.data
    real = corner_normals(me)

    # Squeeze into the unit cube; a normal n of the real slab becomes S*n here, so Unity's inverse-transpose of the
    # same scale S turns it back into n.
    for v in me.vertices:
        v.co = Vector((v.co.x / size.x, v.co.y / size.y, v.co.z / size.z))
    me.normals_split_custom_set([Vector((n.x * size.x, n.y * size.y, n.z * size.z)).normalized() for n in real])
    C.box_uvs(obj)
    return obj


def main():
    C.reset()
    yellow = C.material("M_RampYellow", (1.0, 0.882, 0.478), 0.6)
    objs = [slab(name, w, l, yellow) for name, w, l in RAMPS]
    C.export_fbx(objs, "SM_Ramp")

    # Preview at real size, side by side.
    shown = []
    x = 0.0
    for (name, w, l), o in zip(RAMPS, objs):
        p = o.copy()
        p.data = o.data
        bpy.context.scene.collection.objects.link(p)
        p.scale = (w, l, THICKNESS)
        p.rotation_euler = (math.radians(20), 0, 0)
        p.location = (x + w / 2, 0, l * 0.18)
        x += w + 0.8
        shown.append(p)
    for o in objs:
        o.hide_render = True
    C.render_preview("Ramp", shown, view_dir=(0.9, -1.4, 0.9), size=(1400, 800))
    C.save_blend("Ramp")


main()
