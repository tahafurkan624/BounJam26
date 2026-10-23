"""Floor pressure button: glossy red dome on a chunky gold ring. Only the solid form presses it.

Separate objects so Unity can drive them:
  SM_PressureButton_Base   static gold ring, pivot at bottom centre
  SM_PressureButton_Cap    moving dome, pivot at its own bottom (slide it down ~0.08 m when pressed)
  SM_PressureButton_Light  white ring with its own material slot, make it glow when pressed
"""
import os
import sys

sys.path.append(os.path.dirname(__file__))
import common as C  # noqa: E402

BASE_RADIUS = 0.55
BASE_HEIGHT = 0.14
CAP_RADIUS = 0.38
CAP_HEIGHT = 0.16


def main():
    C.reset()
    m_base = C.material("M_ButtonBase", (1.0, 0.68, 0.08), 0.28, **{"Metallic": 0.35, "Coat Weight": 0.6})
    m_cap = C.material("M_ButtonCap", (0.92, 0.06, 0.05), 0.18, **{"Coat Weight": 1.0, "Coat Roughness": 0.05})
    m_light = C.material("M_ButtonLight", (1.0, 0.97, 0.90), 0.3)

    base = C.prism("SM_PressureButton_Base", C.circle(BASE_RADIUS, 64), 0.0, BASE_HEIGHT, m_base)
    C.bevel(base, 0.05, 4)
    C.shade_auto(base, 40)

    cap = C.prism("SM_PressureButton_Cap", C.circle(CAP_RADIUS, 64), 0.0, CAP_HEIGHT, m_cap)
    C.bevel(cap, 0.11, 8)  # big round-over makes it read as a dome
    C.shade_auto(cap, 60)
    cap.location.z = BASE_HEIGHT

    ring = C.prism("SM_PressureButton_Light", C.circle(0.47, 64), 0.0, 0.03, m_light)
    C.bevel(ring, 0.012, 2)
    C.shade_auto(ring, 40)
    ring.location.z = BASE_HEIGHT - 0.012

    for child in (cap, ring):
        child.parent = base

    parts = [base, cap, ring]
    C.export_fbx(parts, "SM_PressureButton")
    C.render_preview("PressureButton", parts, view_dir=(1.4, -1.2, 1.0))
    C.save_blend("PressureButton")


main()
