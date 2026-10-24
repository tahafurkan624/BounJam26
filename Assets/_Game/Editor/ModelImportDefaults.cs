using UnityEditor;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Import settings for the game's own models: geometry only. Materials are assigned in prefabs,
    /// and nothing here is animated or carries cameras and lights.
    /// </summary>
    class ModelImportDefaults : AssetPostprocessor
    {
        const string ModelsFolder = "Assets/_Game/Art/Models/";
        // Static meshes exported from ArtSource/Blender; rigged SK_ models keep their own settings.
        const string ArtStaticMeshes = "Assets/_Project/Art/Models/SM_";
        // BlobSkin reads its rest shape's vertices at runtime.
        const string BlobRestShape = "Assets/_Project/Art/Models/SM_BlobBase.fbx";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsFolder) && !assetPath.StartsWith(ArtStaticMeshes)) return;

            var importer = (ModelImporter)assetImporter;
            if (assetPath == BlobRestShape) importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
        }
    }
}
