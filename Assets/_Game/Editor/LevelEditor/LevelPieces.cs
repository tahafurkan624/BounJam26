using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BounJam.EditorTools.LevelKit;
using Kind = BounJam.LevelPiece.Kind;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds <see cref="LevelPiece"/>s from their settings out of the part prefabs (see <see cref="LevelParts"/>), and rebuilds one whenever its settings
    /// change: in the Inspector, from its Scene view handles, or through undo and redo. Everything under a piece is
    /// rebuilt from scratch each time and is never recorded for undo; undoing a setting rebuilds the piece again.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelPieces
    {
        // What each piece's children were last built from in this editor session. It wins over the piece's saved
        // BuiltFrom, which undo can roll back while the rebuilt children stay.
        static readonly Dictionary<LevelPiece, string> built = new Dictionary<LevelPiece, string>();
        static readonly HashSet<LevelPiece> pending = new HashSet<LevelPiece>();

        static LevelPieces()
        {
            LevelPiece.SettingsChanged += Queue;
            Undo.undoRedoPerformed += QueueAll;
            EditorApplication.update += Flush;
        }

        // ---------------------------------------------------------------- Kinds

        public static Vector3 DefaultSize(Kind kind) => kind switch
        {
            Kind.Platform => new Vector3(6f, 1f, 6f),
            Kind.Wall => new Vector3(4f, 1.5f, 0.5f),
            Kind.Ramp => new Vector3(4f, 1.5f, 5.6f), // 15 degrees
            Kind.HotPlate => new Vector3(3f, 0.25f, 3f),
            Kind.Water => new Vector3(4f, 1f, 4f),
            Kind.Pool => new Vector3(8f, 1.6f, 8f),
            Kind.Grate => new Vector3(2f, 1.5f, 0.3f),
            _ => Vector3.one,
        };

        /// <summary>The sizes that can be changed; the rest are fixed by the piece.</summary>
        public static PrimitiveBoundsHandle.Axes SizeAxes(Kind kind) => kind switch
        {
            Kind.HotPlate => PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Z,
            Kind.Grate => PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y,
            Kind.PiercerPipe => PrimitiveBoundsHandle.Axes.None,
            _ => PrimitiveBoundsHandle.Axes.All,
        };

        /// <summary>Platforms, pools and water have their top at the pivot (level with the floor); the rest stand on the floor at it.</summary>
        public static bool PivotOnTop(Kind kind) => kind == Kind.Platform || kind == Kind.Pool || kind == Kind.Water;

        /// <summary>The space the piece takes up, in its own space.</summary>
        public static Bounds LocalBounds(LevelPiece piece)
        {
            if (piece.PieceKind != Kind.PiercerPipe) return LocalBounds(piece.PieceKind, piece.Size);
            var fork = new Vector3(0f, 0f, piece.BladeDistance);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bounds.Encapsulate(fork);
            bounds.Encapsulate(piece.FirstExit);
            bounds.Encapsulate(piece.SecondExit);
            bounds.Expand(new Vector3(PipeKit.PipeRadius * 2f, 0f, PipeKit.PipeRadius * 2f));
            bounds.max = new Vector3(bounds.max.x, 1.5f, bounds.max.z);
            return bounds;
        }

        public static Bounds LocalBounds(Kind kind, Vector3 size)
        {
            if (kind == Kind.PiercerPipe) return new Bounds(new Vector3(0f, 0.75f, 4f), new Vector3(6f, 1.5f, 8f));
            if (kind == Kind.HotPlate) size.y = 0.25f;
            if (kind == Kind.Grate) size.z = 0.3f;
            return new Bounds(new Vector3(0f, PivotOnTop(kind) ? -size.y * 0.5f : size.y * 0.5f, 0f), size);
        }

        // ---------------------------------------------------------------- Creating and building

        /// <summary>A new piece of <paramref name="kind"/> under <paramref name="parent"/>, built and recorded for undo.</summary>
        public static LevelPiece Create(Kind kind, string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 size)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, name));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var piece = go.AddComponent<LevelPiece>();
            piece.PieceKind = kind;
            piece.Size = size;
            if (kind == Kind.Pool)
            {
                // A hole with a 1.5 m wide walk all round it, at least 1 m across.
                piece.PoolSize = new Vector2(Mathf.Max(1f, size.x - 3f), Mathf.Max(1f, size.z - 3f));
            }
            Rebuild(piece);
            Undo.RegisterCreatedObjectUndo(go, $"Place {name}");
            return piece;
        }

        /// <summary>Throws away everything under the piece and builds it again from its settings.</summary>
        public static void Rebuild(LevelPiece piece)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(piece.gameObject))
            {
                Debug.LogWarning($"[BounJam] {piece.name} is part of a prefab instance; open the prefab to rebuild it.", piece);
                return;
            }

            Transform root = piece.transform;
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

            // Built around the origin, then moved under the piece as they are: in the piece's own space.
            var space = new GameObject("Piece Space").transform;
            try
            {
                Build(piece, space);
            }
            finally
            {
                while (space.childCount > 0) space.GetChild(0).SetParent(root, false);
                Object.DestroyImmediate(space.gameObject);
            }

            string signature = Signature(piece);
            piece.BuiltFrom = signature;
            built[piece] = signature;
            EditorUtility.SetDirty(piece);
            var scene = piece.gameObject.scene;
            if (scene.IsValid() && !EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.MarkSceneDirty(scene);
        }

        // Out of the part prefabs, so changing one changes every piece built from it.
        static void Build(LevelPiece piece, Transform space)
        {
            Vector3 size = piece.Size;
            switch (piece.PieceKind)
            {
                case Kind.Platform:
                    LevelParts.Platform("Platform", space, new Vector3(0f, -size.y * 0.5f, 0f), size);
                    break;
                case Kind.Wall:
                    LevelParts.Wall(space, size);
                    break;
                case Kind.Ramp:
                    LevelParts.Ramp(space, size);
                    break;
                case Kind.HotPlate:
                    LevelParts.HotPlate(space, size);
                    break;
                case Kind.Water:
                    WaterVolume("Water", space, new Vector3(0f, -size.y * 0.5f, 0f), size, CreatePalette());
                    break;
                case Kind.Pool:
                {
                    // The hole stays inside the floor, and the water inside the hole.
                    var hole = Vector2.Min(piece.PoolSize, new Vector2(size.x, size.z));
                    Vector2 room = (new Vector2(size.x, size.z) - hole) * 0.5f;
                    var offset = new Vector2(Mathf.Clamp(piece.PoolOffset.x, -room.x, room.x), Mathf.Clamp(piece.PoolOffset.y, -room.y, room.y));
                    var floor = new Rect(-size.x * 0.5f, -size.z * 0.5f, size.x, size.z);
                    var pool = new Rect(offset - hole * 0.5f, hole);
                    float waterBelow = Mathf.Min(piece.WaterBelowTop, size.y - 0.1f);
                    LevelParts.Pool(space, floor, pool, size.y, waterBelow);
                    break;
                }
                case Kind.Grate:
                    LevelParts.Grate(space, new Vector2(size.x, size.y));
                    break;
                case Kind.PiercerPipe:
                    LevelParts.Pipe(space, piece.BladeDistance, piece.FirstExit, piece.SecondExit);
                    break;
            }
        }

        static string Signature(LevelPiece p)
            => string.Join("|", p.PieceKind.ToString(), Format(p.Size), Format(p.PoolSize), Format(p.PoolOffset),
                           Format(p.WaterBelowTop), Format(p.BladeDistance), Format(p.FirstExit), Format(p.SecondExit));

        static string Format(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        static string Format(Vector2 v) => $"{Format(v.x)},{Format(v.y)}";
        static string Format(Vector3 v) => $"{Format(v.x)},{Format(v.y)},{Format(v.z)}";

        // ---------------------------------------------------------------- Keeping pieces up to date

        /// <summary>Rebuilds the piece if its settings changed since it was last built.</summary>
        public static void Refresh(LevelPiece piece)
        {
            if (piece == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(piece)) return;
            string now = Signature(piece);
            if (!built.TryGetValue(piece, out string last)) last = piece.BuiltFrom;
            if (now != last) Rebuild(piece);
            else built[piece] = now;
        }

        // Settings change inside OnValidate, where nothing may be destroyed; rebuild on the next editor update. Not
        // delayCall, which an editor in the background never gets round to.
        static void Queue(LevelPiece piece) => pending.Add(piece);

        static void QueueAll()
        {
            foreach (LevelPiece piece in Object.FindObjectsByType<LevelPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Queue(piece);
        }

        static void Flush()
        {
            if (pending.Count == 0) return;
            var pieces = new List<LevelPiece>(pending);
            pending.Clear();
            foreach (LevelPiece piece in pieces) Refresh(piece);

            // Forget deleted pieces.
            var dead = new List<LevelPiece>();
            foreach (LevelPiece piece in built.Keys) if (piece == null) dead.Add(piece);
            foreach (LevelPiece piece in dead) built.Remove(piece);
        }
    }
}
