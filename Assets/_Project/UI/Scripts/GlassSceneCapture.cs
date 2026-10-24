using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BounJam
{
    /// <summary>
    /// Copies the main camera's finished frame (after post-processing, before the overlay UI) into the global
    /// _GlassSceneTexture, which the Liquid Glass shader blurs. The pass is queued from script on every main-camera
    /// render, so no renderer asset needs a renderer feature; the UI itself stays out of post-processing.
    /// </summary>
    public class GlassSceneCapture : ScriptableRenderPass
    {
        static readonly int TextureId = Shader.PropertyToID("_GlassSceneTexture");
        static readonly int ScreenId = Shader.PropertyToID("_GlassScreenParams");

        static GlassSceneCapture instance;
        static int users;

        RTHandle copy;

        GlassSceneCapture()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            users = 0;
        }

        /// <summary>Starts capturing; every call needs a matching <see cref="Release"/>.</summary>
        public static void Acquire()
        {
            if (users++ > 0) return;
            instance ??= new GlassSceneCapture();
            RenderPipelineManager.beginCameraRendering += Enqueue;
        }

        public static void Release()
        {
            if (users == 0 || --users > 0) return;
            RenderPipelineManager.beginCameraRendering -= Enqueue;
        }

        static void Enqueue(ScriptableRenderContext _, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera")) return;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(instance);
            Shader.SetGlobalVector(ScreenId, new Vector4(Screen.width, Screen.height, 1f / Screen.width, 1f / Screen.height));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            RenderTextureDescriptor descriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            if (RenderingUtils.ReAllocateHandleIfNeeded(ref copy, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "_GlassSceneTexture"))
                Shader.SetGlobalTexture(TextureId, copy.rt);

            TextureHandle destination = renderGraph.ImportTexture(copy);
            renderGraph.AddBlitPass(resources.activeColorTexture, destination, Vector2.one, Vector2.zero, passName: "Glass UI Scene Copy");
        }
    }
}
