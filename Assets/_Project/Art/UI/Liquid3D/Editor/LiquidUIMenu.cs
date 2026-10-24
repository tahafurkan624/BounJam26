// BlobShip - editor helpers for the 3D liquid UI.
//   Tools > BlobShip > Liquid UI > Set Up URP          opaque texture + HDR + post processing on the canvas camera + a Bloom volume
//   GameObject > BlobShip > Liquid 3D Pause Menu       builds the whole menu inside the selected Canvas
//   GameObject > BlobShip > Liquid 3D Button           one turquoise liquid button
//   Tools > BlobShip > Liquid UI > Make Selected Liquid   turns your existing buttons into 3D liquid ones
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace BlobShip.UI.Liquid3D.EditorTools
{
    public static class LiquidUIMenu
    {
        const string ProfilePath = "Assets/BlobShip/UI/Liquid3D/LiquidUI_Bloom.asset";

        // ------------------------------------------------------------------ URP
        [MenuItem("Tools/BlobShip/Liquid UI/Set Up URP")]
        public static void SetUpUrp()
        {
            int assets = 0;
            assets += FixAsset(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++) assets += FixAsset(QualitySettings.GetRenderPipelineAssetAt(i));

            int cams = 0;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == null) continue;
                var data = canvas.worldCamera.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Liquid UI URP setup");
                data.renderPostProcessing = true;
                data.requiresColorOption = CameraOverrideOption.On;
                EditorUtility.SetDirty(data);
                cams++;
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.0f);       // only HDR: LED text and bright reflections
                bloom.intensity.Override(0.7f);
                bloom.scatter.Override(0.6f);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            bool hasVolume = false;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) if (v.sharedProfile == profile) hasVolume = true;
            if (!hasVolume)
            {
                var go = new GameObject("Liquid UI Bloom");
                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
                Undo.RegisterCreatedObjectUndo(go, "Liquid UI Bloom");
            }
            EditorUtility.DisplayDialog("Liquid UI",
                $"URP asset(s) updated: {assets} (Opaque Texture + HDR)\nCanvas cameras updated: {cams} (Post Processing)\n" +
                "Bloom volume: Liquid UI Bloom (threshold 1, only bright parts glow).\n\n" +
                "If your canvas camera is an Overlay camera in a stack, turn Post Processing on for the last camera of the stack.", "OK");
        }

        static int FixAsset(RenderPipelineAsset rp)
        {
            var u = rp as UniversalRenderPipelineAsset;
            if (u == null) return 0;
            Undo.RecordObject(u, "Liquid UI URP setup");
            u.supportsCameraOpaqueTexture = true;
            u.supportsHDR = true;
            EditorUtility.SetDirty(u);
            return 1;
        }

        // ------------------------------------------------------------------ creation helpers
        static Canvas TargetCanvas()
        {
            var sel = Selection.activeGameObject;
            Canvas c = sel != null ? sel.GetComponentInParent<Canvas>() : null;
            if (c == null) c = Object.FindFirstObjectByType<Canvas>();
            if (c == null) EditorUtility.DisplayDialog("Liquid UI", "Önce sahnede bir Canvas olmalı (Screen Space - Camera).", "Tamam");
            return c;
        }

        static RectTransform NewUI(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            return rt;
        }

        static TextMeshProUGUI NewLabel(string text, Transform parent, float size, Vector2 pos, Vector2 box, float alpha = 1f, bool led = true)
        {
            var rt = NewUI(text.Length > 0 ? "Label " + text : "Label", parent, box, pos);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold; t.color = new Color(1f, 1f, 1f, alpha); t.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            var c = rt.gameObject.AddComponent<Canvas>(); c.overrideSorting = true; c.sortingOrder = 3;
            if (led) rt.gameObject.AddComponent<LiquidLEDLabel>();
            return t;
        }

        static LiquidSlab NewButton(string text, Transform parent, Vector2 pos, int index)
        {
            var rt = NewUI(text, parent, new Vector2(450, 100), pos);
            rt.gameObject.AddComponent<LiquidRaycastTarget>();
            var slab = rt.gameObject.AddComponent<LiquidSlab>();
            slab.colors = LiquidPalette.ForIndex(index);
            slab.sortingOffset = 2;
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            NewLabel(text, rt, 34, Vector2.zero, new Vector2(430, 90));
            slab.FindLabels();
            return slab;
        }

        [MenuItem("GameObject/BlobShip/Liquid 3D Pause Menu", false, 10)]
        static void CreatePauseMenu(MenuCommand cmd)
        {
            var canvas = TargetCanvas(); if (canvas == null) return;
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
                Debug.LogWarning("Liquid UI: the canvas should be Screen Space - Camera so the 3D buttons are drawn by its camera.");

            var root = NewUI("Liquid Pause Menu", canvas.transform, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
            root.gameObject.AddComponent<LiquidViewTilt>();

            // the glass panel
            var panelRt = NewUI("Panel", root, new Vector2(560, 595), new Vector2(0, 60));
            panelRt.gameObject.AddComponent<LiquidRaycastTarget>();
            var panel = panelRt.gameObject.AddComponent<LiquidSlab>();
            panel.shape = LiquidSlab.SlabShape.Rounded; panel.cornerRadius = 62; panel.edgeRadius = 20; panel.thickness = 20;
            panel.glass = true; panel.frost = 0.82f; panel.refraction = 0.012f; panel.iridescence = 0.55f;
            panel.glassMilk = LiquidPalette.GlassMilk; panel.colors = new LiquidColors(LiquidPalette.GlassRim, LiquidPalette.GlassRim, Vector3.zero);
            panel.simRows = 170; panel.sortingOffset = 1; panel.alive = false;

            NewLabel("Duraklatıldı", panelRt, 46, new Vector2(0, 241), new Vector2(520, 64));
            NewLabel("Bölüm 1 · Kağıt İskele · 3 / 5 damla", panelRt, 18, new Vector2(0, 199), new Vector2(520, 30), 0.6f);

            var cont = NewButton("Devam et", panelRt, new Vector2(0, 63), 0);
            NewButton("Yeniden başlat", panelRt, new Vector2(0, -62), 1);
            NewButton("Ana menü", panelRt, new Vector2(0, -187), 2);
            panel.FindLabels();
            var p3d = panelRt.gameObject.AddComponent<LiquidPanel3D>();
            UnityEventTools.AddPersistentListener(cont.GetComponent<Button>().onClick, p3d.HideMenu);

            // the Sıvı / Katı switch under the panel
            var togRt = NewUI("Sıvı-Katı", root, new Vector2(400, 110), new Vector2(0, -367));
            togRt.gameObject.AddComponent<LiquidRaycastTarget>();
            var tog = togRt.gameObject.AddComponent<LiquidSlab>();
            tog.colors = LiquidPalette.Turquoise; tog.glassMilk = new Color(0.035f, 0.06f, 0.08f, 0f); tog.refraction = 0.026f; tog.iridescence = 0.4f;
            tog.sortingOffset = 2;
            var lt = togRt.gameObject.AddComponent<LiquidToggle>();
            lt.liquidLabel = NewLabel("Sıvı", togRt, 32, new Vector2(-92, 0), new Vector2(180, 80));
            lt.solidLabel = NewLabel("Katı", togRt, 32, new Vector2(92, 0), new Vector2(180, 80));
            tog.FindLabels();

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Liquid 3D Pause Menu");
            Selection.activeGameObject = root.gameObject;
        }

        [MenuItem("GameObject/BlobShip/Liquid 3D Button", false, 11)]
        static void CreateButton(MenuCommand cmd)
        {
            var canvas = TargetCanvas(); if (canvas == null) return;
            var parent = Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<RectTransform>() != null ? Selection.activeGameObject.transform : canvas.transform;
            var b = NewButton("Devam et", parent, Vector2.zero, 0);
            Undo.RegisterCreatedObjectUndo(b.gameObject, "Liquid 3D Button");
            Selection.activeGameObject = b.gameObject;
        }

        [MenuItem("Tools/BlobShip/Liquid UI/Make Selected Liquid")]
        static void MakeSelectedLiquid()
        {
            int i = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<RectTransform>() == null || go.GetComponent<LiquidSlab>() != null) continue;
                var slab = Undo.AddComponent<LiquidSlab>(go);
                slab.colors = LiquidPalette.ForIndex(i++);
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.GetComponent<Canvas>() == null) { var c = Undo.AddComponent<Canvas>(t.gameObject); c.overrideSorting = true; c.sortingOrder = 3; }
                    if (t.GetComponent<LiquidLEDLabel>() == null) Undo.AddComponent<LiquidLEDLabel>(t.gameObject);
                    t.raycastTarget = false;
                }
                slab.FindLabels();
            }
        }

        [MenuItem("Tools/BlobShip/Liquid UI/Make Selected Liquid", true)]
        static bool MakeSelectedLiquidValid() => Selection.gameObjects.Length > 0;
    }
}
