using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The Grate Bridge prefab: a Grate Floor that its buttons turn into floor. A flat drawbridge, the drawbridge's slab
    /// lying level with the grate's top, waits as a stub along the grate's back edge; once a linked button is pressed (a
    /// latch button, say) it grows across the grate toward +Z and stays (see <see cref="BounJam.Drawbridge"/>). Until
    /// it's covered the liquid drips through the grate while the frozen blob, the ship and crates stand on it. The blob
    /// or a crate in the deck's way holds it up; the ship never does.
    /// </summary>
    public static class GrateBridgeKit
    {
        public const string PrefabPath = LevelParts.Folder + "/Grate Bridge.prefab";
        // As thick as the drawbridge's slab.
        const float DeckThickness = 0.4f;
        // The slab's model sits this far above its collider, so it covers the grate's frame, level with the slab's top,
        // without flickering against it; what stands on it still stands level with the floor round the grate.
        const float ModelLift = 0.002f;
        // The slab overhangs the grate this far all round, so the frame's sides don't show through the slab's.
        const float Overhang = 0.002f;

        /// <summary>The space the prefab takes up, for the level editor's preview: the deck, under its top at the pivot.</summary>
        public static Bounds Footprint => new Bounds(new Vector3(0f, -DeckThickness * 0.5f, 0f),
                                                     new Vector3(ArtKit.FloorGrateSize, DeckThickness, ArtKit.FloorGrateSize));

        /// <summary>Makes the Grate Bridge prefab if it's missing; one already there is left as it is.</summary>
        [MenuItem("BounJam/Make Grate Bridge Prefab")]
        public static void Make() => EditorGUIUtility.PingObject(Prefab());

        /// <summary>The Grate Bridge prefab, made if it's missing.</summary>
        public static GameObject Prefab() => LevelParts.Prefab(PrefabPath, parent =>
        {
            float size = ArtKit.FloorGrateSize + 2f * Overhang;
            var root = new GameObject("Grate Bridge") { layer = LayerMask.NameToLayer("Environment") };
            root.transform.SetParent(parent, false);
            LevelParts.Instance(GrateFloorKit.Prefab(), root.transform, "Grate");

            // Pivoted at the grate's back edge, growing toward +Z across it.
            var deck = new GameObject("Deck") { layer = root.layer };
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, 0f, -size * 0.5f);
            Transform slab = LevelParts.Instance(LevelParts.RampPart, deck.transform, "Ramp").transform;
            slab.localPosition = new Vector3(0f, -DeckThickness * 0.5f, size * 0.5f);
            slab.localScale = new Vector3(size, DeckThickness, size);
            foreach (Transform model in slab) model.localPosition += Vector3.up * (ModelLift / DeckThickness);

            var settings = new SerializedObject(LevelKit.DrawbridgeDeck(root, deck, new PressureButton[0]));
            settings.FindProperty("waitsForShip").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        });
    }
}
