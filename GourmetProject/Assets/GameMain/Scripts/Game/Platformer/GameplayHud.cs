using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Platformer
{
    /// <summary>
    /// 对局 HUD（设计文档第九节 / 资源清单 3.5）：左下能量条、右下进度百分比、
    /// 中央死亡/胜利蒙版、右上调试坐标。代码构建屏幕空间 Overlay Canvas。
    /// </summary>
    public sealed class GameplayHud : MonoBehaviour
    {
        private GameWorld _world;
        private Image _energyFill;
        private Text _progressText;
        private Text _debugText;
        private Image _deathOverlay;
        private Image _victoryOverlay;
        private Font _font;

        public void Init(GameWorld world)
        {
            _world = world;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildCanvas();
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("GameplayHudCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 600f);
            canvasGo.AddComponent<GraphicRaycaster>();

            // —— 能量条（左下）——
            Image bg = CreateImage("EnergyBg", canvasGo.transform, Art.Load("Sprites/UI/energy_bar_bg"));
            SetAnchored(bg.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(200f, 24f), new Vector2(0f, 0f));

            _energyFill = CreateImage("EnergyFill", bg.transform, Art.Load("Sprites/UI/energy_bar_fill"));
            Stretch(_energyFill.rectTransform);
            _energyFill.type = Image.Type.Filled;
            _energyFill.fillMethod = Image.FillMethod.Horizontal;
            _energyFill.fillOrigin = 0;
            _energyFill.fillAmount = 1f;

            // —— 进度（右下）——
            _progressText = CreateText("Progress", canvasGo.transform, "0%", 22, TextAnchor.LowerRight);
            SetAnchored(_progressText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 24f), new Vector2(160f, 30f), new Vector2(1f, 0f));

            // —— 调试坐标（右上）——
            _debugText = CreateText("Debug", canvasGo.transform, string.Empty, 14, TextAnchor.UpperRight);
            SetAnchored(_debugText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(320f, 80f), new Vector2(1f, 1f));
            _debugText.color = new Color(1f, 1f, 1f, 0.5f);

            // —— 死亡 / 胜利蒙版（全屏）——
            _deathOverlay = CreateImage("DeathOverlay", canvasGo.transform, Art.Load("Sprites/UI/death_overlay"));
            Stretch(_deathOverlay.rectTransform);
            _deathOverlay.raycastTarget = false;
            SetImageAlpha(_deathOverlay, 0f);

            _victoryOverlay = CreateImage("VictoryOverlay", canvasGo.transform, Art.Load("Sprites/UI/victory_overlay"));
            Stretch(_victoryOverlay.rectTransform);
            _victoryOverlay.raycastTarget = false;
            SetImageAlpha(_victoryOverlay, 0f);
        }

        private void Update()
        {
            if (_world == null) return;

            _energyFill.fillAmount = _world.EnergyFraction;
            _progressText.text = $"{Mathf.RoundToInt(_world.Progress * 100f)}%";

            Vector2 p = _world.DebugPos;
            Vector2 v = _world.DebugVel;
            string reveal = _world.Lighting != null && _world.Lighting.RevealAll ? "  [ , · 全图照亮 ]" : string.Empty;
            _debugText.text = $"x {p.x:F1}  y {p.y:F1}  vx {v.x:F1}  vy {v.y:F1}  R {_world.DebugLightRadius:F1}{reveal}";

            float deathTarget = _world.IsDead ? 1f : 0f;
            SetImageAlpha(_deathOverlay, Mathf.MoveTowards(_deathOverlay.color.a, deathTarget, Time.deltaTime / GameConst.RespawnDelay));

            float victoryTarget = _world.IsVictory ? 1f : 0f;
            SetImageAlpha(_victoryOverlay, Mathf.MoveTowards(_victoryOverlay.color.a, victoryTarget, Time.deltaTime * 1.2f));
        }

        // —— UI 构建辅助 ——

        private Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            if (sprite == null) img.color = new Color(0f, 0f, 0f, 0.4f);
            return img;
        }

        private Text CreateText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetAnchored(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 anchoredPos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }

        private static void SetImageAlpha(Image img, float a)
        {
            Color c = img.color;
            c.a = a;
            img.color = c;
            img.enabled = a > 0.001f;
        }
    }
}
