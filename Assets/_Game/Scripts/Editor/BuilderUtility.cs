using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>Shared helpers for the editor scripts that generate prefabs and scenes.</summary>
    static class BuilderUtility
    {
        const string MaterialFolder = "Assets/_Game/Art/Materials";

        /// <summary>Loads a flat-coloured URP Lit material, creating it the first time it is asked for.</summary>
        public static Material FlatMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Assigns a private [SerializeField] reference the way the Inspector would.</summary>
        public static void SetField(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        /// <summary>A primitive mesh without its collider, for decoration.</summary>
        public static GameObject VisualPrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
