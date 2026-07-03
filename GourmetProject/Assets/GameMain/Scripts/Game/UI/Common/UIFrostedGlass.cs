using GourmetProject.Runtime.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 毛玻璃 UI 绑定器：把 <see cref="WorldBlurCapture"/> 每帧产出的四级世界模糊贴图
    /// 绑到本 Graphic 实际用于绘制的材质上。挂在使用 GourmetProject/UIFrostedGlass 材质的 Image/RawImage 上即可。
    /// （Screen Space - Overlay 的 UI draw 拿不到 Shader.SetGlobalTexture 设置的全局贴图，故需手动绑定。）
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class UIFrostedGlass : MonoBehaviour
    {
        private static readonly int[] GrabBlurTextureIds =
        {
            Shader.PropertyToID("_GrabBlurTexture_0"),
            Shader.PropertyToID("_GrabBlurTexture_1"),
            Shader.PropertyToID("_GrabBlurTexture_2"),
            Shader.PropertyToID("_GrabBlurTexture_3"),
        };

        private Graphic _graphic;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            Bind();
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
        }

        private void OnWillRenderCanvases()
        {
            Bind();
        }

        private void Bind()
        {
            if (_graphic == null)
            {
                _graphic = GetComponent<Graphic>();
                if (_graphic == null)
                {
                    return;
                }
            }

            Texture[] blurLevels = FrostedGlassBlurFeature.BlurTextures;
            if (blurLevels == null || blurLevels.Length < GrabBlurTextureIds.Length)
            {
                return;
            }

            // 必须写到 materialForRendering：Canvas 实际绘制用的实例，.material 可能是另一份拷贝。
            Material renderMaterial = _graphic.materialForRendering;
            if (renderMaterial == null)
            {
                return;
            }

            bool hasAny = false;
            for (int i = 0; i < GrabBlurTextureIds.Length; i++)
            {
                if (blurLevels[i] == null)
                {
                    continue;
                }

                renderMaterial.SetTexture(GrabBlurTextureIds[i], blurLevels[i]);
                hasAny = true;
            }

            if (hasAny)
            {
                _graphic.SetMaterialDirty();
            }
        }
    }
}
