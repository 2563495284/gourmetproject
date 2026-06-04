using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 战争迷雾式屏幕遮罩：圆内全透明；圆外缘软渐变进黑雾。
    /// 挂在 Gameplay 相机上；<see cref="Sync"/> 须在相机跟随之后由 <see cref="GameWorld"/> 调用。
    /// </summary>
    public sealed class VisionFogOverlay : MonoBehaviour
    {
        private static readonly int PropFogColor = Shader.PropertyToID("_FogColor");
        private static readonly int PropCenter = Shader.PropertyToID("_Center");
        private static readonly int PropInnerRadius = Shader.PropertyToID("_InnerRadius");
        private static readonly int PropOuterRadius = Shader.PropertyToID("_OuterRadius");
        private static readonly int PropAspect = Shader.PropertyToID("_Aspect");

        private Camera _cam;
        private Canvas _canvas;
        private Material _material;

        public void Build(Camera cam)
        {
            _cam = cam;
            Shader shader = Shader.Find("GourmetProject/VisionFogOverlay");
            if (shader == null)
            {
                Debug.LogError("[VisionFogOverlay] 未找到 GourmetProject/VisionFogOverlay.shader");
                enabled = false;
                return;
            }

            _material = new Material(shader);
            _material.SetColor(PropFogColor, GameConst.VisionFogColor);

            var canvasGo = new GameObject("VisionFogCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam.nearClipPlane + 0.15f;
            _canvas.sortingOrder = 90;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var imageGo = new GameObject("FogMask");
            imageGo.transform.SetParent(canvasGo.transform, false);
            var image = imageGo.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = Texture2D.whiteTexture;
            image.material = _material;

            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>更新遮罩圆心与半径；<paramref name="revealAll"/> 时关闭遮罩。</summary>
        public void Sync(Vector2 worldCenter, bool revealAll, float visionOcclusion, EnergySystem energy)
        {
            if (_material == null || _canvas == null || _cam == null) return;

            _canvas.enabled = !revealAll;
            if (revealAll || energy == null) return;

            float occMul = 1f - visionOcclusion * 0.8f;
            float worldRadius = (energy.LighterOn ? energy.LighterRadius : GameConst.DefaultVisionRadius) * occMul;

            Vector3 centerScreen = _cam.WorldToScreenPoint(new Vector3(worldCenter.x, worldCenter.y, 0f));
            Vector3 edgeScreen = _cam.WorldToScreenPoint(new Vector3(worldCenter.x + worldRadius, worldCenter.y, 0f));

            float screenRadius = Vector2.Distance(
                new Vector2(centerScreen.x, centerScreen.y),
                new Vector2(edgeScreen.x, edgeScreen.y));

            float normCenterX = centerScreen.x / Screen.width;
            float normCenterY = centerScreen.y / Screen.height;
            float normRadius = screenRadius / Screen.height;
            float normSoft = normRadius * GameConst.VisionFogSoftEdgeFraction;

            _material.SetVector(PropCenter, new Vector4(normCenterX, normCenterY, 0f, 0f));
            _material.SetFloat(PropInnerRadius, normRadius);
            _material.SetFloat(PropOuterRadius, normRadius + normSoft);
            _material.SetFloat(PropAspect, (float)Screen.width / Screen.height);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
