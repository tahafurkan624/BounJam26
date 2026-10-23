"""Blob material look-dev: three liquid treatments side by side, each carrying the paper ship.

All three drop the mirror-like coat of the first version: colour comes from a core-to-rim gradient
(darker core, light rim) and the specular is toned down, so the blob reads as a soft liquid body.
  A satin   satin jelly, soft sheen
  B gummy   translucent gummy with air bubbles inside
  C mochi   matte milky body, bright rim
Renders ArtSource/Previews/BlobLookdev.png. Run blob_base.py, paper_ship.py and push_block.py first.
"""
import math
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402
import blob_base  # noqa: E402
import platform_block  # noqa: E402
import style_frame  # noqa: E402

bpy = C.bpy
V = C.Vector

CORE, RIM = blob_base.BLOB_CORE, blob_base.BLOB_RIM
gradient_jelly = blob_base.gradient_jelly


def variants():
    return [
        ("A · Saten jöle", gradient_jelly("M_Blob_Satin", CORE, RIM, **{
            "Roughness": 0.34, "Subsurface Weight": 1.0, "Subsurface Scale": 0.3,
            "Specular IOR Level": 0.3, "Sheen Weight": 0.35, "Coat Weight": 0.0})),
        ("B · Sakız + kabarcık", gradient_jelly("M_Blob_Gummy", (0.0, 0.62, 0.52), RIM, **{
            "Roughness": 0.2, "Transmission Weight": 0.85, "Subsurface Weight": 0.2, "Subsurface Scale": 0.2,
            "IOR": 1.33, "Specular IOR Level": 0.35, "Coat Weight": 0.0})),
        ("C · Mochi", gradient_jelly("M_Blob_Mochi", (0.10, 0.70, 0.60), (0.80, 1.0, 0.92), (0.15, 0.7), **{
            "Roughness": 0.6, "Subsurface Weight": 1.0, "Subsurface Scale": 0.6,
            "Specular IOR Level": 0.15, "Coat Weight": 0.0})),
    ]


def bubbles(parent, count, seed):
    """Small air bubbles floating inside the blob (look-dev only; in Unity this is a shader effect)."""
    rng = random.Random(seed)
    # IOR < 1 fakes air inside water (relative IOR), so the bubbles refract and catch light.
    mat = C.material("M_Bubble", (1.0, 1.0, 1.0), 0.05, **{"Transmission Weight": 1.0, "IOR": 0.75,
                                                            "Specular IOR Level": 1.0})
    for i in range(count):
        r = rng.uniform(0.018, 0.05)
        while True:
            p = V((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
            if p.length < 0.75:
                break
        b = bpy.data.objects.new("Bubble", blob_base.sphere_mesh("Bubble", r, u=16, v=8))
        bpy.context.scene.collection.objects.link(b)
        b.data.materials.append(mat)
        half_h = blob_base.BODY_H / 2
        b.location = V(parent.location) + V((p.x * 0.5, p.y * 0.5, half_h + p.z * half_h * 0.8))


def main():
    C.reset()
    scene = bpy.context.scene
    top_mat, side_mat = platform_block.materials()

    blob_src = next(o for o in C.append_assets("BlobBase") if o.name.startswith("SM_BlobBase"))
    eye_src = next(o for o in bpy.data.objects if o.name.startswith("SM_BlobEye"))
    for extra in [o for o in bpy.data.objects if o.name.startswith("SM_BlobEye") and o is not eye_src]:
        bpy.data.objects.remove(extra)
    ship_src = next(o for o in C.append_assets("PaperShip") if o.parent is None)
    for o in [o for o in bpy.data.objects if o.name.startswith("SM_PaperShip_Collider")]:
        o.hide_render = True

    for i, (label, mat) in enumerate(variants()):
        x = (i - 1) * 2.4
        block = platform_block.build_block("Block%d" % i, top_mat=top_mat, side_mat=side_mat)
        block.location = (x, 0, 0)

        blob = blob_src if i == 0 else blob_src.copy()
        if i:
            blob.data = blob_src.data.copy()
            scene.collection.objects.link(blob)
        blob.data.materials.clear()
        blob.data.materials.append(mat)
        blob.location = (x, 0, 0)
        eye = eye_src if i == 0 else eye_src.copy()
        if i:
            scene.collection.objects.link(eye)
        blob_base.place_eyes(eye, blob)
        if "Gummy" in mat.name:
            bubbles(blob, 14, seed=3)

        ship = ship_src if i == 0 else ship_src.copy()
        if i:
            scene.collection.objects.link(ship)
        ship.location = (x, 0.0, blob_base.SEAT_Z + 0.006 * 1.15)
        ship.rotation_euler = (0, 0, math.radians(40))
        ship.scale = (1.15, 1.15, 1.15)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 40
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.0, -10.5, 3.6)
    cam.rotation_euler = (V((0, 0, 0.45)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 4.5
    sun_data.angle = math.radians(8)
    sun_data.color = (1.0, 0.96, 0.88)
    sun = bpy.data.objects.new("Sun", sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(45), math.radians(10), math.radians(-30))
    style_frame.sky_world(scene, **style_frame.TOY_SKY)

    C.setup_cycles(scene, samples=160)
    C.render_to("BlobLookdev", (1800, 760))
    C.save_blend("BlobLookdev")


main()
