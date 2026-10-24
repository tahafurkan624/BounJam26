using System.Linq;
using UnityEditor;
using UnityEngine;
using static BounJam.EditorTools.LevelKit;
using Kind = BounJam.LevelPiece.Kind;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Something the level editor places: what it's called, how it's put down (drawn as a rectangle, or clicked), the
    /// group under the Level root it goes in and what it is: a shaped <see cref="LevelPiece"/>, or a fixed piece placed
    /// as its prefab (see <see cref="LevelParts"/>). Each one stands on the floor point it's placed at, facing +Z before
    /// the level editor's turn.
    /// </summary>
    public sealed class Placeable
    {
        public enum Placement
        {
            /// <summary>Drag a rectangle for its size; a click puts down the default size.</summary>
            Draw,
            /// <summary>Click to put one down.</summary>
            Click,
            /// <summary>Click to move the level's one blob, ship or dock there.</summary>
            Start,
        }

        public enum StartPoint { None, Blob, Ship, Dock }

        public string Name, Category, Group, Tooltip;
        public Placement How;
        /// <summary>Put on the build height instead of on whatever is under the mouse.</summary>
        public bool OnBuildPlane;
        /// <summary>Has a facing worth showing (it's turned with the level editor's turn).</summary>
        public bool Directional;
        /// <summary>A <see cref="LevelPiece"/> of this kind, or null for a fixed piece.</summary>
        public Kind? Piece;
        /// <summary>A shaped piece's size when it's clicked down rather than drawn.</summary>
        public Vector3 Size;
        /// <summary>A fixed piece's prefab, made the first time it's needed.</summary>
        public System.Func<GameObject> Prefab;
        /// <summary>How far above the floor point the prefab's root goes, on top of where the prefab keeps it.</summary>
        public Vector3 Raise;
        public StartPoint Start;
        /// <summary>The space it takes up, for the preview under the mouse.</summary>
        public Bounds Footprint;

        // Before All, which uses them: static fields are set in order.
        static readonly Bounds ButtonBounds = new Bounds(new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f));
        static readonly Bounds PickupBounds = new Bounds(new Vector3(0f, 0.15f, 0f), new Vector3(0.7f, 0.3f, 0.7f));

        public static readonly string[] Categories = { "Ground", "Water & Hazards", "Machines", "Pickups", "Start & Goal" };

        public static readonly Placeable[] All =
        {
            Shaped(Kind.Platform, "Platform", "Ground", "Ground", true,
                   "Floor to walk on, its top on the build height. Drag to draw one."),
            Shaped(Kind.Ramp, "Ramp", "Ground", "Ground", false,
                   "Rises toward the arrow. Drag to draw its footprint; set its rise in the Inspector.", directional: true),
            Shaped(Kind.Wall, "Wall", "Ground", "Ground", false, "A 4 m long, 1.5 m tall wall. Drag along a line to draw one."),
            Shaped(Kind.Wall, "Half Wall", "Ground", "Ground", false, "A wall half as long: 2 m. Drag along a line to draw one.",
                   size: new Vector3(2f, 1.5f, 0.5f)),
            new Placeable
            {
                Name = "Tile", Category = "Ground", Group = "Ground", How = Placement.Click,
                Tooltip = "A 2 m concrete tile standing 3 m tall: one step up, which only a flower lift reaches.",
                Prefab = () => TilePrefab(LevelEditorSettings.TileLook),
                Footprint = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(2f, 3f, 2f)),
            },

            Shaped(Kind.Pool, "Pool", "Water & Hazards", "Water", true,
                   "A floor with a pool sunk in it: the liquid dissolves in the water, ice floats and swims. Drag to draw the floor."),
            Shaped(Kind.Water, "Water", "Water & Hazards", "Water", true, "A block of water whose surface is on the build height, level with the floor."),
            Shaped(Kind.HotPlate, "Hot Plate", "Water & Hazards", "Hazards", false,
                   "Burns the ship if it's dropped on it; the blob crosses safely."),

            Fixed("Button", "Machines", "Machines", "Pressed by the frozen blob, a crate or the ship. Link it to gates, lifts and magnets.",
                  "Button", t => Button("Button", t, Vector3.zero, CreatePalette()), ButtonBounds),
            Fixed("Gate", "Machines", "Machines", "Sinks into the floor while a linked button is pressed, and for 3 s after.",
                  "Gate", t => Mover("Gate", t, new Vector3(0f, 0.75f, 0f), new Vector3(3f, 1.5f, 0.5f), CreatePalette().Door,
                                     new PressureButton[0], new Vector3(0f, -1.55f, 0f), speed: 3f, holdTime: 3f),
                  new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(3f, 1.5f, 0.5f))),
            Fixed("Half Gate", "Machines", "Machines", "A gate as wide as a half wall (2 m): sinks while a linked button is pressed, and for 3 s after.",
                  "Half Gate", t => Mover("Half Gate", t, new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.5f, 0.5f), CreatePalette().Door,
                                          new PressureButton[0], new Vector3(0f, -1.55f, 0f), speed: 3f, holdTime: 3f),
                  new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(2f, 1.5f, 0.5f))),
            Fixed("Lift", "Machines", "Machines", "Shuttles 1.8 m up and down while a linked button is pressed, carrying what's on it.",
                  "Lift", t => Mover("Lift", t, new Vector3(0f, 0.1f, 0f), new Vector3(2f, 0.2f, 2f), CreatePalette().Lift,
                                     new PressureButton[0], new Vector3(0f, 1.8f, 0f), speed: 1f, holdTime: 0f, cycle: true, carriesRiders: true),
                  new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(2f, 0.2f, 2f))),
            Fixed("Crate", "Machines", "Machines", "Only the frozen blob pushes it; heavy enough to hold a button down.",
                  "Crate", t => Crate("Crate", t, new Vector3(0f, 0.5f, 0f), 1f, 4f, CreatePalette()),
                  new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one)),
            Shaped(Kind.Grate, "Grate", "Machines", "Machines", false,
                   "The liquid flows through it; the frozen blob, the ship and crates don't.", directional: true, how: Placement.Click),
            Existing("Conveyor", "Machines", "Machines", "A spiked belt along the arrow: freeze around a spike to ride it. Set its length in the Inspector.",
                     ConveyorKit.PrefabPath, ConveyorKit.BuildPrefab, Vector3.zero,
                     new Bounds(new Vector3(0f, 0.4f, 0f), new Vector3(1.5f, 0.8f, 6f)), directional: true),
            Fixed("Magnet", "Machines", "Machines", "Faces the arrow and pulls in the frozen blob carrying a pocket magnet while it's on.",
                  "Magnet", t => MagnetKit.Magnet("Magnet", t, new Vector3(0f, -ArtKit.MagnetBounds().min.y, 0f), Vector3.forward,
                                                  new PressureButton[0], new PressureButton[0], MagnetKit.CreateMaterials()),
                  new Bounds(new Vector3(0f, 0.6f, -0.4f), new Vector3(1.2f, 1.2f, 0.8f)), directional: true),
            Fixed("Magnet On", "Machines", "Machines", "A blue button that switches the magnets it's linked to on.",
                  "Button Magnet On", t => MagnetKit.MagnetButton("Button Magnet On", t, Vector3.zero, true, CreatePalette(), MagnetKit.CreateMaterials()),
                  ButtonBounds),
            Fixed("Magnet Off", "Machines", "Machines", "A copper button that switches the magnets it's linked to off.",
                  "Button Magnet Off", t => MagnetKit.MagnetButton("Button Magnet Off", t, Vector3.zero, false, CreatePalette(), MagnetKit.CreateMaterials()),
                  ButtonBounds),
            Shaped(Kind.PiercerPipe, "Piercer Pipe", "Machines", "Machines", false,
                   "Cuts the blob in two halves that come out of its exits. The mouth is at the click, the pipe runs along the arrow.",
                   directional: true, how: Placement.Click),

            Fixed("Droplet", "Pickups", "Pickups", "Water the liquid soaks up to grow, so it stretches further.",
                  "Water Droplet", t => DropletKit.Droplet("Water Droplet", t, Vector3.zero, DropletKit.CreateMaterials()), PickupBounds),
            Fixed("Pocket Magnet", "Pickups", "Pickups", "Freeze around it to carry it; magnets pull you in while you do.",
                  "Pocket Magnet", t => MagnetKit.IronBall("Pocket Magnet", t, Vector3.zero, MagnetKit.CreateMaterials()), PickupBounds),
            Existing("Seed", "Pickups", "Pickups", "Carry it onto a soil bed and melt to grow a flower lift.",
                     GardenKit.SeedPrefabPath, GardenKit.BuildSeedPrefab, new Vector3(0f, GardenKit.SeedRadius, 0f), PickupBounds),
            // Sits a hair proud of the floor, so its rim doesn't flicker against it (as GardenKit.SoilBed).
            Existing("Soil Bed", "Pickups", "Pickups", "Plant a seed here; the flower's ramps unfold toward the arrow. Its top is level with the floor.",
                     GardenKit.SoilBedPrefabPath, GardenKit.BuildSoilBedPrefab, new Vector3(0f, 0.01f, 0f),
                     new Bounds(Vector3.zero, new Vector3(2f, 0.1f, 2f)), directional: true),

            StartAt(StartPoint.Blob, "Blob", "Where the blob starts (the ship moves with it while Ship Rides Blob is on).",
                    new Bounds(new Vector3(0f, 0.4f, 0f), new Vector3(1f, 0.8f, 1f))),
            StartAt(StartPoint.Ship, "Ship", "Where the ship starts, set down on the floor.",
                    new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.6f, 1.4f))),
            StartAt(StartPoint.Dock, "Dock", "The goal: the level is done once the ship rests here.",
                    new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2.4f, 2f, 2.4f))),
        };

        /// <summary>The concrete tile looks there are SM_Tile_* models for.</summary>
        public static readonly string[] TileLooks = { "Grey", "Dark", "Warm", "Mint", "Pink", "Polished", "Terrazzo", "Hazard" };

        public static Placeable Named(string name) => All.First(p => p.Name == name);

        /// <summary>The concrete tile prefab in one of the SM_Tile_* looks.</summary>
        public static GameObject TilePrefab(string look)
            => LevelParts.Prefab($"{LevelParts.Folder}/Tile {look}.prefab",
                                 t => GardenKit.Tile($"Tile {look}", t, look, new Vector3(0f, GardenKit.TileDepth, 0f)));

        static Placeable Shaped(Kind kind, string name, string category, string group, bool onBuildPlane, string tooltip,
                                bool directional = false, Placement how = Placement.Draw, Vector3? size = null)
        {
            Vector3 clicked = size ?? LevelPieces.DefaultSize(kind);
            return new Placeable
            {
                Name = name, Category = category, Group = group, Tooltip = tooltip, How = how, OnBuildPlane = onBuildPlane,
                Directional = directional, Piece = kind, Size = clicked, Footprint = LevelPieces.LocalBounds(kind, clicked),
            };
        }

        /// <summary>A fixed piece whose prefab, Prefabs/Level/<paramref name="prefab"/>, <paramref name="make"/> builds.</summary>
        static Placeable Fixed(string name, string category, string group, string tooltip, string prefab, System.Action<Transform> make,
                               Bounds footprint, bool directional = false)
            => new Placeable
            {
                Name = name, Category = category, Group = group, Tooltip = tooltip, How = Placement.Click, Directional = directional,
                Prefab = () => LevelParts.Prefab($"{LevelParts.Folder}/{prefab}.prefab", make), Footprint = footprint,
            };

        /// <summary>A fixed piece whose prefab the kits already keep, at <paramref name="path"/>.</summary>
        static Placeable Existing(string name, string category, string group, string tooltip, string path, System.Func<GameObject> build,
                                  Vector3 raise, Bounds footprint, bool directional = false)
            => new Placeable
            {
                Name = name, Category = category, Group = group, Tooltip = tooltip, How = Placement.Click, Directional = directional,
                Prefab = () => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? build(), Raise = raise, Footprint = footprint,
            };

        static Placeable StartAt(StartPoint start, string name, string tooltip, Bounds footprint)
            => new Placeable
            {
                Name = name, Category = "Start & Goal", Tooltip = tooltip, How = Placement.Start, Start = start, Footprint = footprint,
            };
    }
}
