using GourmetProject.Runtime.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// 毛玻璃 UI 绑定器：把 <see cref="FrostedGlassBlurRenderFeature"/> 每帧产出的场景模糊贴图
    /// 绑到本 Graphic 的材质上。挂在使用 GourmetProject/UIFrostedGlass 材质的 Image/RawImage 上即可。
    /// （Screen Space - Overlay 的 UI draw 拿不到 SRP 内 Shader.SetGlobalTexture 设置的全局贴图，故需手动绑定。）
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UIFrostedGlass : MonoBehaviour
    {
        private static readonly int FrostedGlassTexId = Shader.PropertyToID("_FrostedGlassTex");

        private Graphic _graphic;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
        }

        private void OnEnable()
        {
            Bind();
        }

        private void LateUpdate()
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

            Texture blur = FrostedGlassBlurRenderFeature.ActiveBlurTexture;
            Material material = _graphic.material;
            if (blur == null || material == null)
            {
                return;
            }

            material.SetTexture(FrostedGlassTexId, blur);
        }
    }
}
