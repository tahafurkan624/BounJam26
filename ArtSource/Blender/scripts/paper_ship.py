"""Origami paper boat (canoe style) with a centre sail: the cargo the blob carries from A to B.

Shape follows the reference sheet: long faceted hull, flat bottom, V/trapezoid cross-section, sheer that
rises to pointed bow/stern. The hull is closed: the ends are decked over by folded flaps, so when the blob
squashes and bulges under it the liquid surface never shows inside the boat. The flat bottom gives the
blob a stable patch to carry it on.

~1.0 m long, 0.44 m beam, walls 0.20 m (mid) to 0.34 m (tips), sail tip 0.56 m. Y is the bow axis.
Outputs SM_PaperShip (visual, flat-shaded paper) + SM_PaperShip_Collider (convex hull of the hull only;
the sail is cosmetic).
"""
import os
import sys

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

PAPER_THICKNESS = 0.012

# Hull stations (half widths, heights). Tips are single points.
TIP_Y, TIP_Z = 0.5, 0.34
STATIONS = [  # (y, bottom half-width, rim half-width, rim z); widest mid-ships, tapering like a lens
    (-0.36, 0.06, 0.12, 0.255),
    (-0.18, 0.10, 0.195, 0.215),
    (0.00, 0.11, 0.22, 0.20),
    (0.18, 0.10, 0.195, 0.215),
    (0.36, 0.06, 0.12, 0.255),
]


def mirror(points):
    return [(-x, y, z) for x, y, z in points]


def hull_part(paper):
    """Outer hull: flat bottom, two side walls, raked faceted stems to the tips."""
    n = len(STATIONS)
    br = [(hb, y, 0.0) for y, hb, _, _ in STATIONS]      # bottom right 0..n-1
    ur = [(ht, y, z) for y, _, ht, z in STATIONS]       # rim right n..2n-1
    verts = br + ur + mirror(br) + mirror(ur) + [(0, -TIP_Y, TIP_Z), (0, TIP_Y, TIP_Z)]
    UR, BL, UL, T0, T1 = n, 2 * n, 3 * n, 4 * n, 4 * n + 1
    faces = []
    for i in range(n - 1):
        faces += [(i, i + 1, UR + i + 1, UR + i),                   # right wall
                  (BL + i + 1, BL + i, UL + i, UL + i + 1),         # left wall
                  (BL + i, BL + i + 1, i + 1, i)]                   # bottom
    last = n - 1
    faces += [(T0, 0, UR), (T0, UL, BL), (T0, BL, 0),                                  # stern facets
              (last, T1, UR + last), (BL + last, UL + last, T1), (BL + last, T1, last)]  # bow facets
    return C.mesh_object("Hull", verts, faces, paper)


def end_decks(paper):
    """Folded flaps closing bow and stern at rim height, with a valley crease on the centre line."""
    verts, faces = [], []
    for sign, (y, _, ht, z) in ((-1, STATIONS[0]), (1, STATIONS[-1])):
        base = len(verts)
        verts += [(ht - 0.01, y, z - 0.005), (-(ht - 0.01), y, z - 0.005),
                  (0, y, z - 0.035), (0, sign * (TIP_Y - 0.03), TIP_Z - 0.02)]
        r, l, m, t = base, base + 1, base + 2, base + 3
        faces += [(r, m, t), (m, l, t)]
    return C.mesh_object("Decks", verts, faces, paper)


def inner_floor(paper):
    """Inner paper layer on the floor with a raised centre crease."""
    z = 0.014
    n = len(STATIONS)
    side = [(hb - 0.015, y, z) for y, hb, _, _ in STATIONS]
    centre = [(0, y, z + 0.008) for y, _, _, _ in STATIONS]
    verts = side + centre + mirror(side)
    faces = []
    for i in range(n - 1):
        faces += [(i, i + 1, n + i + 1, n + i), (n + i, n + i + 1, 2 * n + i + 1, 2 * n + i)]
    return C.mesh_object("Floor", verts, faces, paper)


