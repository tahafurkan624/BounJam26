using BlobShip.UI;
using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// In-game HUD: level pill, carried-cargo counter (hidden when the level has no collectibles; the drop icon splashes
    /// when cargo comes aboard) and the key hint on the glass overlay; the 3D liquid pause button and the Liquid / Solid
    /// switch on the liquid canvas. The switch follows the blob's form, and clicking it switches the form. While the
    /// blob is solid, every liquid button freezes with it (<see cref="LiquidUI.SetSolid"/>).
    /// </summary>
    public class GlassHud : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [Tooltip("The HUD's part on the liquid canvas.")]
        [SerializeField] CanvasGroup liquidGroup;
        [SerializeField] TMP_Text levelKicker;
        [SerializeField] TMP_Text levelLabel;

        [Header("Cargo")]
        [SerializeField] GameObject cargoPill;
        [SerializeField] RectTransform cargoIcon;
        [SerializeField] TMP_Text cargoCount;
        [SerializeField] RectTransform cargoFill;

        [Header("Form")]
        [SerializeField] LiquidToggle formSwitch;
        [SerializeField] TMP_Text formHint;
        [SerializeField] string hintFormat = "{0}  ·  switch form";

        [SerializeField] Button pauseButton;

        BlobForm form;
        Collectible[] cargo = new Collectible[0];
        int shownHeld = -1;

        public void Bind(GlassUI owner)
        {
            // "Level 1: First Voyage" reads as a small "LEVEL 1" over "First Voyage".
            string title = owner.Level.LevelTitle;
            int colon = title.IndexOf(':');
            levelKicker.text = (colon > 0 ? title.Substring(0, colon).Trim() : "Level").ToUpperInvariant();
            levelLabel.text = colon > 0 ? title.Substring(colon + 1).Trim() : title;
            cargo = FindObjectsByType<Collectible>(FindObjectsSortMode.None);
            cargoPill.SetActive(cargo.Length > 0);
            pauseButton.onClick.AddListener(owner.TogglePause);
            formHint.text = string.Format(hintFormat, ToggleKey());
            formSwitch.onChanged.AddListener(OnSwitchClicked);
            // The liquid state is static and outlives the last level.
            formSwitch.SetSolid(false, false);
            LiquidUI.SetSolid(false);
        }

        public void SetVisible(bool visible)
        {
            foreach (CanvasGroup part in new[] { group, liquidGroup })
            {
                part.alpha = visible ? 1f : 0f;
                part.interactable = part.blocksRaycasts = visible;
            }
        }

        static string ToggleKey()
        {
            InputAction toggle = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/ToggleForm") : null;
            // The keyboard key only: the gamepad's west button reads "X", a key the keyboard has no use for.
            if (toggle != null)
                for (int i = 0; i < toggle.bindings.Count; i++)
                    if (toggle.bindings[i].effectivePath.StartsWith("<Keyboard>"))
                        return toggle.GetBindingDisplayString(i);
            return "E";
        }

        // Every piece of a cut blob switches, as with the key.
        static void OnSwitchClicked(bool solid)
        {
            foreach (BlobForm piece in FindObjectsByType<BlobForm>(FindObjectsSortMode.None))
                if (piece.IsSolid != solid) piece.Toggle();
        }

        void Update()
        {
            if (form == null) form = FindAnyObjectByType<BlobForm>();
            // The blob decides: a click it refused (cooldown, inside a grate) flows back.
            bool solid = form != null && form.IsSolid;
            if (solid != formSwitch.solid) formSwitch.SetSolid(solid, false);

            if (cargo.Length == 0) return;
            int held = 0;
            foreach (Collectible item in cargo)
                if (item != null && item.IsCollected) held++;
            if (held == shownHeld) return;
            // Only a pickup splashes, not the first count or a restart.
            if (shownHeld >= 0 && held > shownHeld && cargoIcon != null) UILiquidFX.Splash(cargoIcon, 10, 0.8f);
            shownHeld = held;
            cargoCount.text = $"{held} / {cargo.Length}";
            cargoFill.anchorMax = new Vector2((float)held / cargo.Length, 1f);
            // An empty bar would still draw a hairline at its left end.
            cargoFill.gameObject.SetActive(held > 0);
        }
    }
}
