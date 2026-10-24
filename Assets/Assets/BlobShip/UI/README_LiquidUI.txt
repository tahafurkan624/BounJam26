BlobShip - Liquid UI (uGUI, no packages needed)
==============================================

PART 1 - THE CONTROLS THEMSELVES ARE LIQUID
-------------------------------------------
Liquid Interactable (Add Component > UI > Effects > Liquid Interactable (BlobShip))
  Put it on every Button / clickable card (Devam et, Yeniden baslat, Ana menu, Tekrar oyna, Sonraki bolum,
  the pause button, the HUD chips if they are clickable).
    hover   : swells like jelly with a springy overshoot, the SHAPE bulges toward the pointer,
              a soft light moves inside the glass following the pointer
    press   : squashes and spreads, a wave runs across the surface from the press point (rim too),
              droplets + rings splash out
    release : pops back taller and wobbles to rest
  It adds "Liquid Jelly Mesh" to the button's Image automatically (that is what bends the shape).
  Feel: Stiffness (higher = tighter), Damping (lower = wobblier), Hover Scale, Press Squash.
  Works with Button transitions (colour tint etc.) and in the pause menu (unscaled time).

Liquid Jelly Mesh (on any Image / RawImage, e.g. the big glass panels)
  Makes the panel's own outline alive: slow edge wave at rest. UILiquidInteractable drives it on hover/press,
  or call jelly.AddRipple(localPoint) / jelly.SetPointer(localPoint, true) yourself.
  Subdivisions 2 for buttons, 3 for large panels. Works with 9-sliced rounded sprites.

Liquid Segmented (the "Sivi / Kati" pill)
  indicator = the white highlight pill (anchor min = max), options = the two halves, toggles = their Toggles
  (optional). The pill flows: its leading edge rushes ahead, the back edge catches up, it gets thinner while
  stretched and wobbles when it lands. On Space: GetComponent<UILiquidSegmented>().Select(1 - current).

Liquid Stretch (slider handles: Muzik / Efektler, anything that slides)
  Put it on the slider's Handle. The faster it moves, the more it stretches like a drop; it wobbles round
  again when it stops.

Try the same values in the browser demo before importing (link in the chat).

PART 2 - PARTICLES
------------------
Liquid Particles component: put on an empty RectTransform (the emitter area) and pick a Preset.
Place it BEHIND a panel's text (above its background) so the text stays crisp.
  Glass panels ............ AmbientBubbles (RectMask2D on the panel keeps them inside)
  Progress bar fill ....... BarBubbles (child of the fill, RectMask2D on the bar)
  Pause panel bottom edge . Drip
  Drop collected .......... UILiquidFX.Splash(dropIconRectTransform);
  Level complete .......... Liquid Sequence on the panel (Drop Icons, Earned, Celebration Area)

CODE
  using BlobShip.UI;
  UILiquidFX.Splash(rect);  UILiquidFX.Ripple(rect, localPoint);  UILiquidFX.Celebrate(fullScreenRect);

Textures: Resources/BlobShipUI/T_LiquidParticles.png (bubble, droplet, ring, glint) and T_LiquidSheen.png.
