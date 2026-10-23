"""Scale check: appends every SM_* object from the asset .blends and renders them side by side.

Run the asset scripts first. Output: ArtSource/Previews/Lineup.png
"""
import os
import sys

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

# blend name -> world offset of its root objects
LAYOUT = {
    "BlobBase": (0.0, 0.0, 0.0),
    "PaperShip": (0.0, 0.0, 0.746),  # flat bottom on the blob's seat (blob_base.SEAT_Z)
    "PressureButton": (-1.4, 0.4, 0.0),
    "PushBlock": (1.5, 0.3, 0.0),
}


def main():
    C.reset()
    shown = []
    for blend_name, offset in LAYOUT.items():
        objs = C.append_assets(blend_name)
        for obj in objs:
            if obj.parent is None:
                obj.location = offset
        shown += [o for o in objs if not o.hide_render]
    C.render_preview("Lineup", shown, view_dir=(0.35, -1.6, 0.55), size=(1600, 900), samples=96)


main()
