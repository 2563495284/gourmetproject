using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Runtime.Rendering
{
    /// <summary>
    /// 把渲染完的场景做 Dual-Kawase 模糊写入全局贴图 <c>_FrostedGlassTex</c>，
    /// 供 Overlay Canvas 上的毛玻璃 UI 着色器按屏幕坐标采样，实现 UI 背景毛玻璃。
    /// </summary>
    public sealed class FrostedGlassBlurRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material material;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
        [SerializeField, Range(1, 6)] private int iterations = 4;
        [SerializeField, Range(1, 8)] private int downsample = 2;
        [SerializeField, Range(0f, 4f)] private float blurRadius = 1f;

        private FrostedGlassBlurPass pass;

        /// <summary>
        /// 最近一帧生成的场景模糊贴图。Overlay Canvas 的 UI draw 拿不到 SRP 内设置的全局贴图，
        /// 需要 <see cref="UIFrostedGlass"/> 之类的组件在运行时把它绑到材质上。
        /// </summary>
        public static Texture ActiveBlurTexture { get; private set; }

        public override void Create()
        {
            pass = new FrostedGlassBlurPass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null || renderingData.cameraData.isPreviewCamera)
            {
                return;
            }

            pass.renderPassEvent = injectionPoint;
            pass.Setup(material, Mathf.Max(1, iterations), Mathf.Max(1, downsample), blurRadius);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
        }

        private sealed class FrostedGlassBlurPass : ScriptableRenderPass
        {
            private static readonly int BlurRadiusId = Shader.PropertyToID("_FrostedBlurRadius");
            private static readonly int FrostedGlassTexId = Shader.PropertyToID("_FrostedGlassTex");

            private Material material;
            private int iterations;
            private int downsample;
            private float blurRadius;
            private RTHandle blurTarget;

            public void Setup(Material passMaterial, int passIterations, int passDownsample, float passBlurRadius)
            {
                material = passMaterial;
                iterations = passIterations;
                downsample = passDownsample;
                blurRadius = passBlurRadius;
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

                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid())
                {
                    return;
                }

                TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
                int baseWidth = Mathf.Max(1, sourceDesc.width / downsample);
                int baseHeight = Mathf.Max(1, sourceDesc.height / downsample);

                EnsureTarget(baseWidth, baseHeight, sourceDesc.format);
                material.SetFloat(BlurRadiusId, blurRadius);

                var mips = new TextureHandle[iterations];
                TextureHandle last = source;
                int width = baseWidth;
                int height = baseHeight;

                for (int i = 0; i < iterations; i++)
                {
                    TextureDesc desc = sourceDesc;
                    desc.width = Mathf.Max(1, width);
                    desc.height = Mathf.Max(1, height);
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = MSAASamples.None;
                    desc.clearBuffer = false;
                    desc.name = "FrostedGlassBlurDown" + i;

                    mips[i] = renderGraph.CreateTexture(desc);
                    // Pass 0 reads the RTHandle-scaled camera color; later levels read exact-size textures (pass 1).
                    int downPass = i == 0 ? 0 : 1;
                    var down = new RenderGraphUtils.BlitMaterialParameters(last, mips[i], material, downPass);
                    renderGraph.AddBlitPass(down, "Frosted Glass Downsample " + i);

                    last = mips[i];
                    width = Mathf.Max(1, width / 2);
                    height = Mathf.Max(1, height / 2);
                }

                for (int i = iterations - 2; i >= 0; i--)
                {
                    var up = new RenderGraphUtils.BlitMaterialParameters(last, mips[i], material, 2);
                    renderGraph.AddBlitPass(up, "Frosted Glass Upsample " + i);
                    last = mips[i];
                }

                TextureHandle target = renderGraph.ImportTexture(blurTarget);
                var final = new RenderGraphUtils.BlitMaterialParameters(last, target, material, 2);
                renderGraph.AddBlitPass(final, "Frosted Glass Composite");

                // 全局贴图供 Screen Space - Camera 的 Canvas 采样；Overlay Canvas 需要组件手动绑定。
                Shader.SetGlobalTexture(FrostedGlassTexId, blurTarget);
                ActiveBlurTexture = blurTarget;
            }

            private void EnsureTarget(int width, int height, GraphicsFormat format)
            {
                if (blurTarget != null && blurTarget.rt != null &&
                    blurTarget.rt.width == width && blurTarget.rt.height == height)
                {
                    return;
                }

                blurTarget?.Release();
                blurTarget = RTHandles.Alloc(
                    width,
                    height,
                    colorFormat: format,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Clamp,
                    name: "_FrostedGlassTex");
            }

            public void Dispose()
            {
                blurTarget?.Release();
                blurTarget = null;
            }
        }
    }
}
