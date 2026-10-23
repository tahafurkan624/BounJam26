"""Shared helpers for the BounJam26 asset scripts.

Run any asset script headless:
    blender --background --factory-startup --python ArtSource/Blender/scripts/<asset>.py

Each script builds its asset from scratch, saves a .blend into ArtSource/Blender,
renders a preview into ArtSource/Previews and exports an FBX into Assets/_Project/Art/Models.
Units: 1 Blender unit = 1 m. Assets sit on z=0 with the pivot at bottom centre unless noted.
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
BLEND_DIR = os.path.join(REPO, "ArtSource", "Blender")
PREVIEW_DIR = os.path.join(REPO, "ArtSource", "Previews")
MODEL_DIR = os.path.join(REPO, "Assets", "_Project", "Art", "Models")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def material(name, color, roughness=0.5, **inputs):
    """Principled material. Extra Principled inputs by socket name, e.g. **{"Subsurface Weight": 0.2}."""
    mat = bpy.data.materials.new(name)
    try:
        mat.use_nodes = True
    except AttributeError:
        pass
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    rgba = (*color, 1.0)
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = roughness
    for key, value in inputs.items():
        bsdf.inputs[key].default_value = value
    mat.diffuse_color = rgba
    return mat


def mesh_object(name, verts, faces, mat=None, collection=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([Vector(v) for v in verts], [], faces)
    me.validate()
    obj = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(obj)
    if mat:
        me.materials.append(mat)
    return obj


def apply_modifiers(obj):
    """Bake the modifier stack into the mesh (works headless, no operator context needed)."""
    deps = bpy.context.evaluated_depsgraph_get()
    new_me = bpy.data.meshes.new_from_object(obj.evaluated_get(deps))
    old_me = obj.data
    obj.modifiers.clear()
    obj.data = new_me
    new_me.name = old_me.name
    bpy.data.meshes.remove(old_me)


def clean_mesh(obj, merge_dist=1e-5):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge_dist)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def prism(name, outline, z0, z1, mat=None, collection=None):
    """Closed vertical prism from a counter-clockwise 2D outline [(x, y), ...]."""
    n = len(outline)
    verts = [(x, y, z0) for x, y in outline] + [(x, y, z1) for x, y in outline]
    faces = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    faces += [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    obj = mesh_object(name, verts, faces, mat, collection)
    clean_mesh(obj)
    return obj


def circle(radius, segments, radius_fn=None):
    return [((radius_fn(i) if radius_fn else radius) * math.cos(2 * math.pi * i / segments),
             (radius_fn(i) if radius_fn else radius) * math.sin(2 * math.pi * i / segments))
            for i in range(segments)]


def bevel(obj, width, segments=3, angle_deg=30.0):
    """Chunky toy-style rounded edges, applied."""
    mod = obj.modifiers.new("Bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(angle_deg)
    apply_modifiers(obj)


def box_uvs(obj, tile=1.0):
    """World-metre box projection UVs (1 UV unit = tile metres), so tiling textures line up across pieces."""
    me = obj.data
    uv = me.uv_layers.new(name="UVMap") if not me.uv_layers else me.uv_layers[0]
    for poly in me.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if abs(n.z) >= max(abs(n.x), abs(n.y)):
                u, v = co.x, co.y
            elif abs(n.x) > abs(n.y):
                u, v = co.y, co.z
            else:
                u, v = co.x, co.z
            uv.data[li].uv = (u / tile, v / tile)


def shade_flat(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = False


def shade_auto(obj, angle_deg=30.0):
    obj.data.shade_smooth()
    obj.data.set_sharp_from_angle(angle=math.radians(angle_deg))


def convex_hull_object(name, source_objs, collection=None):
    """Simple convex collider built from the world-space verts of source_objs."""
    bm = bmesh.new()
    for src in source_objs:
        for v in src.data.vertices:
            bm.verts.new(src.matrix_world @ v.co)
    result = bmesh.ops.convex_hull(bm, input=bm.verts)
    leftovers = {g for g in result["geom_interior"] + result["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    bmesh.ops.delete(bm, geom=list(leftovers), context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(obj)
    obj.display_type = 'WIRE'
    obj.hide_render = True
    return obj


def append_assets(blend_name, prefix="SM_"):
    """Append the objects named prefix* from ArtSource/Blender/<blend_name>.blend into the scene."""
    path = os.path.join(BLEND_DIR, blend_name + ".blend")
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(prefix)]
    for obj in dst.objects:
        bpy.context.scene.collection.objects.link(obj)
    return dst.objects


def save_blend(asset_name):
    os.makedirs(BLEND_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND_DIR, asset_name + ".blend"), compress=True)


def export_fbx(objs, file_name):
    """FBX for Unity: Y up, metres, transforms baked so Unity shows rotation 0 / scale 1."""
    os.makedirs(MODEL_DIR, exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODEL_DIR, file_name + ".fbx"),
        use_selection=True,
        object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        add_leaf_bones=False,
        bake_anim=False,
    )


def use_gpu():
    """Enable OptiX (then CUDA) for Cycles. Returns False if no NVIDIA device is found."""
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
    except KeyError:
        return False
    for backend in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = backend
        except TypeError:
            continue
        prefs.get_devices()
        devices = [d for d in prefs.devices if d.type == backend]
        for d in prefs.devices:
            d.use = d in devices
        if devices:
            return True
    return False


def _world_bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def render_preview(asset_name, objs, view_dir=(1.6, -1.1, 0.7), size=(1200, 900), samples=64,
                   backdrop_color=(0.55, 0.74, 0.95)):
    """Studio render of objs on a soft backdrop. Staging lives in a '_Preview' collection."""
    scene = bpy.context.scene
    bpy.context.view_layer.update()  # refresh matrix_world after any location changes
    stage = bpy.data.collections.new("_Preview")
    scene.collection.children.link(stage)

    lo, hi = _world_bounds(objs)
    center = (lo + hi) / 2
    radius = (hi - lo).length / 2

    floor = mesh_object("Backdrop", [(-40, -40, lo.z), (40, -40, lo.z), (40, 40, lo.z), (-40, 40, lo.z)],
                        [(0, 1, 2, 3)], material("M_Backdrop", backdrop_color, 0.9), stage)
    floor.hide_select = True

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    stage.objects.link(cam)
    direction = Vector(view_dir).normalized()
    v_fov = 2 * math.atan((cam_data.sensor_width * size[1] / size[0]) / 2 / cam_data.lens)
    dist = radius * 0.85 / math.sin(v_fov / 2)
    cam.location = center + direction * dist
    cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    sun_data = bpy.data.lights.new("Key", 'SUN')
    sun_data.energy = 4.5
    sun_data.angle = math.radians(12)
    sun = bpy.data.objects.new("Key", sun_data)
    stage.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))

    world = bpy.data.worlds.new("PreviewWorld")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs["Color"].default_value = (0.70, 0.84, 1.0, 1.0)
    bg.inputs["Strength"].default_value = 0.55

    setup_cycles(scene, samples)
    render_to(asset_name, size)


def setup_cycles(scene, samples=64):
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'GPU' if use_gpu() else 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    # Khronos PBR Neutral keeps saturated toy colours (AgX washes them out).
    for transform in ('Khronos PBR Neutral', 'Standard'):
        try:
            scene.view_settings.view_transform = transform
            break
        except TypeError:
            continue
    scene.view_settings.look = 'None'


def render_to(file_name, size):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    scene.render.filepath = os.path.join(PREVIEW_DIR, file_name + ".png")
    bpy.ops.render.render(write_still=True)
