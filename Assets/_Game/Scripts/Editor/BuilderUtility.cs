using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
        public static void SetField(Object target, string field, Object value) => SetValues(target, (field, value));

        /// <summary>
        /// Sets private [SerializeField] values the way the Inspector would: numbers, bools, strings, colours,
        /// enums, object references and arrays of object references.
        /// </summary>
        public static void SetValues(Object target, params (string field, object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, object value) in values)
            {
                SerializedProperty property = serialized.FindProperty(field)
                    ?? throw new ArgumentException($"{target.GetType().Name} has no serialized field '{field}'.");
                switch (value)
                {
                    case null: property.objectReferenceValue = null; break;
                    case float number: property.floatValue = number; break;
                    case int number: property.intValue = number; break;
                    case bool flag: property.boolValue = flag; break;
                    case string text: property.stringValue = text; break;
                    case Color color: property.colorValue = color; break;
                    case Enum choice: property.intValue = Convert.ToInt32(choice); break;
                    case Object reference: property.objectReferenceValue = reference; break;
                    case Object[] references:
                        property.arraySize = references.Length;
                        for (int i = 0; i < references.Length; i++)
                            property.GetArrayElementAtIndex(i).objectReferenceValue = references[i];
                        break;
                    default:
                        throw new ArgumentException($"Can't assign a {value.GetType().Name} to '{field}'.");
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        public static RectTransform UiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Fills the parent, leaving <paramref name="inset"/> pixels on every side.</summary>
        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image UiImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TMP_Text UiLabel(string name, Transform parent, string text, float size, TextAlignmentOptions alignment)
        {
            var label = UiObject(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
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