def wall_flaps(paper):
    """Triangular folds lying just inside each wall (the origami 'pockets' in the reference)."""
    verts, faces = [], []
    (y0, _, ht0, z0), (y1, _, ht1, z1), (y2, _, ht2, z2) = STATIONS[0], STATIONS[len(STATIONS) // 2], STATIONS[-1]
    for sx in (1, -1):
        for ya, hta, za, yb, htb, zb in ((y0, ht0, z0, y1, ht1, z1), (y2, ht2, z2, y1, ht1, z1)):
            base = len(verts)
            verts += [(sx * (hta - 0.03), ya + (0.02 if ya < 0 else -0.02), za - 0.02),
                      (sx * (htb - 0.03), yb, zb - 0.02),
                      (sx * 0.125, (ya + yb) / 2, 0.09)]
            faces.append((base, base + 1, base + 2))
    return C.mesh_object("Flaps", verts, faces, paper)


def sail(paper):
    """Classic doubled paper-boat sail on the centre line, with a vertical fold crease."""
    apex = (0.0, 0.0, 0.56)
    layer = [(0.012, -0.22, 0.02), (0.034, 0.0, 0.02), (0.012, 0.22, 0.02)]
    verts = [apex] + layer + mirror(layer)
    faces = [(0, 1, 2), (0, 2, 3), (0, 5, 4), (0, 6, 5)]
    return C.mesh_object("Sail", verts, faces, paper)


def build_ship(paper):
    hull, decks = hull_part(paper), end_decks(paper)
    parts = [hull, decks, inner_floor(paper), wall_flaps(paper), sail(paper)]
    for p in parts:
        C.clean_mesh(p)
        mod = p.modifiers.new("Paper", 'SOLIDIFY')
        mod.thickness = PAPER_THICKNESS
        mod.offset = 0.0
        C.apply_modifiers(p)
        C.shade_flat(p)
    collider = C.convex_hull_object("SM_PaperShip_Collider", [hull, decks])

    bpy = C.bpy
    for o in bpy.context.view_layer.objects:
        o.select_set(o in parts)
    bpy.context.view_layer.objects.active = hull
    with bpy.context.temp_override(active_object=hull, selected_editable_objects=parts):
        bpy.ops.object.join()
    hull.name = hull.data.name = "SM_PaperShip"
    collider.parent = hull
    return hull, collider


def render_orthos(ship):
    """Side / top / front orthographic views like the reference sheet (preview stage must exist)."""
    bpy = C.bpy
    scene = bpy.context.scene
    views = {
        "PaperShip_side": ((3.0, 0.0, 0.3), (0.0, 0.0, 0.3)),
        "PaperShip_top": ((0.0, 0.0, 3.0), (0.0, 0.0, 0.0)),
        "PaperShip_front": ((0.0, 3.0, 0.3), (0.0, 0.0, 0.3)),
    }
    for name, (loc, target) in views.items():
        cam_data = bpy.data.cameras.new(name)
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = 1.25
        cam = bpy.data.objects.new(name, cam_data)
        scene.collection.objects.link(cam)
        cam.location = loc
        if name == "PaperShip_top":
            cam.rotation_euler = (0.0, 0.0, 1.5708)  # bow to the right, like the side view
        else:
            cam.rotation_euler = (C.Vector(target) - C.Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        C.render_to(name, (1000, 560))


def main():
    C.reset()
    paper = C.material("M_Paper", (0.96, 0.94, 0.89), 0.55, **{"Subsurface Weight": 0.2, "Sheen Weight": 0.3})
    ship, collider = build_ship(paper)

    C.export_fbx([ship, collider], "SM_PaperShip")
    C.render_preview("PaperShip", [ship], view_dir=(1.3, -1.5, 1.0), backdrop_color=(0.24, 0.25, 0.27))
    render_orthos(ship)
    C.save_blend("PaperShip")
    print("TRIS", sum(len(p.vertices) - 2 for p in ship.data.polygons))


if __name__ == "__main__":
    main()
