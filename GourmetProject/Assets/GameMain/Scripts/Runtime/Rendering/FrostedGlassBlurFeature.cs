using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GourmetProject.Runtime.Rendering
{
    /// <summary>
    /// 毛玻璃背景模糊（照抄参考项目 DOOM 式 CommandBufferBlur，移植到 URP 17 RenderGraph）。
    /// 抓取相机「最终渲染结果」，做 4 级独立可分离高斯模糊，产出 <c>_GrabBlurTexture_0..3</c>
    /// （0 最清晰、3 最模糊），供 UI 毛玻璃着色器按屏幕坐标在各级间用灰度 mask 混合采样。
    ///
    /// 与旧 <c>WorldBlurCapture</c>（副相机重渲染）不同：这里直接复用主相机已渲染画面，
    /// 画面 100% 一致、且不会多渲染一遍场景。
    /// </summary>
    public sealed class FrostedGlassBlurFeature : ScriptableRendererFeature
    {
        public const int BlurLevelCount = 4;

        private static readonly int[] GrabBlurTextureIds =
        {
            Shader.PropertyToID("_GrabBlurTexture_0"),
            Shader.PropertyToID("_GrabBlurTexture_1"),
            Shader.PropertyToID("_GrabBlurTexture_2"),
            Shader.PropertyToID("_GrabBlurTexture_3"),
        };

        /// <summary>4 级模糊贴图，0 最清晰、3 最模糊。供 Overlay UI 组件手动绑到材质。</summary>
        public static Texture[] BlurTextures { get; private set; }

        [SerializeField] private Material blurMaterial;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
        [SerializeField, Range(1, 8)] private int downsample = 1;

        private FrostedGlassBlurPass _pass;

        public override void Create()
        {
            _pass = new FrostedGlassBlurPass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (blurMaterial == null)
            {
                return;
            }

            if (renderingData.cameraData.cameraType != CameraType.Game)
            {
                return;
            }

            _pass.Setup(blurMaterial, downsample);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
            BlurTextures = null;
        }

        private sealed class FrostedGlassBlurPass : ScriptableRenderPass
        {
            private Material _material;
            private int _downsample;
            private readonly RTHandle[] _levels = new RTHandle[BlurLevelCount];
            private readonly Texture[] _exposed = new Texture[BlurLevelCount];

            public void Setup(Material material, int downsampleFactor)
            {
                _material = material;
                _downsample = Mathf.Max(1, downsampleFactor);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (resources.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid())
                {
                    return;
                }

                RenderTextureDescriptor colorDesc = cameraData.cameraTargetDescriptor;
                colorDesc.depthBufferBits = 0;
                colorDesc.msaaSamples = 1;

                int baseWidth = Mathf.Max(1, colorDesc.width / _downsample);
                int baseHeight = Mathf.Max(1, colorDesc.height / _downsample);

                TextureDesc transientDesc = renderGraph.GetTextureDesc(source);
                transientDesc.msaaSamples = MSAASamples.None;
                transientDesc.clearBuffer = false;
                transientDesc.filterMode = FilterMode.Bilinear;
                transientDesc.wrapMode = TextureWrapMode.Clamp;

                bool exposedDirty = false;

                for (int i = 0; i < BlurLevelCount; i++)
                {
                    int levelWidth = Mathf.Max(1, baseWidth >> i);
                    int levelHeight = Mathf.Max(1, baseHeight >> i);

                    RenderTextureDescriptor levelDesc = colorDesc;
                    levelDesc.width = levelWidth;
                    levelDesc.height = levelHeight;

                    RenderingUtils.ReAllocateHandleIfNeeded(
                        ref _levels[i], levelDesc,
                        FilterMode.Bilinear, TextureWrapMode.Clamp,
                        name: "_GrabBlurTexture_" + i);

                    if (_exposed[i] != _levels[i])
                    {
                        _exposed[i] = _levels[i];
                        exposedDirty = true;
                    }

                    TextureHandle levelHandle = renderGraph.ImportTexture(_levels[i]);

                    TextureDesc downDesc = transientDesc;
                    downDesc.width = levelWidth;
                    downDesc.height = levelHeight;
                    downDesc.name = "FrostGlassDownsample" + i;
                    TextureHandle downHandle = renderGraph.CreateTexture(downDesc);

                    TextureDesc tempDesc = transientDesc;
                    tempDesc.width = levelWidth;
                    tempDesc.height = levelHeight;
                    tempDesc.name = "FrostGlassBlurTemp" + i;
                    TextureHandle tempHandle = renderGraph.CreateTexture(tempDesc);

                    // 从全屏画面降采样到该级尺寸（bilinear），使随后的模糊在该级分辨率上进行，忠实参考实现。
                    renderGraph.AddBlitPass(source, downHandle, Vector2.one, Vector2.zero,
                        passName: "FrostGlass Downsample " + i);

                    // 水平（Pass 0）→ 垂直（Pass 1）可分离高斯。
                    renderGraph.AddBlitPass(
                        new RenderGraphUtils.BlitMaterialParameters(downHandle, tempHandle, _material, 0),
                        "FrostGlass Blur H " + i);
                    renderGraph.AddBlitPass(
                        new RenderGraphUtils.BlitMaterialParameters(tempHandle, levelHandle, _material, 1),
                        "FrostGlass Blur V " + i);

                    // RTHandle 不受 RenderGraph 帧回收影响，可作为全局纹理长期绑定（Overlay UI 另走手动绑定）。
                    Shader.SetGlobalTexture(GrabBlurTextureIds[i], _levels[i]);
                }

                if (exposedDirty || BlurTextures == null)
                {
                    BlurTextures = _exposed;
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < BlurLevelCount; i++)
                {
                    _levels[i]?.Release();
                    _levels[i] = null;
                    _exposed[i] = null;
                }
            }
        }
    }
}
