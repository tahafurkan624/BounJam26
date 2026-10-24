using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BounJam.Editor
{
    /// <summary>
    /// Wires the baked concrete tiles up for URP on import.
    ///
    /// Textures in Assets/_Project/Art/Textures/Tiles (T_Tile_&lt;Variant&gt;_*.png, baked by
    /// ArtSource/Blender/scripts/tiles_bake.py) get the right import type: _Normal as a normal map,
    /// _Roughness and _MetallicSmoothness as linear data (alpha kept as smoothness).
    ///
    /// Models named SM_Tile_&lt;Variant&gt;.fbx get their embedded material turned into URP Lit with
    /// _BaseMap, _BumpMap and _MetallicGlossMap (metallic workflow, smoothness from the map's alpha).
    /// </summary>
    public class TileImportPostprocessor : AssetPostprocessor
    {
        const string TextureFolder = "Assets/_Project/Art/Textures/Tiles/";
        const string ModelPrefix = "SM_Tile_";
        const string LitShader = "Universal Render Pipeline/Lit";

        // Bump this when the rules below change so Unity reimports the affected assets.
        public override uint GetVersion() => 2;

        // Run after URP's own FBX material description preprocessor so our setup wins.
        public override int GetPostprocessOrder() => 1000;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (name.EndsWith("_Normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
            else if (name.EndsWith("_Roughness") || name.EndsWith("_MetallicSmoothness"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }
            else if (name.EndsWith("_Albedo"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }
        }

        void OnPreprocessModel()
        {
            if (TileVariant(assetPath) == null)
                return;
            // The project imports models with materials off by default; tiles need their embedded material.
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material,
            AnimationClip[] materialAnimation)
        {
            string variant = TileVariant(assetPath);
            if (variant == null)
                return;

            var shader = Shader.Find(LitShader);
            if (shader == null)
            {
                Debug.LogWarning($"[Tiles] '{LitShader}' not found; is URP installed? Skipping {assetPath}");
                return;
            }

            material.shader = shader;
            material.SetFloat("_WorkflowMode", 1f);            // metallic
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 1f);             // scales the map's alpha
            material.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map alpha
            material.SetFloat("_BumpScale", 1f);

            var albedo = LoadTexture(variant, "Albedo");
            var normal = LoadTexture(variant, "Normal");
            var metallicSmoothness = LoadTexture(variant, "MetallicSmoothness");

            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_MainTex", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            SetKeyword(material, "_NORMALMAP", normal != null);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicSmoothness != null);

            if (albedo == null || normal == null || metallicSmoothness == null)
                Debug.LogWarning($"[Tiles] Missing baked textures for '{variant}' in {TextureFolder}");
        }

        /// <summary>"Assets/.../SM_Tile_Warm.fbx" -> "Warm", anything else -> null.</summary>
        static string TileVariant(string path)
        {
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                return null;
            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith(ModelPrefix) ? name.Substring(ModelPrefix.Length) : null;
        }

        Texture2D LoadTexture(string variant, string map)
        {
            string path = $"{TextureFolder}T_Tile_{variant}_{map}.png";
            // Registers the dependency: the texture is imported first, and the model reimports when it changes.
            context.DependsOnArtifact(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        [MenuItem("Tools/BounJam/Reimport Tiles")]
        static void ReimportTiles()
        {
            // Plain file listing: FindAssets' name search tokenises on '_' and misses "SM_Tile_" prefixes.
            foreach (string file in Directory.GetFiles(TextureFolder, "T_Tile_*.png"))
                AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            foreach (string file in Directory.GetFiles("Assets/_Project/Art/Models", ModelPrefix + "*.fbx"))
                AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        }
    }
}
