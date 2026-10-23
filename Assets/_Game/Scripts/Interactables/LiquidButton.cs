using BounJam.Liquids;
using UnityEngine;
using UnityEngine.Events;

namespace BounJam.Interactables
{
    /// <summary>
    /// A button pressed by spraying it. A steady stream charges it, and once charged it presses and fires
    /// On Pressed: hook doors, gates or anything else to that event. A latching button stays down for good;
    /// otherwise it pops back up (On Released) shortly after the stream stops.
    /// </summary>
    [RequireComponent(typeof(LiquidTarget))]
    public class LiquidButton : MonoBehaviour, ILiquidReceiver
    {
        // Particles land a few frames apart, so the charge only drains after a short gap with no hits.
        const float HitGrace = 0.2f;

        [SerializeField] LiquidKind pressedBy = LiquidKind.Water;
        [SerializeField, Min(0.01f), Tooltip("Units of liquid it takes to press. Water sprays 1 unit a second.")]
        float unitsToPress = 0.5f;
        [SerializeField, Tooltip("Stay pressed for good once pressed.")]
        bool latch = true;
        [SerializeField, Min(0f), Tooltip("Charge lost per second once the stream stops.")]
        float drainPerSecond = 1f;

        [Header("Look")]
        [SerializeField] Transform cap;
        [SerializeField] Renderer capRenderer;
        [SerializeField] Color idleColor = new(0.9f, 0.25f, 0.2f);
        [SerializeField] Color pressedColor = new(0.3f, 0.9f, 0.35f);
        [SerializeField, Tooltip("How far the cap sinks, along the button's back (-Z), when pressed.")]
        float pressDepth = 0.05f;

        [Header("Events")]
        [SerializeField] UnityEvent onPressed;
        [SerializeField] UnityEvent onReleased;

        float charge;
        float lastHitTime = float.NegativeInfinity;
        Vector3 capRestPosition;
        MaterialPropertyBlock capColor;

        public bool IsPressed { get; private set; }
        public UnityEvent Pressed => onPressed;
        public UnityEvent Released => onReleased;

        void Awake()
        {
            capRestPosition = cap.localPosition;
            capColor = new MaterialPropertyBlock();
            ShowState();
        }

        public void OnLiquidHit(in LiquidHit hit)
        {
            if (hit.Liquid.Kind != pressedBy)
                return;
            lastHitTime = Time.time;
            charge = Mathf.Min(unitsToPress, charge + hit.Amount);
            if (!IsPressed && charge >= unitsToPress)
                SetPressed(true);
        }

        void Update()
        {
            if ((latch && IsPressed) || Time.time - lastHitTime < HitGrace)
                return;
            charge = Mathf.Max(0f, charge - drainPerSecond * Time.deltaTime);
            if (IsPressed && charge <= 0f)
                SetPressed(false);
        }

        void SetPressed(bool pressed)
        {
            IsPressed = pressed;
            ShowState();
            Debug.Log($"[BounJam] {name} {(pressed ? "pressed" : "released")}.", this);
            (pressed ? onPressed : onReleased).Invoke();
        }

        void ShowState()
        {
            cap.localPosition = IsPressed ? capRestPosition + Vector3.back * pressDepth : capRestPosition;
            capColor.SetColor("_BaseColor", IsPressed ? pressedColor : idleColor);
            capRenderer.SetPropertyBlock(capColor);
        }
    }
}
