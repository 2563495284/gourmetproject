using System;
using System.Collections.Generic;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 纯场景物体（SpriteRenderer + TextMesh + BoxCollider2D）实现的菜单书，替代原 World Space uGUI 版。
    /// 与餐桌/菜品同走 BattleSorting / WorldInput 体系，保证在 URP 2D 正交相机下稳定可见、可交互。
    ///
    /// 结构按 presentation-prefab 规则预拼在 prefab 里（书皮图/标题/铃铛/翻页钮/页码/tooltip/条目容器），
    /// 由 <see cref="Build"/> 引用并按参考尺寸整体缩放到运行时分配的世界尺寸；只有「随数据变数量」的菜谱条目
    /// 才运行时生成，并填入预置的 <c>LeftPage</c>/<c>RightPage</c> 命名容器。prefab 未拼好时回退到运行时构建。
    ///
    /// 翻页/铃铛按钮为与书皮图同风格的「纯图标」（无胶囊底块），hover 放大 + 暖色提亮，禁用变灰。
    /// </summary>
    public sealed class MenuBookWorldView : MonoBehaviour
    {
        // prefab 预拼时采用的参考尺寸（书体世界宽高）。运行时按 worldWidth/worldHeight 对根做整体缩放贴合。
        // 比例须与 BattleWorldController 里的 aspect(=高/宽) 一致，缩放才不会拉伸变形。
        private const float RefW = 4f;
        private const float RefH = 2.64f;

        private const int FontSize = 64;                 // TextMesh 字体分辨率（清晰度），世界大小由 characterSize 决定
        private const float TextUnit = FontSize * 0.1f;  // 字高(世界) = characterSize * TextUnit

        private const float HoverScale = 1.15f;          // 按钮 hover 放大倍率

        [Header("外观颜色（设计师可在 Inspector 调）")]
        [SerializeField] private Color _bookColor = new(0.30f, 0.19f, 0.10f, 1f);
        [SerializeField] private Color _paperColor = new(0.96f, 0.92f, 0.80f, 1f);
        [SerializeField] private Color _titleColor = new(1f, 0.93f, 0.75f, 1f);
        [SerializeField] private Color _entryColor = new(0.20f, 0.14f, 0.08f, 1f);
        [SerializeField] private Color _entryHoverColor = new(0.85f, 0.30f, 0.08f, 1f);
        [SerializeField] private Color _pageTextColor = new(0.30f, 0.22f, 0.12f, 1f);
        [SerializeField] private Color _navColor = new(0.55f, 0.38f, 0.20f, 1f);
        [SerializeField] private Color _bellColor = new(0.92f, 0.72f, 0.22f, 1f);
        [Tooltip("tooltip 背景用 ui_tag_box 手绘奶油标签(已上色),tint 一般保持白。")]
        [SerializeField] private Color _tooltipBgColor = Color.white;
        [Tooltip("tooltip 文字:奶油底用深棕最清晰。")]
        [SerializeField] private Color _tooltipTextColor = new(0.28f, 0.18f, 0.08f, 1f);

        [Header("图标按钮 tint（图标本身已上色，这里只做态切换）")]
        [Tooltip("常态：图标原色直出，一般保持白色不染色。")]
        [SerializeField] private Color _iconNormalTint = Color.white;
        [Tooltip("Hover：暖琥珀色叠乘，配合放大提示可点击。")]
        [SerializeField] private Color _iconHoverTint = new(1f, 0.82f, 0.45f, 1f);
        [Tooltip("禁用：压暗去饱和。")]
        [SerializeField] private Color _iconDisabledTint = new(0.55f, 0.5f, 0.42f, 0.6f);

        [Header("每页条目数")]
        [SerializeField] private int _perSide = 7;

        [Header("固定结构（prefab 预拼，运行时引用）")]
        [SerializeField] private SpriteRenderer _bookBg;
        [SerializeField] private TextMesh _titleText;
        [SerializeField] private TextMesh _pageText;
        [Tooltip("左页条目容器：运行时菜谱条目挂到这里。")]
        [SerializeField] private Transform _leftPage;
        [Tooltip("右页条目容器：运行时菜谱条目挂到这里。")]
        [SerializeField] private Transform _rightPage;

        [Header("按钮图标（铃铛 + 翻页：纯图标 SpriteRenderer + 碰撞盒）")]
        [SerializeField] private SpriteRenderer _bellIcon;
        [SerializeField] private BoxCollider2D _bellCol;
        [SerializeField] private SpriteRenderer _firstIcon;
        [SerializeField] private BoxCollider2D _firstCol;
        [SerializeField] private SpriteRenderer _prevIcon;
        [SerializeField] private BoxCollider2D _prevCol;
        [SerializeField] private SpriteRenderer _nextIcon;
        [SerializeField] private BoxCollider2D _nextCol;
        [SerializeField] private SpriteRenderer _lastIcon;
        [SerializeField] private BoxCollider2D _lastCol;

        [Header("Hover 详情面板（prefab 预拼，默认隐藏）")]
        [SerializeField] private GameObject _tooltipRoot;
        [SerializeField] private SpriteRenderer _tooltipBg;
        [SerializeField] private TextMesh _tooltipText;

        private static Sprite _whiteSprite;

        private Camera _cam;
        private Action<int> _onBell;
        private int _slotIndex;
        private int _spreadIndex;
        private bool _interactable = true;
        private bool _built;

        private float _w;
        private float _h;

        // 由 ComputeMetrics 计算并缓存的内容区几何，供 FillPage 使用。
        private float _contentTop;
        private float _rowH;
        private float _leftEntryX;
        private float _rightEntryX;
        private float _entryInnerW;

        private GameplayDatabase _db;
        private readonly List<Item> _items = new();

        private Button _first;
        private Button _prev;
        private Button _next;
        private Button _last;
        private Button _bell;
        private readonly List<Button> _buttons = new();
        private readonly List<Entry> _entries = new();

        private Entry _hovered;
        private Button _hoveredButton;

        private int SpreadCount => Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)(_perSide * 2)));

        private Transform LeftEntryParent => _leftPage != null ? _leftPage : transform;

        private Transform RightEntryParent => _rightPage != null ? _rightPage : transform;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                }

                return _whiteSprite;
            }
        }

        /// <summary>构建一本菜单书。<paramref name="worldWidth"/>/<paramref name="worldHeight"/> 为书体世界尺寸。</summary>
        public void Build(int slotIndex, Camera cam, Action<int> onBell, float worldWidth, float worldHeight)
        {
            _slotIndex = slotIndex;
            _cam = cam != null ? cam : Camera.main;
            _onBell = onBell;

            float targetW = Mathf.Max(0.5f, worldWidth);
            float targetH = Mathf.Max(0.5f, worldHeight);

            if (HasAuthoredStructure())
            {
                // 结构按参考尺寸预拼好：在参考空间内布局，再对根做整体缩放贴合运行时分配的世界尺寸。
                _w = RefW;
                _h = RefH;
                transform.localScale = new Vector3(targetW / RefW, targetH / RefH, 1f);
                BindAuthored();
            }
            else
            {
                // 兜底：prefab 未拼结构时退回运行时构建（见 presentation-prefab 规则，不应是常态）。
                _w = targetW;
                _h = targetH;
                transform.localScale = Vector3.one;
                BuildVisual();
            }

            _built = true;
            SetInteractable(true);
            Render();
        }

        private bool HasAuthoredStructure()
        {
            return _bookBg != null
                && _titleText != null
                && _pageText != null
                && _bellIcon != null && _bellCol != null
                && _tooltipRoot != null;
        }

        /// <summary>绑定 prefab 预拼好的结构：归一化材质/排序、构建按钮包装、算内容区几何。</summary>
        private void BindAuthored()
        {
            ApplyPanel(_bookBg, 0);
            ApplyTextSorting(_titleText, 4);
            ApplyTextSorting(_pageText, 4);

            _bell = BindIconButton(_bellIcon, _bellCol, OnBellClicked);
            _first = BindIconButton(_firstIcon, _firstCol, GoFirst);
            _prev = BindIconButton(_prevIcon, _prevCol, GoPrev);
            _next = BindIconButton(_nextIcon, _nextCol, GoNext);
            _last = BindIconButton(_lastIcon, _lastCol, GoLast);

            _buttons.Clear();
            _buttons.Add(_bell);
            _buttons.Add(_first);
            _buttons.Add(_prev);
            _buttons.Add(_next);
            _buttons.Add(_last);

            if (_tooltipBg != null)
            {
                ApplyPanel(_tooltipBg, 30);
            }

            if (_tooltipText != null)
            {
                ApplyTextSorting(_tooltipText, 31);
            }

            _tooltipRoot.SetActive(false);

            ComputeMetrics();
        }

        /// <summary>计算内容区与左右书页的条目布局几何（依赖 <see cref="_w"/>/<see cref="_h"/>）。</summary>
        private void ComputeMetrics()
        {
            float titleH = _h * 0.14f;
            float footerH = _h * 0.16f;
            float pad = _w * 0.03f;
            float halfGap = _w * 0.02f;

            _contentTop = _h * 0.5f - titleH;
            float contentBottom = -_h * 0.5f + footerH;
            float contentH = _contentTop - contentBottom;
            _rowH = contentH / _perSide;

            float leftLeft = -_w * 0.5f + pad;
            float leftRight = -halfGap;
            float leftW = leftRight - leftLeft;
            float rightLeft = halfGap;

            float entryPad = pad * 0.6f;
            _leftEntryX = leftLeft + entryPad;
            _rightEntryX = rightLeft + entryPad;
            _entryInnerW = leftW - entryPad * 2f;
        }

        private void BuildVisual()
        {
            float titleH = _h * 0.14f;
            float footerH = _h * 0.16f;
            float pad = _w * 0.03f;

            // 书体背景。
            _bookBg = AddPanel(transform, "BookBg", _w, _h, _bookColor, Vector3.zero, 0);

            // 条目容器（命名容器，运行时条目挂这里）。
            _leftPage = AddContainer("LeftPage");
            _rightPage = AddContainer("RightPage");

            ComputeMetrics();

            // 标题栏：标题居左，铃铛居右（书内，不出界）。
            float titleY = _h * 0.5f - titleH * 0.5f;
            _titleText = AddText(transform, "Title", "菜谱", titleH * 0.5f, _titleColor,
                TextAnchor.MiddleLeft, TextAlignment.Left, new Vector3(-_w * 0.5f + pad, titleY, -0.02f), 4);

            float bellSize = titleH * 0.9f;
            _bell = AddIconButtonFallback("Bell", "铃", _bellColor, bellSize, bellSize,
                new Vector3(_w * 0.5f - pad - bellSize * 0.5f, titleY, -0.02f), OnBellClicked, out _bellIcon, out _bellCol);

            // 页脚：首/上 + 页码 + 下/末。
            float footerY = -_h * 0.5f + footerH * 0.5f;
            float bsz = footerH * 0.78f;
            _first = AddIconButtonFallback("First", "|<", _navColor, bsz, bsz, new Vector3(-_w * 0.40f, footerY, -0.02f), GoFirst, out _firstIcon, out _firstCol);
            _prev = AddIconButtonFallback("Prev", "<", _navColor, bsz, bsz, new Vector3(-_w * 0.21f, footerY, -0.02f), GoPrev, out _prevIcon, out _prevCol);
            _pageText = AddText(transform, "PageText", "1/1", footerH * 0.42f, _pageTextColor,
                TextAnchor.MiddleCenter, TextAlignment.Center, new Vector3(0f, footerY, -0.02f), 4);
            _next = AddIconButtonFallback("Next", ">", _navColor, bsz, bsz, new Vector3(_w * 0.21f, footerY, -0.02f), GoNext, out _nextIcon, out _nextCol);
            _last = AddIconButtonFallback("Last", ">|", _navColor, bsz, bsz, new Vector3(_w * 0.40f, footerY, -0.02f), GoLast, out _lastIcon, out _lastCol);

            _buttons.Clear();
            _buttons.Add(_bell);
            _buttons.Add(_first);
            _buttons.Add(_prev);
            _buttons.Add(_next);
            _buttons.Add(_last);

            // hover 详情面板（默认隐藏）。
            _tooltipRoot = new GameObject("Tooltip");
            _tooltipRoot.transform.SetParent(transform, false);
            _tooltipBg = AddPanel(_tooltipRoot.transform, "Bg", 1f, 1f, _tooltipBgColor, Vector3.zero, 30);
            _tooltipText = AddText(_tooltipRoot.transform, "Text", string.Empty, 0.12f, _tooltipTextColor,
                TextAnchor.UpperLeft, TextAlignment.Left, new Vector3(0f, 0f, -0.01f), 31);
            _tooltipRoot.SetActive(false);
        }

        /// <summary>设置本书展示的菜谱槽内容（dishIds 可重复，内部去重聚合为「菜名 xN」）。</summary>
        public void SetData(IReadOnlyList<string> dishIds, GameplayDatabase db)
        {
            _db = db;
            _items.Clear();

            if (dishIds != null && db != null)
            {
                var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string id in dishIds)
                {
                    DishDef def = db.GetDish(id);
                    if (def == null)
                    {
                        continue;
                    }

                    if (indexById.TryGetValue(def.Id, out int idx))
                    {
                        _items[idx].Count++;
                    }
                    else
                    {
                        indexById[def.Id] = _items.Count;
                        _items.Add(new Item { Def = def, Count = 1 });
                    }
                }
            }

            _spreadIndex = Mathf.Clamp(_spreadIndex, 0, SpreadCount - 1);
            Render();
        }

        public void SetTitle(string title)
        {
            if (_titleText != null)
            {
                _titleText.text = title;
            }
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            _bell?.SetEnabled(interactable);
        }

        private void Render()
        {
            foreach (Entry e in _entries)
            {
                if (e.Go != null)
                {
                    Destroy(e.Go);
                }
            }

            _entries.Clear();
            _hovered = null;
            HideTooltip();

            int spreadCount = SpreadCount;
            _spreadIndex = Mathf.Clamp(_spreadIndex, 0, spreadCount - 1);
            int start = _spreadIndex * _perSide * 2;
            FillPage(LeftEntryParent, _leftEntryX, start);
            FillPage(RightEntryParent, _rightEntryX, start + _perSide);

            if (_pageText != null)
            {
                _pageText.text = $"{_spreadIndex + 1}/{spreadCount}";
            }

            UpdateNavButtons(spreadCount);
        }

        private void FillPage(Transform parent, float entryX, int startIndex)
        {
            for (int i = 0; i < _perSide; i++)
            {
                int idx = startIndex + i;
                if (idx >= _items.Count)
                {
                    break;
                }

                Item item = _items[idx];
                float y = _contentTop - _rowH * (i + 0.5f);

                var go = new GameObject($"Entry_{idx}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(entryX, y, -0.01f);

                var tm = go.AddComponent<TextMesh>();
                tm.text = $"{item.Def.Name} x{item.Count}";
                tm.fontSize = FontSize;
                tm.characterSize = (_rowH * 0.5f) / TextUnit;
                tm.color = _entryColor;
                tm.anchor = TextAnchor.MiddleLeft;
                tm.alignment = TextAlignment.Left;
                BattleSorting.Apply(go.GetComponent<MeshRenderer>(), BattleSorting.WorldUi, 5);

                // 碰撞盒从行左缘向右覆盖整行（TextMesh 锚点在左，故 offset 右移半宽）。
                var col = go.AddComponent<BoxCollider2D>();
                col.size = new Vector2(_entryInnerW, _rowH * 0.9f);
                col.offset = new Vector2(_entryInnerW * 0.5f, 0f);

                _entries.Add(new Entry
                {
                    Go = go,
                    Col = col,
                    Text = tm,
                    Tooltip = DishInfoText.Tooltip(item.Def, item.Def.SkillIds, item.Def.FlavorId, _db),
                });
            }
        }

        private void Update()
        {
            if (!_built || _cam == null)
            {
                return;
            }

            Vector2 world = WorldInput.MouseWorld(_cam);

            // 按钮 hover：放大 + 暖色提亮（仅可点击的按钮）。
            Button hitButton = null;
            foreach (Button b in _buttons)
            {
                if (b.Enabled && b.Col != null && b.Col.OverlapPoint(world))
                {
                    hitButton = b;
                    break;
                }
            }

            if (hitButton != _hoveredButton)
            {
                _hoveredButton?.SetHover(false);
                hitButton?.SetHover(true);
                _hoveredButton = hitButton;
            }

            if (_interactable && WorldInput.PrimaryPressedThisFrame && hitButton != null)
            {
                hitButton.OnClick?.Invoke();
            }

            Entry hit = null;
            foreach (Entry e in _entries)
            {
                if (e.Col != null && e.Col.OverlapPoint(world))
                {
                    hit = e;
                    break;
                }
            }

            if (hit != _hovered)
            {
                SetHover(_hovered, false);
                SetHover(hit, true);
                _hovered = hit;
                if (hit != null)
                {
                    ShowTooltip(hit);
                }
                else
                {
                    HideTooltip();
                }
            }
        }

        private void SetHover(Entry e, bool on)
        {
            if (e == null || e.Text == null)
            {
                return;
            }

            e.Text.color = on ? _entryHoverColor : _entryColor;
            e.Text.transform.localScale = on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
        }

        private void ShowTooltip(Entry e)
        {
            if (_tooltipRoot == null || string.IsNullOrEmpty(e.Tooltip))
            {
                return;
            }

            string[] lines = e.Tooltip.Split('\n');
            const float lineH = 0.135f;
            // padX/padY 要盖过 ui_tag_box 的 9-slice 描边圈(left/right≈0.22, top/bottom≈0.17 世界单位),
            // 文字才落在奶油内区而不压到棕橙边框上。
            const float padX = 0.22f;
            const float padY = 0.16f;
            float ttW = Mathf.Max(_w * 0.66f, 1.9f);
            // 9-slice 竖向最小高度须 ≥ 上下描边和,否则圆角会被挤压变形。
            float ttH = Mathf.Max(Mathf.Max(lines.Length, 1) * lineH + padY * 2f, 0.55f);

            _tooltipRoot.transform.localPosition = new Vector3(_w * 0.5f + ttW * 0.5f + 0.04f, e.Go.transform.localPosition.y, -0.05f);

            if (_tooltipBg != null)
            {
                if (_tooltipBg.drawMode == SpriteDrawMode.Simple)
                {
                    // 兜底纯色块:无 9-slice,用缩放铺满。
                    _tooltipBg.transform.localScale = new Vector3(ttW, ttH, 1f);
                }
                else
                {
                    // 常态:ui_tag_box 9-slice,描边随尺寸保持不变形。
                    _tooltipBg.size = new Vector2(ttW, ttH);
                }
            }

            _tooltipText.text = e.Tooltip;
            _tooltipText.characterSize = (lineH * 0.82f) / TextUnit;
            _tooltipText.transform.localPosition = new Vector3(-ttW * 0.5f + padX, ttH * 0.5f - padY, -0.01f);
            _tooltipRoot.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(false);
            }
        }

        private void UpdateNavButtons(int spreadCount)
        {
            bool hasPrev = _spreadIndex > 0;
            bool hasNext = _spreadIndex < spreadCount - 1;
            _first?.SetEnabled(hasPrev);
            _prev?.SetEnabled(hasPrev);
            _next?.SetEnabled(hasNext);
            _last?.SetEnabled(hasNext);
        }

        private void GoFirst() => GoTo(0);

        private void GoPrev() => GoTo(_spreadIndex - 1);

        private void GoNext() => GoTo(_spreadIndex + 1);

        private void GoLast() => GoTo(SpreadCount - 1);

        private void GoTo(int target)
        {
            target = Mathf.Clamp(target, 0, SpreadCount - 1);
            if (target == _spreadIndex)
            {
                return;
            }

            _spreadIndex = target;
            Render();
        }

        private void OnBellClicked()
        {
            if (_interactable)
            {
                _onBell?.Invoke(_slotIndex);
            }
        }

        /// <summary>归一化 prefab 预拼面板的材质与排序（颜色保留 prefab 里设计师设定的值）。</summary>
        private void ApplyPanel(SpriteRenderer sr, int order)
        {
            if (sr == null)
            {
                return;
            }

            if (sr.sprite == null)
            {
                sr.sprite = WhiteSprite;
            }

            SpriteRenderStyle.ApplyUnlitMaterial(sr);
            BattleSorting.Apply(sr, BattleSorting.WorldUi, order);
        }

        private static void ApplyTextSorting(TextMesh tm, int order)
        {
            if (tm == null)
            {
                return;
            }

            BattleSorting.Apply(tm.GetComponent<MeshRenderer>(), BattleSorting.WorldUi, order);
        }

        /// <summary>把 prefab 预拼好的图标按钮（纯图标 SpriteRenderer + 碰撞盒）包装成可交互按钮。</summary>
        private Button BindIconButton(SpriteRenderer icon, BoxCollider2D col, Action onClick)
        {
            if (icon != null)
            {
                if (icon.sprite == null)
                {
                    icon.sprite = WhiteSprite;
                }

                SpriteRenderStyle.ApplyUnlitMaterial(icon);
                BattleSorting.Apply(icon, BattleSorting.WorldUi, 6);
            }

            var button = new Button
            {
                Icon = icon,
                Col = col,
                NormalTint = _iconNormalTint,
                HoverTint = _iconHoverTint,
                DisabledTint = _iconDisabledTint,
                OnClick = onClick,
            };
            button.CaptureBaseScale();
            button.SetEnabled(true);
            return button;
        }

        private Transform AddContainer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        private SpriteRenderer AddPanel(Transform parent, string name, float w, float h, Color color, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.color = color;
            SpriteRenderStyle.ApplyUnlitMaterial(sr);
            BattleSorting.Apply(sr, BattleSorting.WorldUi, order);
            return sr;
        }

        private TextMesh AddText(Transform parent, string name, string text, float charHeight, Color color,
            TextAnchor anchor, TextAlignment alignment, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = FontSize;
            tm.characterSize = charHeight / TextUnit;
            tm.color = color;
            tm.anchor = anchor;
            tm.alignment = alignment;
            BattleSorting.Apply(go.GetComponent<MeshRenderer>(), BattleSorting.WorldUi, order);
            return tm;
        }

        /// <summary>
        /// 兜底按钮：prefab 没拼图标时用纯色块 + 字符当图标，保证不崩、可点击。非常态。
        /// </summary>
        private Button AddIconButtonFallback(string name, string glyph, Color color, float w, float h, Vector3 localPos, Action onClick,
            out SpriteRenderer icon, out BoxCollider2D col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            icon = AddPanel(go.transform, "Icon", w, h, color, Vector3.zero, 6);
            AddText(go.transform, "Label", glyph, h * 0.5f, Color.white,
                TextAnchor.MiddleCenter, TextAlignment.Center, new Vector3(0f, 0f, -0.01f), 7);

            col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(w, h);

            var button = new Button
            {
                Icon = icon,
                Col = col,
                NormalTint = color,
                HoverTint = Color.Lerp(color, Color.white, 0.35f),
                DisabledTint = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.6f),
                OnClick = onClick,
            };
            button.CaptureBaseScale();
            button.SetEnabled(true);
            return button;
        }

        private sealed class Button
        {
            public SpriteRenderer Icon;
            public BoxCollider2D Col;
            public Color NormalTint = Color.white;
            public Color HoverTint = Color.white;
            public Color DisabledTint = Color.gray;
            public Action OnClick;
            public bool Enabled = true;

            private Vector3 _baseScale = Vector3.one;
            private bool _hover;

            public void CaptureBaseScale()
            {
                if (Icon != null)
                {
                    _baseScale = Icon.transform.localScale;
                }
            }

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
                if (!enabled)
                {
                    _hover = false;
                }

                Apply();
            }

            public void SetHover(bool on)
            {
                _hover = on && Enabled;
                Apply();
            }

            private void Apply()
            {
                if (Icon == null)
                {
                    return;
                }

                Icon.color = !Enabled ? DisabledTint : (_hover ? HoverTint : NormalTint);
                Icon.transform.localScale = _hover ? _baseScale * HoverScale : _baseScale;
            }
        }

        private sealed class Entry
        {
            public GameObject Go;
            public BoxCollider2D Col;
            public TextMesh Text;
            public string Tooltip;
        }

        private sealed class Item
        {
            public DishDef Def;
            public int Count;
        }
    }
}
