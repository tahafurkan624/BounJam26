"""Player blob: rest-pose body + eye.

  SM_BlobBase  water-balloon rest pose, pivot at the bottom centre (ground contact).
               - soft flattened contact patch at the bottom (weight / surface tension)
               - lower belly slightly wider, tapering upward
               - flat seat on top (SEAT_Z) so the flat-bottomed paper ship rests on it without sinking in
               Built from an evenly tessellated icosphere (2562 verts), so the runtime squash/flatten
               deformation in Unity still has even verts to work with.
  SM_BlobEye   one glossy pill-shaped eye with two catch-lights, pivot at its back, looking down -Y.
               Unity places two of them on the surface facing the move direction (not skinned).
"""
import math
import os
import sys

import bmesh

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

SUBDIVISIONS = 5    # bmesh counts the bare icosahedron as 1 -> 2562 verts / 5120 tris
RADIUS_XY = 0.56    # horizontal radius before belly shaping
RADIUS_Z = 0.5      # vertical radius before flattening
BOTTOM = -0.78      # contact plane, in unit-sphere space
TOP = 0.70          # seat plane, in unit-sphere space
BODY_H = (TOP - BOTTOM) * RADIUS_Z   # ~0.74 m
SEAT_Z = BODY_H                      # where the ship's flat bottom sits

BLOB_CORE = (0.00, 0.52, 0.46)
BLOB_RIM = (0.42, 0.98, 0.82)
RIM_GLOW = (0.30, 0.95, 0.90)


def _smooth_max(a, b, k):
    return (a + b + math.sqrt((a - b) ** 2 + k * k)) / 2


def _smooth_min(a, b, k):
    return (a + b - math.sqrt((a - b) ** 2 + k * k)) / 2


def balloon_point(p):
    """Unit-sphere point -> rest-pose point (metres, bottom at z=0)."""
    x, y, z = p
    belly = 1.0 + 0.06 - 0.10 * z            # wider below the middle, slimmer toward the top
    z = _smooth_max(z, BOTTOM, 0.14)          # soft contact patch
    z = _smooth_min(z, TOP, 0.16)             # soft-edged flat seat
    return (x * RADIUS_XY * belly, y * RADIUS_XY * belly, (z - BOTTOM) * RADIUS_Z)


