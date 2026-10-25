using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A level piece whose shape is made from these settings: the platforms, walls, ramps, hot plates, water, pools,
    /// grates, floor grates and piercer pipes placed with the level editor (BounJam/Level Editor). Change a setting here
    /// or drag the piece's handles in the Scene view and the editor rebuilds everything under it, so hand edits belong
    /// on other objects. The game itself never reads it.
    /// </summary>
    [DisallowMultipleComponent, SelectionBase]
    public class LevelPiece : MonoBehaviour
    {
        // New kinds go at the end: scenes keep the kind as its number.
        public enum Kind { Platform, Wall, Ramp, HotPlate, Water, Pool, Grate, PiercerPipe, FloorGrate, TopPlatform }
        /// <summary>What a floor or wall is made of: concrete blocks and brick, or tropical log blocks and wood.</summary>
        public enum FloorType { Concrete, Tropical }

        [SerializeField] Kind kind;
        [Tooltip("Width (x), height (y) and length (z), m. Platforms, top platforms, pools, floor grates and water have their top at the " +
                 "pivot; everything else stands on the floor at the pivot. Ramps rise toward +Z.")]
        [SerializeField] Vector3 size = new Vector3(6f, 1f, 6f);
        [Tooltip("Platform, pool and floor grate: what the floor is made of, concrete blocks or tropical log blocks. " +
                 "Wall: concrete brick or tropical wood. Ramp: a concrete slab or tropical logs.")]
        [SerializeField] FloorType floor;

        [Tooltip("Pool and floor grate: the hole sunk into the floor, width and length, m.")]
        [SerializeField] Vector2 poolSize = new Vector2(4f, 4f);
        [Tooltip("Pool and floor grate: how far the hole's centre is from the floor's centre, m.")]
        [SerializeField] Vector2 poolOffset;
        [Tooltip("Pool: how far the water's surface is below the floor top, m.")]
        [SerializeField] float waterBelowTop = 0.15f;

        [Tooltip("Piercer pipe: how far in from the mouth the blade cuts the blob in two, m.")]
        [SerializeField] float bladeDistance = 3.5f;
        [Tooltip("Piercer pipe: where the branch the ship comes out of ends, on the floor, from the mouth.")]
        [SerializeField] Vector3 firstExit = new Vector3(2.5f, 0f, 7.5f);
        [Tooltip("Piercer pipe: where the other branch ends, on the floor, from the mouth.")]
        [SerializeField] Vector3 secondExit = new Vector3(-2.5f, 0f, 7.5f);

        // The settings everything under the piece was last built from, so the editor only rebuilds on a real change.
        [SerializeField, HideInInspector] string builtFrom;

        public Kind PieceKind { get => kind; set => kind = value; }
        public Vector3 Size { get => size; set => size = value; }
        public FloorType Floor { get => floor; set => floor = value; }
        public Vector2 PoolSize { get => poolSize; set => poolSize = value; }
        public Vector2 PoolOffset { get => poolOffset; set => poolOffset = value; }
        public float WaterBelowTop { get => waterBelowTop; set => waterBelowTop = value; }
        public float BladeDistance { get => bladeDistance; set => bladeDistance = value; }
        public Vector3 FirstExit { get => firstExit; set => firstExit = value; }
        public Vector3 SecondExit { get => secondExit; set => secondExit = value; }
        public string BuiltFrom { get => builtFrom; set => builtFrom = value; }

#if UNITY_EDITOR
        /// <summary>Raised in the editor whenever a piece's settings change (Inspector, undo, paste), so it can be rebuilt.</summary>
        public static event System.Action<LevelPiece> SettingsChanged;

        void OnValidate()
        {
            size = Vector3.Max(size, Vector3.one * 0.05f);
            poolSize = Vector2.Max(poolSize, Vector2.one * 0.5f);
            waterBelowTop = Mathf.Max(0f, waterBelowTop);
            bladeDistance = Mathf.Max(1f, bladeDistance);
            firstExit.y = secondExit.y = 0f;
            SettingsChanged?.Invoke(this);
        }
#endif
    }
}
