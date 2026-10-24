using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>The level editor's own settings, kept per user in EditorPrefs (written only when they change).</summary>
    public static class LevelEditorSettings
    {
        const string Prefix = "BounJam.LevelEditor.";

        /// <summary>Grid step for placing and resizing pieces, m.</summary>
        public static float Grid
        {
            get => EditorPrefs.GetFloat(Prefix + "Grid", 0.5f);
            set => SetFloat("Grid", Mathf.Max(0.05f, value), Grid);
        }

        public static bool Snapping
        {
            get => EditorPrefs.GetBool(Prefix + "Snapping", true);
            set => SetBool("Snapping", value, Snapping);
        }

        /// <summary>Height of the plane platforms and pools are drawn on (their top), and that clicks fall back to over the void, m.</summary>
        public static float BuildHeight
        {
            get => EditorPrefs.GetFloat(Prefix + "BuildHeight", 0f);
            set => SetFloat("BuildHeight", value, BuildHeight);
        }

        /// <summary>Turn of the next piece placed, degrees about Y, in quarter turns.</summary>
        public static int Turn
        {
            get => EditorPrefs.GetInt(Prefix + "Turn", 0);
            set
            {
                int turn = ((value % 360) + 360) % 360;
                if (turn != Turn) EditorPrefs.SetInt(Prefix + "Turn", turn);
            }
        }

        public static bool ShipRidesBlob
        {
            get => EditorPrefs.GetBool(Prefix + "ShipRidesBlob", true);
            set => SetBool("ShipRidesBlob", value, ShipRidesBlob);
        }

        public static bool ShowLinks
        {
            get => EditorPrefs.GetBool(Prefix + "ShowLinks", true);
            set => SetBool("ShowLinks", value, ShowLinks);
        }

        public static bool SelectWholePieces
        {
            get => EditorPrefs.GetBool(Prefix + "SelectWholePieces", true);
            set => SetBool("SelectWholePieces", value, SelectWholePieces);
        }

        public static string TileLook
        {
            get => EditorPrefs.GetString(Prefix + "TileLook", "Grey");
            set
            {
                if (value != TileLook) EditorPrefs.SetString(Prefix + "TileLook", value);
            }
        }

        public static float Snap(float value)
        {
            if (!Snapping) return value;
            float grid = Grid;
            return Mathf.Round(value / grid) * grid;
        }

        /// <summary>The smallest a piece can be resized to, m.</summary>
        public static float Smallest => Snapping ? Grid : 0.1f;

        static void SetFloat(string key, float value, float current)
        {
            if (!Mathf.Approximately(value, current)) EditorPrefs.SetFloat(Prefix + key, value);
        }

        static void SetBool(string key, bool value, bool current)
        {
            if (value != current) EditorPrefs.SetBool(Prefix + key, value);
        }
    }
}
