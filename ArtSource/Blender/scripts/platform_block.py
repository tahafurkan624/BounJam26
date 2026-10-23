"""Level kit: chunky 2 x 2 x 2 m platform block with a checkered top. Top face at z=0, pivot top centre,
so blocks snap on a 2 m grid and their tops line up by height.

  SM_Platform_2x2   material 0 = checker top (M_PlatformTop), material 1 = sides (M_PlatformSide)
UVs are box-projected in metres (1 UV unit = 1 m), so a 0.5 m checker texture tiles across neighbours.
"""
import os
import sys

import bmesh

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

SIZE = 2.0
HEIGHT = 2.0
TOP_A = (1.0, 0.90, 0.62)
TOP_B = (1.0, 0.70, 0.30)
SIDE = (0.97, 0.95, 0.90)
CHECKS_PER_METRE = 2


def checker_material(name, a, b, checks_per_metre):
    mat = C.material(name, a, 0.35, **{"Coat Weight": 0.2})
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    coords = nt.nodes.new("ShaderNodeTexCoord")
    checker = nt.nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = checks_per_metre
    checker.inputs["Color1"].default_value = (*a, 1.0)
    checker.inputs["Color2"].default_value = (*b, 1.0)
    nt.links.new(coords.outputs["UV"], checker.inputs["Vector"])
    nt.links.new(checker.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def build_block(name, size=SIZE, height=HEIGHT, top_mat=None, side_mat=None):
    me = C.bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(size, size, height), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, -height / 2), verts=bm.verts)
    bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=0.16, offset_type='OFFSET',
                    segments=5, profile=0.5, affect='EDGES', clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.material_index = 0 if f.normal.z > 0.75 else 1
    bm.to_mesh(me)
    bm.free()
    obj = C.bpy.data.objects.new(name, me)
    C.bpy.context.scene.collection.objects.link(obj)
    me.materials.append(top_mat)
    me.materials.append(side_mat)
    C.box_uvs(obj)
    C.shade_auto(obj, 35)
    return obj


def materials():
    return (checker_material("M_PlatformTop", TOP_A, TOP_B, CHECKS_PER_METRE),
            C.material("M_PlatformSide", SIDE, 0.4, **{"Coat Weight": 0.2}))


def main():
    C.reset()
    top, side = materials()
    block = build_block("SM_Platform_2x2", top_mat=top, side_mat=side)
    C.export_fbx([block], "SM_Platform_2x2")
    C.render_preview("Platform_2x2", [block], view_dir=(1.4, -1.2, 1.0))
    C.save_blend("Platform_2x2")


if __name__ == "__main__":
    main()
