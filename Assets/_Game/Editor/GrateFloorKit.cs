using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The Grate Floor prefab: one floor grate panel, 2 m square, with its top at the pivot like a platform, on its own.
    /// It's a LiquidPassable plate, so the liquid drips straight through it while the frozen blob, the ship and crates
    /// stand on it. Unlike the level editor's Floor Grate there's no floor or drain around it: lay it over a gap, a pit
    /// or water, side by side for a wider grate, or scale it in X and Z.
    /// </summary>
    public static class GrateFloorKit
    {
        public const string PrefabPath = LevelParts.Folder + "/Grate Floor.prefab";
        // Deep enough to hold the frozen blob landing on it at speed; the panel's slats and crossbars hang about this far.
        const float PlateThickness = 0.1f;

        /// <summary>The space the prefab takes up, for the level editor's preview: the plate, under its top at the pivot.</summary>
        public static Bounds Footprint => new Bounds(new Vector3(0f, -PlateThickness * 0.5f, 0f),
                                                     new Vector3(ArtKit.FloorGrateSize, PlateThickness, ArtKit.FloorGrateSize));

        /// <summary>Makes the Grate Floor prefab if it's missing; one already there is left as it is.</summary>
        [MenuItem("BounJam/Make Grate Floor Prefab")]
        public static void Make() => EditorGUIUtility.PingObject(Prefab());

        /// <summary>The Grate Floor prefab, made if it's missing.</summary>
        public static GameObject Prefab() => LevelParts.Prefab(PrefabPath, parent =>
        {
            GameObject root = ArtKit.FloorGratePanel("Grate Floor", parent);
            var plate = root.AddComponent<BoxCollider>();
            plate.center = Footprint.center;
            plate.size = Footprint.size;
        });
    }
}
