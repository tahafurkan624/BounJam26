"""Pushable toy block (1 m cube) with rounded edges and recessed panels. Only the solid form pushes it.

  SM_PushBlock   pivot at bottom centre. Use a BoxCollider (1 x 1 x 1) in Unity.
"""
import os
import sys

import bmesh

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

SIZE = 1.0
PANEL_INSET = 0.13
PANEL_DEPTH = 0.05


def main():
    C.reset()
    m_body = C.material("M_PushBlock", (0.30, 0.20, 0.95), 0.3, **{"Coat Weight": 0.3})
    m_panel = C.material("M_PushBlockPanel", (1.0, 0.72, 0.05), 0.35, **{"Coat Weight": 0.2})

    me = C.bpy.data.meshes.new("SM_PushBlock")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=SIZE)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, SIZE / 2))
    # Round the cube first; the six big flat faces that survive become the panels.
    bmesh.ops.bevel(bm, geom=list(bm.edges) + list(bm.verts), offset=0.08, offset_type='OFFSET',
                    segments=4, profile=0.5, affect='EDGES', clamp_overlap=True)
    panels = sorted(bm.faces, key=lambda f: f.calc_area(), reverse=True)[:6]
    bmesh.ops.inset_individual(bm, faces=panels, thickness=PANEL_INSET, depth=-PANEL_DEPTH)
    for f in panels:  # inset_individual keeps the originals as the inner panels
        f.material_index = 1
    bm.to_mesh(me)
    bm.free()

    block = C.bpy.data.objects.new("SM_PushBlock", me)
    C.bpy.context.scene.collection.objects.link(block)
    me.materials.append(m_body)
    me.materials.append(m_panel)
    C.bevel(block, 0.015, 2, angle_deg=40)  # soften only the panel rims
    C.shade_auto(block, 35)

    C.export_fbx([block], "SM_PushBlock")
    C.render_preview("PushBlock", [block])
    C.save_blend("PushBlock")


main()
