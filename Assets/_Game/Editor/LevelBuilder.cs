using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds the playable levels as fresh scenes and puts them in Build Settings in play order
    /// (Level01 -> Level02 -> Level03, then the Sandbox, which isn't part of the run). Each level is a short course
    /// floating over the void: falling off loses the run, a dropped ship can be scooped back up, and the level is
    /// done once the ship rests on the dock. Rebuilding overwrites the level scenes.
    /// </summary>
    public static class LevelBuilder
    {
        const string ScenesFolder = "Assets/_Game/Scenes";

        struct Spots
        {
            public Vector3 Blob;   // on the floor
            public Vector3 Ship;
            public Vector3 Dock;   // on the floor
        }

        delegate Spots Layout(Transform level, Palette m);

        [MenuItem("BounJam/Build All Levels")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string previous = SceneManager.GetActiveScene().path;

            Palette m = CreatePalette();
            Build("Level01", "Level 1: First Voyage", "Level02", false, m, FirstVoyage);
            Build("Level02", "Level 2: Hold the Gate", "Level03", false, m, HoldTheGate);
            Build("Level03", "Level 3: Heavy Lifting", "Level01", true, m, HeavyLifting);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{ScenesFolder}/Level01.unity", true),
                new EditorBuildSettingsScene($"{ScenesFolder}/Level02.unity", true),
                new EditorBuildSettingsScene($"{ScenesFolder}/Level03.unity", true),
                new EditorBuildSettingsScene(SandboxBuilder.ScenePath, true),
            };
            AssetDatabase.SaveAssets(); // writes the build settings to ProjectSettings now, not at editor shutdown

            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            Debug.Log("[BounJam] Levels built: Level01, Level02, Level03.");
        }

        static void Build(string sceneName, string title, string nextScene, bool isFinal, Palette m, Layout layout)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupEnvironment();

            var level = new GameObject("Level").transform;
            Spots spots = layout(level, m);
            var (blob, ship) = PlacePlayer(m, spots.Blob, spots.Ship);
            ShipDock dock = Dock("Dock", level, spots.Dock, m, ship);
            BuildCamera(blob.transform);
            BuildGame(title, nextScene, isFinal, blob, ship, dock);

            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{sceneName}.unity");
        }

        static Vector3 OnBlob(Vector3 blobSpawn) => blobSpawn + Vector3.up * CoreRadius + ShipOnBlobOffset;

        /// <summary>Carry the ship along a narrow path, across a hot plate, up a ramp and round a corner to the dock.</summary>
        static Spots FirstVoyage(Transform level, Palette m)
        {
            Floor("Start", level, 0f, 0f, 6f, 6f, 0f, m.Floor);
            Floor("Path", level, 0f, 8f, 4f, 10f, 0f, m.Floor);
            HotPlate("Hot Plates", level, new Vector3(0f, 0f, 8.5f), new Vector2(4f, 3f), m);

            const float upper = 1.5f;
            float rampTop = Ramp("Ramp", level, 0f, 13f, 0f, upper, 4f, 15f, m.Structure);
            Floor("Upper Path", level, 0f, rampTop + 4f, 4f, 8f, upper, m.Structure);
            Floor("Upper Turn", level, 5f, rampTop + 6f, 6f, 4f, upper, m.Structure);

            var blob = new Vector3(0f, 0f, -1.5f);
            return new Spots { Blob = blob, Ship = OnBlob(blob), Dock = new Vector3(6f, upper, rampTop + 6f) };
        }

        /// <summary>
        /// A wall blocks the yard. Freeze on the button to sink the gate, melt and get the ship through in time.
        /// The grate next to the gate lets the liquid through but scrapes the ship off.
        /// </summary>
        static Spots HoldTheGate(Transform level, Palette m)
        {
            Floor("Start", level, 0f, 0f, 8f, 8f, 0f, m.Floor);
            Floor("Yard", level, 0f, 10f, 8f, 12f, 0f, m.Floor);

            PressureButton button = Button("Button", level, new Vector3(-2.5f, 0f, 5.5f), m);
            Box("Wall Left", level, new Vector3(-2.75f, 0.75f, 8f), new Vector3(2.5f, 1.5f, 0.5f), m.Wall);
            Mover("Gate", level, new Vector3(0f, 0.75f, 8f), new Vector3(3f, 1.5f, 0.5f), m.Door,
                  new[] { button }, new Vector3(0f, -1.55f, 0f), speed: 3f, holdTime: 3f);
            Box("Grate", level, new Vector3(2.25f, 0.75f, 8f), new Vector3(1.5f, 1.5f, 0.15f), m.Grate, "LiquidPassable");
            Box("Wall Right", level, new Vector3(3.5f, 0.75f, 8f), new Vector3(1f, 1.5f, 0.5f), m.Wall);

            var blob = new Vector3(0f, 0f, -1.5f);
            return new Spots { Blob = blob, Ship = OnBlob(blob), Dock = new Vector3(0f, 0f, 13f) };
        }

        /// <summary>
        /// The dock is on a 2 m deck. Push the crate onto the button to power the lift, then pick up the ship
        /// (it starts set down on the ground), board the lift at the bottom and ride up.
        /// </summary>
        static Spots HeavyLifting(Transform level, Palette m)
        {
            Floor("Ground", level, 0f, 0f, 12f, 16f, 0f, m.Floor);
            const float deck = 2f;
            Box("Upper Deck", level, new Vector3(0f, deck - 3f, 11f), new Vector3(8f, 6f, 6f), m.Structure);

            PressureButton button = Button("Button", level, new Vector3(-3.5f, 0f, -2.5f), m);
            Crate("Crate", level, new Vector3(-3.5f, 0.5f, 1.5f), 1f, 4f, m);
            // A powered elevator: once the crate holds the button, it shuttles between the ground and the deck.
            Mover("Lift", level, new Vector3(2.5f, 0.1f, 7f), new Vector3(2f, 0.2f, 2f), m.Lift,
                  new[] { button }, new Vector3(0f, deck - 0.2f, 0f), speed: 1f, holdTime: 0f, cycle: true);

            return new Spots
            {
                Blob = new Vector3(0f, 0f, -5f),
                Ship = new Vector3(-0.5f, 0.02f, 4.5f),
                Dock = new Vector3(0f, deck, 11.5f),
            };
        }
    }
}