def gradient_jelly(name, core, rim, ramp_pos=(0.0, 0.85), rim_glow=None, **inputs):
    """Principled jelly whose base colour runs from core (facing the camera) to rim (grazing).
    rim_glow: optional (rgb, strength) faint emission on the grazing rim so the blob stays lively in shadow."""
    mat = C.material(name, core, inputs.pop("Roughness", 0.3), **inputs)
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    weight = nt.nodes.new("ShaderNodeLayerWeight")
    weight.inputs["Blend"].default_value = 0.45
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = ramp_pos[0]
    ramp.color_ramp.elements[0].color = (*core, 1.0)
    ramp.color_ramp.elements[1].position = ramp_pos[1]
    ramp.color_ramp.elements[1].color = (*rim, 1.0)
    nt.links.new(weight.outputs["Facing"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    if rim_glow:
        glow = nt.nodes.new("ShaderNodeValToRGB")
        glow.color_ramp.elements[0].position = 0.55
        glow.color_ramp.elements[0].color = (0, 0, 0, 1)
        glow.color_ramp.elements[1].position = 1.0
        glow.color_ramp.elements[1].color = (*rim_glow[0], 1.0)
        nt.links.new(weight.outputs["Facing"], glow.inputs["Fac"])
        nt.links.new(glow.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = rim_glow[1]
    return mat


def jelly_material():
    """Satin jelly (look-dev pick A): soft sheen instead of a mirror coat, core-to-rim colour gradient."""
    return gradient_jelly("M_Blob", BLOB_CORE, BLOB_RIM, rim_glow=(RIM_GLOW, 0.3), **{
        "Roughness": 0.34, "Subsurface Weight": 1.0, "Subsurface Scale": 0.3,
        "Specular IOR Level": 0.3, "Sheen Weight": 0.35, "Coat Weight": 0.0})


def sphere_mesh(name, radius, scale=(1, 1, 1), offset=(0, 0, 0), u=32, v=16):
    me = C.bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius)
    for vert in bm.verts:
        vert.co = C.Vector((vert.co.x * scale[0] + offset[0], vert.co.y * scale[1] + offset[1],
                            vert.co.z * scale[2] + offset[2]))
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    return me


def build_body():
    me = C.bpy.data.meshes.new("SM_BlobBase")
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=SUBDIVISIONS, radius=1.0, calc_uvs=True)
    for v in bm.verts:
        v.co = C.Vector(balloon_point(v.co))
    bm.to_mesh(me)
    bm.free()
    obj = C.bpy.data.objects.new("SM_BlobBase", me)
    C.bpy.context.scene.collection.objects.link(obj)
    me.materials.append(jelly_material())
    me.shade_smooth()
    return obj


def build_eye():
    """Pill-shaped black eye facing -Y with two catch-lights (big top-left, small bottom-right).
    Pivot at the back of the eye; the catch-lights sit just proud of the eye surface."""
    m_eye = C.material("M_BlobEye", (0.01, 0.012, 0.03), 0.06, **{"Coat Weight": 1.0})
    m_shine = C.material("M_BlobEyeShine", (1.0, 1.0, 1.0), 0.2, **{"Emission Color": (1, 1, 1, 1),
                                                                     "Emission Strength": 1.0})
    eye = C.bpy.data.objects.new("SM_BlobEye", sphere_mesh("SM_BlobEye", 1.0, (0.055, 0.03, 0.095), (0, -0.03, 0)))
    C.bpy.context.scene.collection.objects.link(eye)
    eye.data.materials.append(m_eye)

    parts = [eye]
    # Viewer's left is -X when looking at the eye from the front (-Y).
    for name, size, offset in (("ShineBig", (0.019, 0.012, 0.027), (-0.017, -0.056, 0.038)),
                               ("ShineSmall", (0.009, 0.008, 0.011), (0.02, -0.055, -0.04))):
        shine = C.bpy.data.objects.new(name, sphere_mesh(name, 1.0, size, offset, 16, 8))
        C.bpy.context.scene.collection.objects.link(shine)
        shine.data.materials.append(m_shine)
        parts.append(shine)
    for o in C.bpy.context.view_layer.objects:
        o.select_set(o in parts)
    with C.bpy.context.temp_override(active_object=eye, selected_editable_objects=parts):
        C.bpy.ops.object.join()
    return eye


def place_eyes(eye, blob, spread_deg=17, eye_height=0.40, face_deg=0, sink=0.012):
    """Put the eye + a mirrored copy on the blob's front (-Y rotated by face_deg) by ray-casting onto
    the actual surface, each facing along the surface normal. eye_height is metres above the pivot."""
    bpy = C.bpy
    bpy.context.view_layer.update()
    eyes = [eye, eye.copy()]
    bpy.context.scene.collection.objects.link(eyes[1])
    mw = blob.matrix_world
    inv = mw.inverted()
    normal_mat = mw.to_3x3().inverted().transposed()
    for side, e in zip((-1, 1), eyes):
        a = math.radians(face_deg + side * spread_deg)
        outward = C.Vector((math.sin(a), -math.cos(a), 0.0))
        origin = mw @ C.Vector((0, 0, eye_height / max(blob.scale.z, 1e-6))) + outward * 5.0
        hit, loc, nrm, _ = blob.ray_cast(inv @ origin, (inv.to_3x3() @ -outward).normalized())
        if not hit:
            continue
        n = (normal_mat @ nrm).normalized()
        e.location = (mw @ loc) - n * sink
        e.rotation_euler = n.to_track_quat('-Y', 'Z').to_euler()
    return eyes


def main():
    C.reset()
    blob = build_body()
    eye = build_eye()
    C.export_fbx([blob], "SM_BlobBase")
    C.export_fbx([eye], "SM_BlobEye")

    preview_eyes = place_eyes(eye, blob)
    C.render_preview("BlobBase", [blob] + preview_eyes, view_dir=(0.6, -1.6, 0.55))
    C.save_blend("BlobBase")
    me = blob.data
    print("VERTS", len(me.vertices), "TRIS", len(me.polygons), "HEIGHT %.3f" % BODY_H)


if __name__ == "__main__":
    main()
