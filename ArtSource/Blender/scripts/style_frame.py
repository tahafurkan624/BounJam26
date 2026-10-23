"""Style frame: the look target. One scene, several aesthetic presets ("looks") to compare.

Builds a small seaside level from the real assets + look-dev-only set dressing (sea, foam, clouds,
distant islands, dock, goal flag), restyles it for the chosen look and renders it with Cycles on the GPU.
Run the asset scripts first.

    blender -b --factory-startup --python style_frame.py -- [look] [width height samples]
looks: toy (default), golden, clay, lowpoly, pastel, papercraft
"""
import math
import os
import sys

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402
import blob_base  # noqa: E402
import platform_block  # noqa: E402

bpy = C.bpy
V = C.Vector

WATER_Z = -1.3

TOY_SUN = dict(energy=4.5, color=(1.0, 0.96, 0.88), angle=8, rot=(42, 12, -30))
TOY_SKY = dict(horizon=(0.78, 0.93, 1.0), zenith=(0.18, 0.55, 1.0), ambient=(0.62, 0.80, 1.0), strength=0.6)

# Each look: palette (material base name -> colour, or (a, b) for the checker), surface overrides for every
# solid material, water/blob overrides, bump layers [(noise scale, strength)], lighting, sky, DOF, shading.
LOOKS = {
    "toy": dict(
        title="Oyuncak (parlak plastik)",
    ),
    "golden": dict(
        title="Gün batımı (oyuncak + sıcak ışık)",
        sun=dict(energy=4.0, color=(1.0, 0.58, 0.32), angle=4, rot=(76, 0, -55)),
        sky=dict(horizon=(1.0, 0.70, 0.45), zenith=(0.30, 0.32, 0.78), ambient=(0.85, 0.62, 0.70), strength=0.5),
        palette={"M_Water": (0.05, 0.30, 0.75), "M_ShallowWater": (0.25, 0.65, 0.95)},
    ),
    "clay": dict(
        title="Kil / stop-motion",
        palette={
            "M_PlatformTop": ((0.93, 0.80, 0.62), (0.84, 0.60, 0.40)), "M_PlatformSide": (0.90, 0.84, 0.76),
            "M_Water": (0.16, 0.45, 0.78), "M_ShallowWater": (0.38, 0.70, 0.86), "M_PushBlock": (0.46, 0.36, 0.76),
            "M_PushBlockPanel": (0.95, 0.74, 0.30), "M_ButtonCap": (0.86, 0.24, 0.20), "M_ButtonBase": (0.90, 0.68, 0.30),
            "M_Hill": (0.36, 0.60, 0.30), "M_Flag": (0.86, 0.26, 0.22), "M_Wood": (0.70, 0.45, 0.28),
            "M_Blob": (0.18, 0.80, 0.62),
        },
        surface={"Roughness": 0.85, "Coat Weight": 0.0, "Metallic": 0.0, "Subsurface Weight": 0.15},
        water={"Roughness": 0.45, "Coat Weight": 0.0},
        bump=[(22.0, 0.35), (2.5, 0.45)],
        sun=dict(energy=3.6, color=(1.0, 0.90, 0.78), angle=18, rot=(48, 10, -35)),
        sky=dict(horizon=(0.95, 0.90, 0.82), zenith=(0.55, 0.70, 0.88), ambient=(0.95, 0.88, 0.80), strength=0.55),
    ),
    "lowpoly": dict(
        title="Low-poly (fasetli, düz renk)",
        palette={
            "M_PlatformTop": ((0.99, 0.86, 0.45), (0.96, 0.68, 0.28)), "M_PlatformSide": (0.97, 0.52, 0.36),
            "M_Water": (0.08, 0.55, 0.88), "M_ShallowWater": (0.30, 0.80, 0.95), "M_Hill": (0.40, 0.76, 0.30),
        },
        surface={"Roughness": 0.7, "Coat Weight": 0.0, "Metallic": 0.0},
        water={"Roughness": 0.3, "Coat Weight": 0.0},
        blob={"Transmission Weight": 0.0, "Roughness": 0.25},
        lowpoly=True,
        sun=dict(energy=4.2, color=(1.0, 0.97, 0.92), angle=3, rot=(45, 10, -40)),
    ),
    "pastel": dict(
        title="Pastel diorama (tilt-shift)",
        palette={
            "M_PlatformTop": ((1.0, 0.92, 0.93), (0.96, 0.76, 0.84)), "M_PlatformSide": (0.99, 0.97, 0.95),
            "M_Water": (0.52, 0.76, 0.96), "M_ShallowWater": (0.70, 0.90, 0.97), "M_PushBlock": (0.72, 0.64, 0.96),
            "M_PushBlockPanel": (1.0, 0.90, 0.62), "M_ButtonCap": (1.0, 0.58, 0.60), "M_ButtonBase": (1.0, 0.88, 0.62),
            "M_Hill": (0.64, 0.88, 0.66), "M_Flag": (1.0, 0.60, 0.66), "M_Wood": (0.96, 0.80, 0.66),
            "M_WoodDark": (0.85, 0.66, 0.55), "M_Blob": (0.48, 0.94, 0.84), "M_Sand": (1.0, 0.93, 0.80),
        },
        surface={"Roughness": 0.55, "Coat Weight": 0.1, "Metallic": 0.0},
        sun=dict(energy=3.2, color=(1.0, 0.97, 0.95), angle=22, rot=(40, 10, -30)),
        sky=dict(horizon=(0.99, 0.93, 0.95), zenith=(0.66, 0.80, 1.0), ambient=(0.95, 0.93, 1.0), strength=0.6),
        fstop=0.7,
    ),
    "papercraft": dict(
        title="Kağıt işi (karton + renkli kağıt)",
        palette={
            "M_PlatformTop": ((0.95, 0.88, 0.70), (0.86, 0.70, 0.48)), "M_PlatformSide": (0.93, 0.90, 0.84),
            "M_Water": (0.18, 0.44, 0.80), "M_ShallowWater": (0.40, 0.70, 0.90), "M_PushBlock": (0.86, 0.34, 0.30),
            "M_PushBlockPanel": (0.98, 0.86, 0.56), "M_ButtonCap": (0.30, 0.55, 0.90), "M_ButtonBase": (0.96, 0.80, 0.40),
            "M_Hill": (0.45, 0.70, 0.36), "M_Flag": (0.90, 0.30, 0.30), "M_Wood": (0.76, 0.56, 0.36),
        },
        surface={"Roughness": 0.95, "Coat Weight": 0.0, "Metallic": 0.0, "Sheen Weight": 0.5,
                 "Subsurface Weight": 0.25},
        water={"Roughness": 0.9, "Coat Weight": 0.0, "Sheen Weight": 0.4},
        bump=[(90.0, 0.18), (7.0, 0.12)],
        flat_solids=True,
        sun=dict(energy=3.8, color=(1.0, 0.95, 0.86), angle=10, rot=(46, 10, -35)),
        sky=dict(horizon=(0.96, 0.94, 0.88), zenith=(0.55, 0.72, 0.95), ambient=(0.95, 0.92, 0.86), strength=0.6),
    ),
}


