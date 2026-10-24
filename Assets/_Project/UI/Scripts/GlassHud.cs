using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// In-game HUD: level pill, carried-cargo counter (hidden when the level has no collectibles), the Liquid / Solid
    /// switch whose glass lens springs to the blob's current form, the key hint and the pause button.
    /// </summary>
    public class GlassHud : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text levelKicker;
        [SerializeField] TMP_Text levelLabel;

        [Header("Cargo")]
        [SerializeField] GameObject cargoPill;
        [SerializeField] TMP_Text cargoCount;
        [SerializeField] RectTransform cargoFill;

        [Header("Form")]
        [SerializeField] RectTransform formLens;
        [Tooltip("Gap between the lens and the switch's rim, canvas units.")]
        [SerializeField] float lensInset = 7f;
        [SerializeField] Graphic[] liquidParts;
        [SerializeField] Graphic[] solidParts;
        [SerializeField] TMP_Text formHint;
        [SerializeField] string hintFormat = "{0}  ·  switch form";
        [SerializeField] Color activeColor = new Color32(0x0B, 0x22, 0x33, 0xFF);
        [SerializeField] Color idleColor = new Color32(0x3A, 0x5A, 0x6E, 0xFF);
        [Tooltip("Lens spring stiffness and damping: a little overshoot reads as liquid.")]
        [SerializeField] float lensStiffness = 320f;
        [SerializeField] float lensDamping = 20f;

        [SerializeField] Button pauseButton;

        BlobForm form;
        Collectible[] cargo = new Collectible[0];
        int shownHeld = -1;
        float lensX;
        float lensVelocity;
        bool shownSolid;

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
            lensX = LensTarget(false);
            ApplyForm(false);
        }

        public void SetVisible(bool visible)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
        }

        static string ToggleKey()
        {
            InputAction toggle = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/ToggleForm") : null;
            string key = toggle != null ? toggle.GetBindingDisplayString() : "";
            return string.IsNullOrEmpty(key) ? "E" : key.Replace(" | ", " / ");
        }

        float LensTarget(bool solid)
        {
            float travel = (((RectTransform)formLens.parent).rect.width - formLens.rect.width) * 0.5f - lensInset;
            return solid ? travel : -travel;
        }

        void Update()
        {
            if (form == null) form = FindAnyObjectByType<BlobForm>();
            bool solid = form != null && form.IsSolid;
            if (solid != shownSolid) ApplyForm(solid);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            lensVelocity += (LensTarget(solid) - lensX) * lensStiffness * dt;
            lensVelocity -= lensVelocity * Mathf.Min(lensDamping * dt, 1f);
            lensX += lensVelocity * dt;
            formLens.anchoredPosition = new Vector2(lensX, formLens.anchoredPosition.y);

            if (cargo.Length == 0) return;
            int held = 0;
            foreach (Collectible item in cargo)
                if (item != null && item.IsCollected) held++;
            if (held == shownHeld) return;
            shownHeld = held;
            cargoCount.text = $"{held} / {cargo.Length}";
            cargoFill.anchorMax = new Vector2((float)held / cargo.Length, 1f);
            // An empty bar would still draw a hairline at its left end.
            cargoFill.gameObject.SetActive(held > 0);
        }

        void ApplyForm(bool solid)
        {
            shownSolid = solid;
            foreach (Graphic part in liquidParts) part.color = solid ? idleColor : activeColor;
            foreach (Graphic part in solidParts) part.color = solid ? activeColor : idleColor;
        }
    }
}
