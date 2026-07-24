using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Runtime.Rendering
{
    public sealed class GrotesqueCartoonRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material material;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
        [SerializeField, Range(0f, 2f)] private float saturation = 1.18f;
        [SerializeField, Range(0f, 2f)] private float contrast = 1.08f;
        [SerializeField, Range(2f, 64f)] private float posterizeSteps = 18f;
        [SerializeField, Range(0f, 1f)] private float inkStrength = 0.28f;
        [SerializeField, Range(0f, 1f)] private float vignetteStrength = 0.18f;
        [SerializeField] private Color warmTint = new(1.05f, 0.96f, 0.86f, 1f);

        private GrotesqueCartoonPass pass;

        public override void Create()
        {
            pass = new GrotesqueCartoonPass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null
                || renderingData.cameraData.isPreviewCamera
                || IsDishIconPreviewCamera(renderingData.cameraData.camera))
            {
                return;
            }

            pass.Setup(material, saturation, contrast, posterizeSteps, inkStrength, vignetteStrength, warmTint);
            renderer.EnqueuePass(pass);
        }

        private static bool IsDishIconPreviewCamera(Camera camera)
        {
            int layer = LayerMask.NameToLayer("DishIconPreview");
            return camera != null
                && layer >= 0
                && camera.cullingMask == 1 << layer;
        }

        protected override void Dispose(bool disposing)
        {
            pass = null;
        }

        private sealed class GrotesqueCartoonPass : ScriptableRenderPass
        {
            private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
            private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
            private static readonly int PosterizeStepsId = Shader.PropertyToID("_PosterizeSteps");
            private static readonly int InkStrengthId = Shader.PropertyToID("_InkStrength");
            private static readonly int VignetteStrengthId = Shader.PropertyToID("_VignetteStrength");
            private static readonly int WarmTintId = Shader.PropertyToID("_WarmTint");

            private Material material;
            private float saturation;
            private float contrast;
            private float posterizeSteps;
            private float inkStrength;
            private float vignetteStrength;
            private Color warmTint;

            public void Setup(Material passMaterial, float passSaturation, float passContrast, float passPosterizeSteps, float passInkStrength, float passVignetteStrength, Color passWarmTint)
            {
                material = passMaterial;
                saturation = passSaturation;
                contrast = passContrast;
                posterizeSteps = passPosterizeSteps;
                inkStrength = passInkStrength;
                vignetteStrength = passVignetteStrength;
                warmTint = passWarmTint;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    return;
                }

                material.SetFloat(SaturationId, saturation);
                material.SetFloat(ContrastId, contrast);
                material.SetFloat(PosterizeStepsId, posterizeSteps);
                material.SetFloat(InkStrengthId, inkStrength);
                material.SetFloat(VignetteStrengthId, vignetteStrength);
                material.SetColor(WarmTintId, warmTint);

                TextureHandle source = resources.activeColorTexture;
                TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = "GrotesqueCartoonPostProcess";
                destinationDesc.clearBuffer = false;

                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
                var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
                renderGraph.AddBlitPass(parameters, "Grotesque Cartoon Post Process");
                resources.cameraColor = destination;
            }
        }
    }
}