def node_material(name):
    mat = bpy.data.materials.new(name)
    try:
        mat.use_nodes = True
    except AttributeError:
        pass
    return mat, mat.node_tree, next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')


def water_material():
    mat, nt, bsdf = node_material("M_Water")
    bsdf.inputs["Base Color"].default_value = (0.0, 0.42, 0.95, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.12
    bsdf.inputs["Coat Weight"].default_value = 0.5
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.4
    noise.inputs["Detail"].default_value = 2.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.25
    bump.inputs["Distance"].default_value = 0.15
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def sky_world(scene, horizon, zenith, ambient, strength):
    """Gradient seen by the camera, flat ambient light for everything else."""
    world = bpy.data.worlds.new("Sky")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    nt = world.node_tree
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_WORLD')
    light_bg = next(n for n in nt.nodes if n.type == 'BACKGROUND')
    light_bg.inputs["Color"].default_value = (*ambient, 1.0)
    light_bg.inputs["Strength"].default_value = strength

    coords = nt.nodes.new("ShaderNodeTexCoord")
    split = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (*horizon, 1.0)
    ramp.color_ramp.elements[1].position = 1.0
    ramp.color_ramp.elements[1].color = (*zenith, 1.0)
    cam_bg = nt.nodes.new("ShaderNodeBackground")
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(coords.outputs["Window"], split.inputs[0])
    nt.links.new(split.outputs["Y"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], cam_bg.inputs["Color"])
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(light_bg.outputs[0], mix.inputs[1])
    nt.links.new(cam_bg.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def rounded_rect(w, d, r, per_corner=8):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90),
                       (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for k in range(per_corner + 1):
            a = math.radians(a0 + 90 * k / per_corner)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def cloud(name, center, puffs, mat, lowpoly=False):
    """Puffy cloud: blended metaballs, or faceted icospheres for the low-poly look."""
    if lowpoly:
        import bmesh
        me = bpy.data.meshes.new(name)
        bm = bmesh.new()
        for (x, y, z, r) in puffs:
            ret = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
            bmesh.ops.translate(bm, vec=(x, y, z), verts=ret["verts"])
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(name, me)
    else:
        mb = bpy.data.metaballs.new(name)
        mb.resolution = 0.2
        mb.render_resolution = 0.08
        for (x, y, z, r) in puffs:
            el = mb.elements.new()
            el.co = (x, y, z)
            el.radius = r
        obj = bpy.data.objects.new(name, mb)
        me = mb
    bpy.context.scene.collection.objects.link(obj)
    obj.location = center
    obj.visible_shadow = False  # their shadows read as dark streaks on the sea
    me.materials.append(mat)
    return obj


def dock_and_flag(start, length, width, top_z, mats):
    """Wooden dock running into the water with the goal flag at the end."""
    planks = 5
    gap = 0.04
    pw = (width - gap * (planks - 1)) / planks
    for i in range(planks):
        y0 = start[1] - width / 2 + i * (pw + gap)
        p = C.prism("Plank%d" % i, [(0, 0), (length, 0), (length, pw), (0, pw)], top_z - 0.12, top_z, mats["wood"])
        p.location = (start[0], y0, 0)
        C.bevel(p, 0.025, 2)
        C.shade_auto(p)
    for px in (0.5, length - 0.25):
        for py in (start[1] - width / 2 + 0.1, start[1] + width / 2 - 0.1):
            post = C.prism("Post", C.circle(0.12, 16), WATER_Z - 0.5, top_z + 0.12, mats["wood_dark"])
            post.location = (start[0] + px, py, 0)
            C.bevel(post, 0.04, 3)
            C.shade_auto(post, 40)

    fx, fy = start[0] + length - 0.25, start[1]
    pole = C.prism("FlagPole", C.circle(0.05, 16), top_z, top_z + 2.6, mats["white"])
    pole.location = (fx, fy, 0)
    C.shade_auto(pole, 40)
    ball = bpy.data.objects.new("FlagBall", blob_base.sphere_mesh("FlagBall", 0.13))
    bpy.context.scene.collection.objects.link(ball)
    ball.data.materials.append(mats["gold"])
    ball.location = (fx, fy, top_z + 2.68)
    # Waving triangular flag: subdivided strip bent with a sine.
    verts, faces = [], []
    cols, h, L = 12, 0.75, 1.1
    for i in range(cols + 1):
        t = i / cols
        half = h / 2 * (1 - t)
        wave = 0.08 * math.sin(t * math.pi * 2.0) * t
        verts += [(t * L, wave, -half), (t * L, wave, half)]
    for i in range(cols):
        a = 2 * i
        faces.append((a, a + 2, a + 3, a + 1))
    flag = C.mesh_object("Flag", verts, faces, mats["flag"])
    sol = flag.modifiers.new("Thick", 'SOLIDIFY')
    sol.thickness = 0.03
    C.apply_modifiers(flag)
    flag.data.shade_smooth()
    flag.location = (fx + 0.04, fy, top_z + 2.2)


# ---- look application ------------------------------------------------------------------------

def _add_bump(mat, bsdf, layers):
    nt = mat.node_tree
    if bsdf.inputs["Normal"].is_linked:
        return
    prev = None
    for scale, strength in layers:
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = scale
        noise.inputs["Detail"].default_value = 4.0
        bump = nt.nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = strength
        bump.inputs["Distance"].default_value = 0.03  # Blender 5 defaults to 0.001: bumps vanish
        nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
        if prev:
            nt.links.new(prev.outputs["Normal"], bump.inputs["Normal"])
        prev = bump
    nt.links.new(prev.outputs["Normal"], bsdf.inputs["Normal"])


def restyle_materials(look):
    palette = look.get("palette", {})
    for mat in bpy.data.materials:
        if not mat.node_tree:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            continue
        base = mat.name.split(".")[0]
        if base in palette:
            colour = palette[base]
            checker = next((n for n in mat.node_tree.nodes if n.type == 'TEX_CHECKER'), None)
            if checker and isinstance(colour[0], tuple):
                checker.inputs["Color1"].default_value = (*colour[0], 1.0)
                checker.inputs["Color2"].default_value = (*colour[1], 1.0)
            else:
                bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
        if base.startswith("M_BlobEye"):
            continue
        if base == "M_Blob":
            overrides = look.get("blob", {})
        elif base in ("M_Water", "M_ShallowWater"):
            overrides = look.get("water", {})
        else:
            overrides = look.get("surface", {})
            if look.get("bump") and base != "M_Cloud":
                _add_bump(mat, bsdf, look["bump"])
        for key, value in overrides.items():
            bsdf.inputs[key].default_value = value


def make_lowpoly(scene):
    for obj in list(scene.objects):
        if obj.type != 'MESH' or obj.name == "Sea":
            continue
        faces = len(obj.data.polygons)
        boxy = obj.name.startswith(("Block", "Plank", "Post", "Foam", "Shallow", "SM_PushBlock", "SM_PressureButton"))
        if boxy:
            mod = obj.modifiers.new("LowPoly", 'DECIMATE')
            mod.decimate_type = 'DISSOLVE'
            mod.angle_limit = math.radians(25)
        elif faces > 200:
            mod = obj.modifiers.new("LowPoly", 'DECIMATE')
            mod.ratio = max(0.04, 220.0 / faces)
        for poly in obj.data.polygons:
            poly.use_smooth = False


def flatten_solids(scene):
    for obj in scene.objects:
        if obj.type == 'MESH' and obj.name.startswith(("Block", "Plank", "SM_PushBlock", "SM_PressureButton")):
            for poly in obj.data.polygons:
                poly.use_smooth = False


# ---- scene -----------------------------------------------------------------------------------

def build_scene(look):
    scene = bpy.context.scene
    mats = {
        "water": water_material(),
        "foam": C.material("M_Foam", (1.0, 1.0, 1.0), 0.3),
        "shallow": C.material("M_ShallowWater", (0.10, 0.80, 1.0), 0.05, **{"Coat Weight": 0.5}),
        "cloud": C.material("M_Cloud", (1.0, 1.0, 1.0), 0.6, **{"Subsurface Weight": 0.6, "Subsurface Scale": 0.5}),
        "hill": C.material("M_Hill", (0.16, 0.62, 0.18), 0.55),
        "sand": C.material("M_Sand", (1.0, 0.86, 0.55), 0.6),
        "wood": C.material("M_Wood", (0.80, 0.45, 0.18), 0.45, **{"Coat Weight": 0.3}),
        "wood_dark": C.material("M_WoodDark", (0.55, 0.28, 0.12), 0.5),
        "white": C.material("M_White", (0.97, 0.97, 0.97), 0.25, **{"Coat Weight": 0.5}),
        "gold": C.material("M_Gold", (1.0, 0.72, 0.08), 0.2, **{"Metallic": 0.6, "Coat Weight": 0.6}),
        "flag": C.material("M_Flag", (0.95, 0.10, 0.12), 0.4, **{"Sheen Weight": 0.5}),
    }

    # Sea + platforms
    C.mesh_object("Sea", [(-800, -60, WATER_Z), (800, -60, WATER_Z), (800, 900, WATER_Z), (-800, 900, WATER_Z)],
                  [(0, 1, 2, 3)], mats["water"])
    top_mat, side_mat = platform_block.materials()
    blocks = [(-3, -1, 0.0), (-1, -1, 0.0), (1, -1, 0.0), (-1, 1, 0.0), (1, 1, 0.0), (-3, 1, 1.0), (-3, 3, 1.0),
              (-1, 3, 1.0)]
    for i, (x, y, top) in enumerate(blocks):
        b = platform_block.build_block("Block%d" % i, top_mat=top_mat, side_mat=side_mat, height=3.0)
        b.location = (x, y, top)
        foam = C.prism("Foam%d" % i, rounded_rect(2.4, 2.4, 0.45), WATER_Z - 0.02, WATER_Z + 0.03, mats["foam"])
        foam.location = (x, y, 0)
        C.bevel(foam, 0.02, 2)
        shallow = C.prism("Shallow%d" % i, rounded_rect(3.6, 3.6, 1.0), WATER_Z - 0.02, WATER_Z + 0.008,
                          mats["shallow"])
        shallow.location = (x, y, 0)

    dock_and_flag((1.9, -1.0), 3.6, 1.5, -0.25, mats)

    # Background: islands + clouds
    for name, loc, scl in (("HillA", (-70, 160, WATER_Z), (14, 9, 5)), ("HillB", (-45, 190, WATER_Z), (9, 6, 3)),
                           ("HillC", (60, 150, WATER_Z), (12, 8, 4))):
        hill = bpy.data.objects.new(name, blob_base.sphere_mesh(name, 1.0, u=48, v=24))
        scene.collection.objects.link(hill)
        hill.data.materials.append(mats["hill"])
        hill.location, hill.scale = loc, scl
        beach = bpy.data.objects.new(name + "Beach", blob_base.sphere_mesh(name + "Beach", 1.0, u=48, v=24))
        scene.collection.objects.link(beach)
        beach.data.materials.append(mats["sand"])
        beach.location, beach.scale = (loc[0], loc[1], WATER_Z), (scl[0] * 1.25, scl[1] * 1.25, 0.5)
    puffs = [(-1.6, 0, 0, 1.1), (-0.4, 0, 0.5, 1.5), (1.0, 0, 0.3, 1.3), (2.1, 0, -0.1, 0.9), (0.3, 0.3, -0.3, 1.2)]
    low = look.get("lowpoly", False)
    cloud("CloudA", (-12, 40, 7.0), [(x * 1.6, y, z, r * 1.6) for x, y, z, r in puffs], mats["cloud"], low)
    cloud("CloudB", (6, 46, 9.0), [(x * 1.9, y, z, r * 1.9) for x, y, z, r in puffs[:4]], mats["cloud"], low)
    cloud("CloudC", (20, 38, 6.0), [(x * 1.3, y, z, r * 1.3) for x, y, z, r in puffs], mats["cloud"], low)

    # Game assets
    blob_objs = C.append_assets("BlobBase")
    blob = next(o for o in blob_objs if o.name.startswith("SM_BlobBase"))
    eyes = [o for o in blob_objs if o.name.startswith("SM_BlobEye")]
    for extra in eyes[1:]:
        bpy.data.objects.remove(extra)
    blob.location = (-1.2, -1.3, 0.0)
    blob_base.place_eyes(eyes[0], blob, face_deg=22)

    for obj in C.append_assets("PaperShip"):
        if obj.parent is None:
            ship_scale = 1.15
            obj.location = (-1.2, -1.3, blob_base.SEAT_Z + 0.006 * ship_scale)  # flat bottom on the seat
            obj.scale = (ship_scale,) * 3
            obj.rotation_euler = (0, 0, math.radians(-50))
    for obj in C.append_assets("PushBlock"):
        obj.location = (1.0, 1.0, 0.0)
        obj.rotation_euler = (0, 0, math.radians(8))
    for obj in C.append_assets("PressureButton"):
        if obj.parent is None:
            obj.location = (0.9, -1.2, 0.0)
    return blob


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    look_name = argv[0] if argv else "toy"
    width, height, samples = (int(a) for a in argv[1:4]) if len(argv) >= 4 else (1920, 1080, 160)
    look = LOOKS[look_name]

    C.reset()
    scene = bpy.context.scene
    blob = build_scene(look)
    restyle_materials(look)
    if look.get("lowpoly"):
        make_lowpoly(scene)
    if look.get("flat_solids"):
        flatten_solids(scene)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 35
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (4.6, -11.5, 4.4)
    target = V((-0.2, 1.0, 1.6))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.dof.use_dof = True
    cam_data.dof.focus_distance = (V(blob.location) - cam.location).length
    cam_data.dof.aperture_fstop = look.get("fstop", 2.8)
    scene.camera = cam

    sun_cfg = look.get("sun", TOY_SUN)
    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = sun_cfg["energy"]
    sun_data.angle = math.radians(sun_cfg["angle"])
    sun_data.color = sun_cfg["color"]
    sun = bpy.data.objects.new("Sun", sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = tuple(math.radians(a) for a in sun_cfg["rot"])
    sky_world(scene, **look.get("sky", TOY_SKY))

    C.setup_cycles(scene, samples=samples)
    out = "StyleFrame" if look_name == "toy" else "StyleFrame_" + look_name
    C.render_to(out, (width, height))
    C.save_blend(out)


if __name__ == "__main__":
    main()
