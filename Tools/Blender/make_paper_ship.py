"""Builds the low-poly paper boat and exports it for Unity.

Run headless (paths are relative to the repository root):
    blender -b --factory-startup --python Tools/Blender/make_paper_ship.py -- \
        --fbx Assets/_Game/Art/Models/PaperShip.fbx --blend ArtSource/PaperShip.blend

Shape: the classic folded newspaper boat. Two slanted side panels meet at the keel, the bow and stern rise to
points at deck height, and a tall double-sided sail stands in the middle. A solidify modifier gives the paper a
real (thin) thickness so both sides shade correctly, and flat shading keeps every fold crisp.
Blender axes here: X = width, Y = length (bow at +Y), Z = up. Origin at the bottom of the keel.
"""

import argparse
import os
import sys

import bmesh
import bpy

KEEL_HALF = 0.20       # half length of the flat keel line
TIP_HALF = 0.33        # half length at deck height (bow/stern points)
DECK_HALF_WIDTH = 0.11
SHOULDER = 0.12        # where the deck edge turns from the sides toward the tips
DECK_HEIGHT = 0.13
SAIL_HALF_LENGTH = 0.13
SAIL_HALF_WIDTH = 0.02
SAIL_HEIGHT = 0.30
PAPER_THICKNESS = 0.008
PAPER_COLOR = (0.93, 0.91, 0.84, 1.0)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--fbx", required=True)
    parser.add_argument("--blend")
    return parser.parse_args(argv)


def build_mesh():
    v = {
        "keel_stern": (0.0, -KEEL_HALF, 0.0),
        "keel_bow": (0.0, KEEL_HALF, 0.0),
        "stern": (0.0, -TIP_HALF, DECK_HEIGHT),
        "bow": (0.0, TIP_HALF, DECK_HEIGHT),
        "right_aft": (DECK_HALF_WIDTH, -SHOULDER, DECK_HEIGHT),
        "right_fore": (DECK_HALF_WIDTH, SHOULDER, DECK_HEIGHT),
        "left_aft": (-DECK_HALF_WIDTH, -SHOULDER, DECK_HEIGHT),
        "left_fore": (-DECK_HALF_WIDTH, SHOULDER, DECK_HEIGHT),
        "sail_right_aft": (SAIL_HALF_WIDTH, -SAIL_HALF_LENGTH, DECK_HEIGHT),
        "sail_right_fore": (SAIL_HALF_WIDTH, SAIL_HALF_LENGTH, DECK_HEIGHT),
        "sail_left_aft": (-SAIL_HALF_WIDTH, -SAIL_HALF_LENGTH, DECK_HEIGHT),
        "sail_left_fore": (-SAIL_HALF_WIDTH, SAIL_HALF_LENGTH, DECK_HEIGHT),
        "sail_top": (0.0, 0.0, SAIL_HEIGHT),
    }
    names = list(v)
    index = {name: i for i, name in enumerate(names)}

    faces = [
        # Side panels, each split along a diagonal crease so the paper reads as folded.
        ("keel_stern", "right_aft", "right_fore"), ("keel_stern", "right_fore", "keel_bow"),
        ("keel_stern", "keel_bow", "left_fore"), ("keel_stern", "left_fore", "left_aft"),
        # Bow and stern points.
        ("keel_bow", "right_fore", "bow"), ("keel_bow", "bow", "left_fore"),
        ("keel_stern", "stern", "right_aft"), ("keel_stern", "left_aft", "stern"),
        # Sail: a narrow tent standing on the deck line.
        ("sail_right_aft", "sail_top", "sail_right_fore"),
        ("sail_left_fore", "sail_top", "sail_left_aft"),
        ("sail_right_fore", "sail_top", "sail_left_fore"),
        ("sail_left_aft", "sail_top", "sail_right_aft"),
    ]

    mesh = bpy.data.meshes.new("PaperShip")
    mesh.from_pydata([v[n] for n in names], [], [tuple(index[n] for n in f) for f in faces])
    mesh.update()

    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    mesh = build_mesh()
    ship = bpy.data.objects.new("PaperShip", mesh)
    bpy.context.collection.objects.link(ship)
    bpy.context.view_layer.objects.active = ship
    ship.select_set(True)

    paper = ship.modifiers.new("Paper", "SOLIDIFY")
    paper.thickness = PAPER_THICKNESS
    paper.offset = 0.0
    paper.use_even_offset = True
    bpy.ops.object.modifier_apply(modifier=paper.name)

    for polygon in mesh.polygons:
        polygon.use_smooth = False

    material = bpy.data.materials.new("Paper")
    material.diffuse_color = PAPER_COLOR
    mesh.materials.append(material)

    fbx_path = os.path.abspath(args.fbx)
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
    )
    print(f"[make_paper_ship] exported {fbx_path}: {len(mesh.vertices)} vertices, {len(mesh.polygons)} faces")

    if args.blend:
        blend_path = os.path.abspath(args.blend)
        os.makedirs(os.path.dirname(blend_path), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
        print(f"[make_paper_ship] saved {blend_path}")


main()
