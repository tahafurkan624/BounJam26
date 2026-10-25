using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Kind = BounJam.LevelPiece.Kind;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The Inspector for <see cref="LevelPiece"/>: only the settings its kind uses, named for what they do. In the Scene
    /// view a box with a handle on each face resizes it (snapped to the level editor's grid), and the scale tool works
    /// too: the scale is turned into the piece's size when you let go. A piercer pipe gets handles on its exits instead.
    /// </summary>
    [CustomEditor(typeof(LevelPiece)), CanEditMultipleObjects]
    public class LevelPieceEditor : UnityEditor.Editor
    {
        static readonly Color HandleColor = new Color(1f, 0.78f, 0.2f);

        SerializedProperty kind, size, floor, poolSize, poolOffset, waterBelowTop, bladeDistance, firstExit, secondExit;
        readonly BoxBoundsHandle box = new BoxBoundsHandle { handleColor = HandleColor, wireframeColor = HandleColor };

        void OnEnable()
        {
            kind = serializedObject.FindProperty("kind");
            size = serializedObject.FindProperty("size");
            floor = serializedObject.FindProperty("floor");
            poolSize = serializedObject.FindProperty("poolSize");
            poolOffset = serializedObject.FindProperty("poolOffset");
            waterBelowTop = serializedObject.FindProperty("waterBelowTop");
            bladeDistance = serializedObject.FindProperty("bladeDistance");
            firstExit = serializedObject.FindProperty("firstExit");
            secondExit = serializedObject.FindProperty("secondExit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(kind);
            if (kind.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("These pieces are of different kinds; select one kind to edit their settings.", MessageType.None);
            }
            else
            {
                switch ((Kind)kind.enumValueIndex)
                {
                    case Kind.Platform:
                        SizeFields("Width", "Thickness", "Length");
                        EditorGUILayout.PropertyField(floor);
                        break;
                    case Kind.Wall:
                        SizeFields("Width", "Height", "Thickness");
                        EditorGUILayout.PropertyField(floor, new GUIContent("Made Of", "Concrete brick, or tropical wood logs."));
                        break;
                    case Kind.Ramp:
                        SizeFields("Width", "Rise", "Run");
                        if (!size.hasMultipleDifferentValues)
                        {
                            Vector3 s = size.vector3Value;
                            EditorGUILayout.LabelField("Slope", $"{Mathf.Atan2(s.y, s.z) * Mathf.Rad2Deg:0.#}°");
                        }
                        EditorGUILayout.PropertyField(floor, new GUIContent("Made Of", "A concrete slab, or tropical logs on trestles."));
                        break;
                    case Kind.HotPlate:
                        SizeFields("Width", null, "Length");
                        break;
                    case Kind.Water:
                        SizeFields("Width", "Depth", "Length");
                        break;
                    case Kind.Pool:
                        SizeFields("Width", "Depth", "Length");
                        EditorGUILayout.PropertyField(floor);
                        EditorGUILayout.PropertyField(poolSize, new GUIContent("Hole Size", poolSize.tooltip));
                        EditorGUILayout.PropertyField(poolOffset, new GUIContent("Hole Offset", poolOffset.tooltip));
                        EditorGUILayout.PropertyField(waterBelowTop, new GUIContent("Water Below Top", waterBelowTop.tooltip));
                        break;
                    case Kind.Grate:
                        SizeFields("Width", "Height", null);
                        break;
                    case Kind.FloorGrate:
                        SizeFields("Width", "Drain Depth", "Length");
                        EditorGUILayout.PropertyField(floor);
                        EditorGUILayout.PropertyField(poolSize, new GUIContent("Grate Size", poolSize.tooltip));
                        EditorGUILayout.PropertyField(poolOffset, new GUIContent("Grate Offset", poolOffset.tooltip));
                        break;
                    case Kind.PiercerPipe:
                        EditorGUILayout.PropertyField(bladeDistance);
                        EditorGUILayout.PropertyField(firstExit, new GUIContent("Ship Exit", firstExit.tooltip));
                        EditorGUILayout.PropertyField(secondExit, new GUIContent("Other Exit", secondExit.tooltip));
                        break;
                }
            }
            // Through OnValidate, this rebuilds the pieces that changed.
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild"))
                foreach (Object t in targets) LevelPieces.Rebuild((LevelPiece)t);
            EditorGUILayout.HelpBox("Everything under this object is rebuilt from these settings. Resize it with the " +
                                    "handles in the Scene view or the scale tool.", MessageType.None);
        }

        void SizeFields(string x, string y, string z)
        {
            if (x != null) EditorGUILayout.PropertyField(size.FindPropertyRelative("x"), new GUIContent(x));
            if (y != null) EditorGUILayout.PropertyField(size.FindPropertyRelative("y"), new GUIContent(y));
            if (z != null) EditorGUILayout.PropertyField(size.FindPropertyRelative("z"), new GUIContent(z));
        }

        void OnSceneGUI()
        {
            var piece = (LevelPiece)target;
            if (piece == null || EditorApplication.isPlaying) return;
            FoldScale(piece);

            Transform t = piece.transform;
            if (piece.PieceKind == Kind.PiercerPipe)
            {
                PipeHandles(piece);
                return;
            }

            Bounds bounds = LevelPieces.LocalBounds(piece);
            using (new Handles.DrawingScope(Matrix4x4.TRS(t.position, t.rotation, Vector3.one)))
            {
                box.axes = LevelPieces.SizeAxes(piece.PieceKind);
                box.center = bounds.center;
                box.size = bounds.size;
                EditorGUI.BeginChangeCheck();
                box.DrawHandle();
                if (EditorGUI.EndChangeCheck()) Resize(piece, bounds, new Bounds(box.center, box.size));
            }
        }

        /// <summary>
        /// Gives the piece the size of <paramref name="after"/> (dragged from <paramref name="before"/>), snapping the faces
        /// that moved to the grid and moving the pivot so the faces that didn't move stay where they are.
        /// </summary>
        static void Resize(LevelPiece piece, Bounds before, Bounds after)
        {
            Vector3 min = after.min, max = after.max;
            float smallest = LevelEditorSettings.Smallest;
            for (int a = 0; a < 3; a++)
            {
                if (!Mathf.Approximately(min[a], before.min[a]))
                    min[a] = Mathf.Min(LevelEditorSettings.Snap(min[a]), max[a] - smallest);
                if (!Mathf.Approximately(max[a], before.max[a]))
                    max[a] = Mathf.Max(LevelEditorSettings.Snap(max[a]), min[a] + smallest);
            }

            PrimitiveBoundsHandle.Axes axes = LevelPieces.SizeAxes(piece.PieceKind);
            Vector3 size = piece.Size;
            for (int a = 0; a < 3; a++)
                if (HasAxis(axes, a)) size[a] = max[a] - min[a];
            Vector3 shift = (min + max) * 0.5f - LevelPieces.LocalBounds(piece.PieceKind, size).center;
            for (int a = 0; a < 3; a++)
                if (!HasAxis(axes, a)) shift[a] = 0f;

            Transform t = piece.transform;
            Undo.RecordObjects(new Object[] { piece, t }, $"Resize {piece.name}");
            piece.Size = size;
            t.position += t.rotation * shift;
            LevelPieces.Refresh(piece);
        }

        /// <summary>Turns a scale left by the scale or rect tool into the piece's size once the drag is over.</summary>
        static void FoldScale(LevelPiece piece)
        {
            Transform t = piece.transform;
            if (t.localScale == Vector3.one || GUIUtility.hotControl != 0) return;

            PrimitiveBoundsHandle.Axes axes = LevelPieces.SizeAxes(piece.PieceKind);
            Vector3 size = piece.Size, scale = t.localScale;
            for (int a = 0; a < 3; a++)
                if (HasAxis(axes, a)) size[a] = Mathf.Max(0.05f, size[a] * Mathf.Abs(scale[a]));
            Undo.RecordObjects(new Object[] { piece, t }, $"Resize {piece.name}");
            piece.Size = size;
            t.localScale = Vector3.one;
            LevelPieces.Refresh(piece);
        }

        static bool HasAxis(PrimitiveBoundsHandle.Axes axes, int axis) => (axes & (PrimitiveBoundsHandle.Axes)(1 << axis)) != 0;

        /// <summary>Handles on the blade (along the pipe) and on both exits (on the floor).</summary>
        static void PipeHandles(LevelPiece piece)
        {
            Transform t = piece.transform;
            Handles.color = HandleColor;

            Vector3 blade = t.position + t.forward * piece.BladeDistance;
            Handles.Label(blade + Vector3.up * 1.8f, "Blade");
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(blade, t.forward, HandleUtility.GetHandleSize(blade) * 0.8f, Handles.ArrowHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(piece, "Move Pipe Blade");
                piece.BladeDistance = Mathf.Max(1f, LevelEditorSettings.Snap(Vector3.Dot(moved - t.position, t.forward)));
                LevelPieces.Refresh(piece);
            }

            ExitHandle(piece, true, "Ship exit");
            ExitHandle(piece, false, "Other exit");
        }

        static void ExitHandle(LevelPiece piece, bool first, string label)
        {
            Transform t = piece.transform;
            Vector3 world = t.position + t.rotation * (first ? piece.FirstExit : piece.SecondExit);
            Handles.Label(world + Vector3.up * 1.4f, label);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, t.rotation);
            if (!EditorGUI.EndChangeCheck()) return;

            Vector3 local = Quaternion.Inverse(t.rotation) * (moved - t.position);
            local = new Vector3(LevelEditorSettings.Snap(local.x), 0f, LevelEditorSettings.Snap(local.z));
            Undo.RecordObject(piece, "Move Pipe Exit");
            if (first) piece.FirstExit = local;
            else piece.SecondExit = local;
            LevelPieces.Refresh(piece);
        }
    }
}
