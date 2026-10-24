using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds the greybox sandbox: a walled arena with every test piece (ramp, steps, low tunnel, narrow gap, bumps,
    /// grate, crates, buttons, gate, lift, hot plate, water pool, iron ball, magnet tower, piercer pipe, spiked conveyor,
    /// seed and soil bed under a ledge, water droplets and ledges across gaps, a test dock), the blob, the ship, the
    /// camera and level logic.
    /// It never completes; R or falling restarts it. Running it again replaces everything it created in the scene;
    /// the template's light and volume stay.
    /// </summary>
    public static class SandboxBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/Sandbox.unity";

        static readonly string[] GeneratedRoots = { "Level", "Blob", "PaperShip", "CM FollowCam", CameraGroupName, "Game" };
        static readonly Vector3 Spawn = new Vector3(0f, 0f, -12f);

        [MenuItem("BounJam/Build Sandbox Level")]
        public static void Build()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(GeneratedRoots, root.name) >= 0)
                    Object.DestroyImmediate(root);

            Palette palette = CreatePalette();
            Transform level = BuildArena(palette);
            MagnetKit.SandboxPieces(level, palette);
            MagnetKit.UpgradeBlobPrefab();
            PipeKit.SandboxPieces(level);
            PipeKit.UpgradeBlobPrefab();
            ConveyorKit.SandboxPieces(level);
            ConveyorKit.UpgradeBlobPrefab();
            GardenKit.SandboxPieces(level);
            DropletKit.SandboxPieces(level, palette);
            DropletKit.UpgradeBlobPrefab();
            var (blob, ship) = PlacePlayer(palette, Spawn, Spawn + Vector3.up * CoreRadius + ShipOnBlobOffset);
            // A dock to try delivering, but no next scene: the sandbox never completes.
            ShipDock dock = Dock("Test Dock", level, new Vector3(14f, 0f, -13f), palette, ship);
            BuildCamera(blob.transform);
            BuildGame("Sandbox", null, false, blob, ship, dock);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Sandbox level built.");
        }

        static Transform BuildArena(Palette m)
        {
            var level = new GameObject("Level").transform;

            // Arena: 36 x 36 m floor, top surface at y = 0, low walls all around. A 7 x 7 m pool is sunk into the
            // west side: the liquid dissolves in it, the solid floats and swims, and climbs out over the low edge
            // or up the ramp on its north side.
            Transform arena = Group("Arena", level);
            const float poolDepth = 1.6f;
            FloorWithPool("Floor", arena, new Rect(-18f, -18f, 36f, 36f), new Rect(-16.5f, -12f, 7f, 7f), 0f, poolDepth, 0.15f, m);
            float rampRise = poolDepth, rampAngle = 20f;
            Ramp("Pool Ramp", arena, -10.5f, -5f - rampRise / Mathf.Tan(rampAngle * Mathf.Deg2Rad), -poolDepth, rampRise, 2f,
                 rampAngle, m.Structure);
            Box("Wall North", arena, new Vector3(0f, 0.75f, 18.5f), new Vector3(38f, 1.5f, 1f), m.Wall);
            Box("Wall South", arena, new Vector3(0f, 0.75f, -18.5f), new Vector3(38f, 1.5f, 1f), m.Wall);
            Box("Wall East", arena, new Vector3(18.5f, 0.75f, 0f), new Vector3(1f, 1.5f, 36f), m.Wall);
            Box("Wall West", arena, new Vector3(-18.5f, 0.75f, 0f), new Vector3(1f, 1.5f, 36f), m.Wall);

            // 15 degree ramp up to a 2 m platform with open edges to fall off.
            Transform ramp = Group("Ramp And Platform", level);
            ArtKit.Platform("Platform", ramp, new Vector3(11f, 1f, 9f), new Vector3(10f, 2f, 8f), m.Structure);
            float rampRun = 2f / Mathf.Tan(15f * Mathf.Deg2Rad);
            Ramp("Ramp", ramp, 11f, 5f - rampRun, 0f, 2f, 4f, 15f, m.Structure);

            // Steps rising 0.15 m each, then a landing: shows which step heights the blob climbs.
            Transform steps = Group("Steps", level);
            for (int i = 0; i < 4; i++)
            {
                float height = 0.15f * (i + 1);
                Box($"Step {height:0.00}m", steps, new Vector3(-11f, height * 0.5f, -2f + i * 1.5f), new Vector3(4f, height, 1.5f), m.Structure);
            }
            Box("Landing", steps, new Vector3(-11f, 0.3f, 5f), new Vector3(4f, 0.6f, 3f), m.Structure);

            // Low tunnel, 0.65 m clearance: the liquid flattens through, the ship's sail gets knocked off.
            Transform tunnel = Group("Low Tunnel", level);
            Box("Tunnel Wall Left", tunnel, new Vector3(-1.75f, 0.6f, 11f), new Vector3(0.5f, 1.2f, 6f), m.Wall);
            Box("Tunnel Wall Right", tunnel, new Vector3(1.75f, 0.6f, 11f), new Vector3(0.5f, 1.2f, 6f), m.Wall);
            Box("Tunnel Ceiling", tunnel, new Vector3(0f, 0.8f, 11f), new Vector3(4f, 0.3f, 6f), m.Structure);

            // Narrow gap, 0.7 m wide: the liquid squeezes through sideways.
            Transform gap = Group("Narrow Gap", level);
            Box("Gap Wall Left", gap, new Vector3(-14.925f, 0.75f, 10f), new Vector3(5.15f, 1.5f, 0.5f), m.Wall);
            Box("Gap Wall Right", gap, new Vector3(-8.825f, 0.75f, 10f), new Vector3(5.65f, 1.5f, 0.5f), m.Wall);

            // Pillars to wrap around and low bumps to flow over.
            Transform obstacles = Group("Pillars And Bumps", level);
            Shape(PrimitiveType.Cylinder, "Pillar Wide", obstacles, new Vector3(-4f, 1f, 4f), new Vector3(1f, 1f, 1f), m.Obstacle);
            Shape(PrimitiveType.Cylinder, "Pillar Thin", obstacles, new Vector3(-2f, 1f, 5.5f), new Vector3(0.6f, 1f, 0.6f), m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump A", obstacles, new Vector3(3f, -0.4f, -1f), Vector3.one * 1.4f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump B", obstacles, new Vector3(4.5f, -0.45f, 0.2f), Vector3.one * 1.2f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump C", obstacles, new Vector3(3.4f, -0.4f, 1.4f), Vector3.one * 1.0f, m.Obstacle);
            Shape(PrimitiveType.Sphere, "Bump D", obstacles, new Vector3(5f, -0.5f, -1.8f), Vector3.one * 1.6f, m.Obstacle);

            // Grate in a wall: liquid flows through (LiquidPassable layer), the solid form and the ship don't.
            Transform grate = Group("Grate Wall", level);
            Box("Grate Wall Left", grate, new Vector3(5f, 0.75f, -8f), new Vector3(6f, 1.5f, 0.5f), m.Wall);
            Box("Grate Wall Right", grate, new Vector3(15.1f, 0.75f, -8f), new Vector3(5.8f, 1.5f, 0.5f), m.Wall);
            Box("Grate", grate, new Vector3(10f, 0.75f, -8f), new Vector3(4.4f, 1.5f, 0.15f), m.Grate, "LiquidPassable");

            // Pushable crates: only the solid form moves them; heavy enough to hold a button down.
            Transform crates = Group("Crates", level);
            Crate("Crate Big", crates, new Vector3(-5f, 0.5f, -6f), 1f, 4f, m);
            Crate("Crate Small", crates, new Vector3(-6.6f, 0.4f, -6f), 0.8f, 3f, m);

            Transform interactables = Group("Interactables", level);
            // A locked room in the south-west corner. Its gate sinks into the floor while the button is pressed
            // and stays open 4 s after release: press as a solid, melt, slip in as liquid.
            Box("Room Wall North", interactables, new Vector3(-14f, 0.75f, -15f), new Vector3(8f, 1.5f, 0.5f), m.Wall);
            Box("Room Wall North Short", interactables, new Vector3(-6.75f, 0.75f, -15f), new Vector3(1.5f, 1.5f, 0.5f), m.Wall);
            Box("Room Wall East", interactables, new Vector3(-5.75f, 0.75f, -16.5f), new Vector3(0.5f, 1.5f, 3f), m.Wall);
            PressureButton gateButton = Button("Button Gate", interactables, new Vector3(-3f, 0f, -13f), m);
            Mover("Room Gate", interactables, new Vector3(-8.75f, 0.75f, -15f), new Vector3(2.5f, 1.5f, 0.5f), m.Door,
                  new[] { gateButton }, new Vector3(0f, -1.55f, 0f), speed: 2f, holdTime: 4f);

            // Push the big crate 3.5 m south onto this button to power a lift next to the platform.
            PressureButton liftButton = Button("Button Lift", interactables, new Vector3(-5f, 0f, -9.5f), m);
            Mover("Lift", interactables, new Vector3(5f, 0.1f, 9f), new Vector3(2f, 0.2f, 2f), m.Lift,
                  new[] { liftButton }, new Vector3(0f, 1.8f, 0f), speed: 1f, holdTime: 0f, cycle: true, carriesRiders: true);

            // Burns the ship if it's dropped here; the blob crosses safely.
            HotPlate("Hot Plate", interactables, new Vector3(15.5f, 0f, -3f), new Vector2(3.5f, 3f), m);
            return level;
        }
    }
}
