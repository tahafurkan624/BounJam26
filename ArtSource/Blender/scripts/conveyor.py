"""Spiked conveyor: a belt loop standing on its edge like a low wall, with metal spikes sticking out sideways.
The liquid flows around a spike; freezing sets the crystal around it and the belt carries it along.

Unity builds the belt from these parts at any length (Conveyor.cs). Modelled with X outward from the belt,
Y along it and Z up; the export turns the meshes 180 degrees about Z, so Unity gets +X outward, +Z along the belt
and +Y up. Every pivot is at the world origin, so the meshes keep these offsets:
  SM_ConveyorSlat      one rubber belt link, inner face at x = 0, bottom at z = 0, 0.2 m pitch, 0.8 m tall
  SM_ConveyorSpike     steel flange, collar and rod, base on the belt's outer face (x = 0)
  SM_ConveyorSpikeTip  the knob at the rod's end; its own mesh so Unity can make it glow
  SM_ConveyorDrum      roller at each end, bottom centre pivot, spins about Z
  SM_ConveyorLidMid    1 m of the lid over the straight part, stretched along Y to the belt's length
  SM_ConveyorLidEnd    half-round lid over a roller, round side toward +Y, pivot on the flat side's middle
  SM_ConveyorHub       gear cap on top of each lid end, spins with the roller
"""
import math
import os
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

DRUM_RADIUS = 0.35      # belt's inner face, from the roller axis
BELT_THICKNESS = 0.07
BELT_HEIGHT = 0.8
SLAT_PITCH = 0.2
SPIKE_HEIGHT = 0.3      # spike axis above the belt's bottom
SPIKE_REACH = 0.475     # outer end of the tip, from the belt's outer face
LID_THICKNESS = 0.06
LID_HALF_WIDTH = DRUM_RADIUS + BELT_THICKNESS + 0.03


def bm_object(name, bm, mats):
    me = C.bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = C.bpy.data.objects.new(name, me)
    C.bpy.context.scene.collection.objects.link(obj)
    for m in mats:
        me.materials.append(m)
    return obj


def add_cylinder(bm, radius, x0, x1, segments=24, axis='X'):
    """Closed cylinder from x0 to x1 along the axis, centred on it."""
    geom = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius,
                                 depth=x1 - x0)
    verts = geom["verts"]
    bmesh.ops.translate(bm, verts=verts, vec=(0, 0, (x0 + x1) / 2))
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
    return verts


def bevel_edges(bm, edges, width, segments=3):
    bmesh.ops.bevel(bm, geom=list(edges), offset=width, offset_type='OFFSET', segments=segments, profile=0.5,
                    affect='EDGES', clamp_overlap=True)


def slat(m_belt):
    bm = bmesh.new()
    half = SLAT_PITCH * 0.45
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(BELT_THICKNESS, half * 2, BELT_HEIGHT), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(BELT_THICKNESS / 2, 0, BELT_HEIGHT / 2), verts=bm.verts)
    bevel_edges(bm, bm.edges, 0.018, 2)
    # Two tread ribs across the outer face, clear of the spike mount between them.
    for z in (0.12, 0.68):
        rib = bmesh.ops.create_cube(bm, size=1.0)["verts"]
        bmesh.ops.scale(bm, vec=(0.03, half * 2 - 0.02, 0.05), verts=rib)
        bmesh.ops.translate(bm, vec=(BELT_THICKNESS + 0.005, 0, z), verts=rib)
    obj = bm_object("SM_ConveyorSlat", bm, [m_belt])
    C.shade_auto(obj, 35)
    return obj


