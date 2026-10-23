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

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsFolder)) return;

            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
        }
    }
}
