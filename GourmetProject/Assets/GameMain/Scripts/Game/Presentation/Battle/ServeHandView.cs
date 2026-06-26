using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 上菜「商人手」：一只摊开的狗爪，掌垫托着菜品从屏幕上方降到目标格上方，再抽走让菜品落下。
    /// 固定结构（爪 sprite + 假阴影 + 手臂延伸条）摆在 prefab 里，脚本只在运行时布局/驱动（见 presentation-prefab 规则）。
    /// 爪与手臂走 Lit 受光（真实 URP 2D Light2D 明暗）；阴影仍走假阴影软暗斑（见 battle-fake-shadow 规则）。
    /// 手只做平移、不旋转：掌心锚点 = 根坐标 + 固定世界偏移，菜品贴在掌心、抽手时菜品独立落下。
    /// 手臂用程序条（取爪图顶行像素竖直无缝延伸到屏幕外）补齐，保证爪子降到任意低处都不会露出手臂尽头。
    /// </summary>
    public sealed class ServeHandView : MonoBehaviour
    {
        // 掌心锚点相对爪 sprite 中心的纵向偏移（按 sprite 世界高度的比例，+ 向上）。掌垫≈中心偏下，可在 Inspector 微调。
        [SerializeField] private float _palmOffsetYFrac = -0.16f;

        [Header("固定结构（prefab 摆好）")]
        [SerializeField] private SpriteRenderer _hand;
        [SerializeField] private SpriteRenderer _shadow;
        [SerializeField] private SpriteRenderer _arm;

        // 阴影（落在桌面的假投影）随高度的表现：高=远、大、淡；近=贴、小、浓。按爪宽取比例。
        private const float ShadowBaseAlpha = 0.42f;
        private const float ShadowGroundScale = 1.0f;    // 贴近态阴影直径 = 爪宽 * 此比例
        private const float ShadowLiftScale = 1.7f;       // 高空态相对贴近态放大
        private const float ShadowLiftAlphaMul = 0.45f;   // 高空态变淡
        private const float ShadowGroundDrop = 0.35f;     // 贴近态阴影在掌心下方的落差（爪宽比例）
        private const float ShadowLiftDrop = 1.2f;        // 高空态额外落差

        private const float ArmTopMargin = 0.6f;          // 手臂顶端超出屏幕顶的余量
        private const int ArmStripHeight = 8;             // 程序手臂条纹理高度（px）

        private Camera _cam;
        private float _worldHeight = 3.5f;
        private float _pawWidth = 1f;
        private float _handScale = 1f;
        private float _palmWorldOffsetY;
        private Vector3 _shadowBaseScale = Vector3.one;
        private Sprite _armSprite;
        private float _armUnitHeight = 0.08f;
        private bool _built;

        /// <summary>掌心世界坐标：菜品应贴在这里。</summary>
        public Vector3 PalmWorldPosition => transform.position + new Vector3(0f, _palmWorldOffsetY, 0f);

        /// <summary>移动手，使掌心锚点对齐到指定世界坐标，并把手臂延伸条接到屏幕外。</summary>
        public void SetPalmWorld(Vector3 world)
        {
            transform.position = world - new Vector3(0f, _palmWorldOffsetY, 0f);
            UpdateArm();
        }

        /// <summary>构建：按世界高度布局爪 sprite / 阴影 / 手臂延伸条，设 Unlit 材质与排序。</summary>
        public void Build(Camera cam, float worldHeight)
        {
            EnsureRefs();
            _cam = cam != null ? cam : Camera.main;
            _worldHeight = Mathf.Max(0.5f, worldHeight);
            _palmWorldOffsetY = _worldHeight * _palmOffsetYFrac;

            LayoutHand();
            LayoutArm();
            LayoutShadow();
            _built = true;
            SetHeight(1f);
            UpdateArm();
        }

        private void LayoutHand()
        {
            if (_hand == null)
            {
                return;
            }

            _hand.transform.localPosition = Vector3.zero;
            _hand.transform.localRotation = Quaternion.identity;

            Vector2 bounds = _hand.sprite != null ? (Vector2)_hand.sprite.bounds.size : Vector2.one;
            _handScale = bounds.y > 0f ? _worldHeight / bounds.y : 1f;
            _pawWidth = bounds.x * _handScale;
            _hand.transform.localScale = new Vector3(_handScale, _handScale, 1f);

        SpriteRenderStyle.ApplyLitMaterial(_hand);
        // 飞行覆盖层：压在棋盘所有静态层之上；order 低于菜品本体(OrderBody=10)，让掌心托的菜显示在手之上。
        BattleSorting.Apply(_hand, BattleSorting.PiecesFlying, 5);
        }

        /// <summary>从爪图顶行像素生成一条竖直手臂 sprite（pivot 底边中心），用于无缝延伸到屏幕外。</summary>
        private void LayoutArm()
        {
            if (_arm == null || _hand == null || _hand.sprite == null)
            {
                return;
            }

            _armSprite = BuildArmSprite(_hand.sprite);
            if (_armSprite != null)
            {
                _arm.sprite = _armSprite;
                _armUnitHeight = _armSprite.bounds.size.y; // pivot 在底边，bounds.y = 条世界高
            }

            _arm.transform.localRotation = Quaternion.identity;
            SpriteRenderStyle.ApplyLitMaterial(_arm);
            // 手臂在爪之下（order 更低），接缝被爪盖住；同 PiecesFlying 层。
            BattleSorting.Apply(_arm, BattleSorting.PiecesFlying, 3);
        }

        private static Sprite BuildArmSprite(Sprite src)
        {
            Texture2D tex = src.texture;
            if (tex == null)
            {
                return null;
            }

            Rect tr = src.textureRect;
            int x0 = Mathf.RoundToInt(tr.x);
            int w = Mathf.RoundToInt(tr.width);
            int top = Mathf.RoundToInt(tr.y + tr.height);
            int sampleY = Mathf.Clamp(top - 3, 0, tex.height - 1);

            Color[] row;
            try
            {
                row = tex.GetPixels(x0, sampleY, w, 1);
            }
            catch
            {
                return null; // 纹理不可读时放弃手臂条
            }

            var px = new Color[w * ArmStripHeight];
            for (int y = 0; y < ArmStripHeight; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[(y * w) + x] = row[x];
                }
            }

            var strip = new Texture2D(w, ArmStripHeight, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            strip.SetPixels(px);
            strip.Apply();
            return Sprite.Create(strip, new Rect(0f, 0f, w, ArmStripHeight), new Vector2(0.5f, 0f), src.pixelsPerUnit);
        }

        /// <summary>把手臂条接在爪 sprite 顶端，竖直拉伸到屏幕顶外。</summary>
        private void UpdateArm()
        {
            if (_arm == null || _armSprite == null)
            {
                return;
            }

            float pawTopY = transform.position.y + (_worldHeight * 0.5f);
            float overlap = _worldHeight * 0.01f; // 略微下探，盖住接缝
            float bottomY = pawTopY - overlap;

            float screenTopY = _cam != null
                ? _cam.transform.position.y + (_cam.orthographic ? _cam.orthographicSize : 6f)
                : pawTopY + 10f;
            float length = Mathf.Max(0f, (screenTopY + ArmTopMargin) - bottomY);

            _arm.transform.position = new Vector3(transform.position.x, bottomY, transform.position.z + 0.01f);
            float scaleY = _armUnitHeight > 0f ? length / _armUnitHeight : 0f;
            _arm.transform.localScale = new Vector3(_handScale, scaleY, 1f);
            _arm.enabled = length > 0.001f;
        }

        private void LayoutShadow()
        {
            if (_shadow == null)
            {
                return;
            }

            if (_shadow.sprite == null)
            {
                _shadow.sprite = BattleShadow.SoftShadowSprite;
            }

            float ground = _pawWidth * ShadowGroundScale;
            Vector2 bounds = _shadow.sprite != null ? (Vector2)_shadow.sprite.bounds.size : Vector2.one;
            float sx = bounds.x > 0f ? ground / bounds.x : ground;
            float sy = bounds.y > 0f ? (ground * 0.5f) / bounds.y : ground * 0.5f; // 桌面投影压扁成椭圆
            _shadowBaseScale = new Vector3(sx, sy, 1f);

            _shadow.color = new Color(0f, 0f, 0f, ShadowBaseAlpha);
            SpriteRenderStyle.ApplyUnlitMaterial(_shadow);
            // 阴影在手之下、棋盘食品之上：同 PiecesFlying 层但 order 更低。
            BattleSorting.Apply(_shadow, BattleSorting.PiecesFlying, 0);
        }

        /// <summary>设置手离桌高度：1=高空（阴影远/大/淡），0=贴近（阴影近/小/浓）。</summary>
        public void SetHeight(float h01)
        {
            if (!_built || _shadow == null)
            {
                return;
            }

            float h = Mathf.Clamp01(h01);

            float drop = _pawWidth * (ShadowGroundDrop + ShadowLiftDrop * h);
            // 阴影挂在掌心正下方的桌面位置（局部坐标，相对根）。
            _shadow.transform.localPosition = new Vector3(0f, _palmWorldOffsetY - drop, 0.02f);

            float scaleMul = Mathf.Lerp(1f, ShadowLiftScale, h);
            _shadow.transform.localScale = new Vector3(_shadowBaseScale.x * scaleMul, _shadowBaseScale.y * scaleMul, 1f);

            Color c = _shadow.color;
            c.a = ShadowBaseAlpha * Mathf.Lerp(1f, ShadowLiftAlphaMul, h);
            _shadow.color = c;
        }

        /// <summary>兜底引用：容忍 prefab 未手动赋值（见 presentation-prefab 规则）。</summary>
        private void EnsureRefs()
        {
            if (_hand == null)
            {
                Transform t = transform.Find("Hand");
                if (t != null)
                {
                    _hand = t.GetComponent<SpriteRenderer>();
                }
            }

            if (_shadow == null)
            {
                Transform t = transform.Find("Shadow");
                if (t != null)
                {
                    _shadow = t.GetComponent<SpriteRenderer>();
                }
            }

            if (_arm == null)
            {
                Transform t = transform.Find("Arm");
                if (t != null)
                {
                    _arm = t.GetComponent<SpriteRenderer>();
                }
            }
        }
    }
}