def spike(m_steel, m_tip):
    bm = bmesh.new()
    add_cylinder(bm, 0.09, 0.0, 0.03, 32)     # flange bolted to the belt
    add_cylinder(bm, 0.055, 0.03, 0.08, 24)   # collar
    add_cylinder(bm, 0.035, 0.08, 0.43, 20)   # rod
    bmesh.ops.translate(bm, vec=(0, 0, SPIKE_HEIGHT), verts=bm.verts)
    rod = bm_object("SM_ConveyorSpike", bm, [m_steel])
    C.bevel(rod, 0.008, 2, angle_deg=40)
    C.shade_auto(rod, 40)

    bm = bmesh.new()
    tip_radius = 0.055
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=tip_radius)
    bmesh.ops.translate(bm, vec=(SPIKE_REACH - tip_radius, 0, SPIKE_HEIGHT), verts=bm.verts)
    tip = bm_object("SM_ConveyorSpikeTip", bm, [m_tip])
    tip.data.shade_smooth()
    return rod, tip


def drum(m_drum):
    body = C.prism("SM_ConveyorDrum", C.circle(0.3, 36, lambda i: 0.3 if i % 2 == 0 else 0.285),
                   0.04, BELT_HEIGHT - 0.04, m_drum)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for z0 in (0.0, BELT_HEIGHT - 0.04):
        flange = bmesh.ops.create_cone(bm, cap_ends=True, segments=36, radius1=0.33, radius2=0.33, depth=0.04)
        bmesh.ops.translate(bm, verts=flange["verts"], vec=(0, 0, z0 + 0.02))
    bm.to_mesh(body.data)
    bm.free()
    C.bevel(body, 0.008, 2, angle_deg=40)
    C.shade_auto(body, 30)
    return body


def lid_mid(m_lid):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(LID_HALF_WIDTH * 2, 1.0, LID_THICKNESS), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0, LID_THICKNESS / 2), verts=bm.verts)
    # Round only the long top edges: the ends butt against the end pieces.
    top = [e for e in bm.edges if all(v.co.z > LID_THICKNESS - 1e-4 for v in e.verts)
           and abs(e.verts[0].co.x - e.verts[1].co.x) < 1e-4]
    bevel_edges(bm, top, 0.025, 4)
    obj = bm_object("SM_ConveyorLidMid", bm, [m_lid])
    C.shade_auto(obj, 35)
    return obj


def lid_end(m_lid):
    segments = 32
    outline = [(LID_HALF_WIDTH * math.cos(math.pi * i / segments), LID_HALF_WIDTH * math.sin(math.pi * i / segments))
               for i in range(segments + 1)]
    obj = C.prism("SM_ConveyorLidEnd", outline, 0.0, LID_THICKNESS, m_lid)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    rim = [e for e in bm.edges if all(v.co.z > LID_THICKNESS - 1e-4 for v in e.verts)
           and not all(abs(v.co.y) < 1e-4 for v in e.verts)]
    bevel_edges(bm, rim, 0.025, 4)
    bm.to_mesh(obj.data)
    bm.free()
    C.shade_auto(obj, 35)
    return obj


def hub(m_hub):
    teeth = 10
    gear = C.prism("SM_ConveyorHub", C.circle(0.2, teeth * 4, lambda i: 0.2 if i % 4 in (1, 2) else 0.165),
                   0.0, 0.05, m_hub)
    bm = bmesh.new()
    bm.from_mesh(gear.data)
    cap = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.08, radius2=0.07, depth=0.04)
    bmesh.ops.translate(bm, verts=cap["verts"], vec=(0, 0, 0.07))
    bm.to_mesh(gear.data)
    bm.free()
    C.bevel(gear, 0.01, 2, angle_deg=40)
    C.shade_auto(gear, 35)
    return gear


# ---------------------------------------------------------------- Preview assembly (not exported)

def path_pose(s, length):
    """Point, outward direction and heading at distance s along the belt's inner face (Conveyor.cs does the same)."""
    half = length / 2
    arc = math.pi * DRUM_RADIUS
    loop = 2 * length + 2 * arc
    s %= loop
    if s < length:
        return Vector((DRUM_RADIUS, -half + s, 0)), Vector((1, 0, 0)), 0.0
    s -= length
    if s < arc:
        a = s / DRUM_RADIUS
        out = Vector((math.cos(a), math.sin(a), 0))
        return Vector((0, half, 0)) + out * DRUM_RADIUS, out, a
    s -= arc
    if s < length:
        return Vector((-DRUM_RADIUS, half - s, 0)), Vector((-1, 0, 0)), math.pi
    s -= length
    a = math.pi + s / DRUM_RADIUS
    out = Vector((math.cos(a), math.sin(a), 0))
    return Vector((0, -half, 0)) + out * DRUM_RADIUS, out, a


def place(src, name, location, angle, stage):
    obj = C.bpy.data.objects.new(name, src.data)
    stage.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (0, 0, angle)
    return obj


def assemble(parts, length=2.4, spike_spacing=1.2):
    stage = C.bpy.data.collections.new("_Assembly")
    C.bpy.context.scene.collection.children.link(stage)
    loop = 2 * length + 2 * math.pi * DRUM_RADIUS
    placed = []
    slats = round(loop / SLAT_PITCH)
    for i in range(slats):
        point, _, angle = path_pose(i * loop / slats, length)
        placed.append(place(parts["slat"], f"Slat {i}", point, angle, stage))
    spikes = round(loop / spike_spacing)
    for i in range(spikes):
        point, out, angle = path_pose((i + 0.5) * loop / spikes, length)
        base = point + out * BELT_THICKNESS
        placed.append(place(parts["spike"], f"Spike {i}", base, angle, stage))
        placed.append(place(parts["tip"], f"Tip {i}", base, angle, stage))
    for sign in (1, -1):
        end = Vector((0, sign * length / 2, 0))
        placed.append(place(parts["drum"], "Drum", end, 0, stage))
        placed.append(place(parts["lid_end"], "Lid End", end + Vector((0, 0, BELT_HEIGHT)), 0 if sign > 0 else math.pi, stage))
        placed.append(place(parts["hub"], "Hub", end + Vector((0, 0, BELT_HEIGHT + LID_THICKNESS)), 0.3 * sign, stage))
    mid = place(parts["lid_mid"], "Lid Mid", Vector((0, 0, BELT_HEIGHT)), 0, stage)
    mid.scale = (1, length, 1)
    placed.append(mid)
    for o in parts.values():
        o.hide_render = True
        o.hide_set(True)
    return placed


def main():
    C.reset()
    m_belt = C.material("M_ConveyorBelt", (0.045, 0.05, 0.06), 0.65)
    m_steel = C.material("M_ConveyorSteel", (0.72, 0.74, 0.78), 0.22, **{"Metallic": 1.0})
    m_tip = C.material("M_ConveyorSpikeTip", (0.55, 0.82, 1.0), 0.15, **{"Metallic": 0.6, "Coat Weight": 0.5})
    m_drum = C.material("M_ConveyorDrum", (0.16, 0.17, 0.2), 0.35, **{"Metallic": 0.8})
    m_lid = C.material("M_ConveyorLid", (0.92, 0.22, 0.14), 0.3, **{"Coat Weight": 0.6})
    m_hub = C.material("M_ConveyorHub", (1.0, 0.68, 0.08), 0.28, **{"Metallic": 0.35, "Coat Weight": 0.6})

    rod, tip = spike(m_steel, m_tip)
    parts = {
        "slat": slat(m_belt),
        "spike": rod,
        "tip": tip,
        "drum": drum(m_drum),
        "lid_mid": lid_mid(m_lid),
        "lid_end": lid_end(m_lid),
        "hub": hub(m_hub),
    }

    shown = assemble(parts)
    C.render_preview("Conveyor", shown, view_dir=(1.5, -1.3, 1.0))
    C.save_blend("Conveyor")

    # Into Unity's frame: X outward, Z (Blender -Y after the axis conversion) along the belt.
    flip = Matrix.Rotation(math.pi, 4, 'Z')
    for o in parts.values():
        o.data.transform(flip)
        o.hide_set(False)
    C.export_fbx(list(parts.values()), "SM_Conveyor")


main()
