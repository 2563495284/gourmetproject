using System;
using System.Collections.Generic;
using GourmetProject.Game.UI;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 纯场景物体（SpriteRenderer + TextMesh + BoxCollider2D）实现的菜单书，替代原 World Space uGUI 版。
    /// 与棋盘/菜品同走 BattleSorting / WorldInput 体系，保证在 URP 2D 正交相机下稳定可见、可交互。
    /// 结构扁平：根节点 scale=1，所有子物体直接用世界单位摆放，避免父子缩放耦合。
    /// </summary>
    public sealed class MenuBookWorldView : MonoBehaviour
    {
        private const int PerSide = 7;
        private const int FontSize = 64;                 // TextMesh 字体分辨率（清晰度），世界大小由 characterSize 决定
        private const float TextUnit = FontSize * 0.1f;  // 字高(世界) = characterSize * TextUnit

        private static readonly Color BookColor = new(0.30f, 0.19f, 0.10f, 1f);
        private static readonly Color PaperColor = new(0.96f, 0.92f, 0.80f, 1f);
        private static readonly Color TitleColor = new(1f, 0.93f, 0.75f, 1f);
        private static readonly Color EntryColor = new(0.20f, 0.14f, 0.08f, 1f);
        private static readonly Color EntryHoverColor = new(0.85f, 0.30f, 0.08f, 1f);
        private static readonly Color PageTextColor = new(0.30f, 0.22f, 0.12f, 1f);
        private static readonly Color NavColor = new(0.55f, 0.38f, 0.20f, 1f);
        private static readonly Color BellColor = new(0.92f, 0.72f, 0.22f, 1f);
        private static readonly Color TooltipBgColor = new(0.10f, 0.08f, 0.05f, 0.97f);
        private static readonly Color TooltipTextColor = new(0.98f, 0.95f, 0.86f, 1f);

        private static Sprite _whiteSprite;

        private Camera _cam;
        private Action<int> _onBell;
        private int _slotIndex;
        private int _spreadIndex;
        private bool _interactable = true;
        private bool _built;

        private float _w;
        private float _h;

        // 由 BuildVisual 计算并缓存的内容区几何，供 FillPage 使用。
        private float _contentTop;
        private float _rowH;
        private float _leftEntryX;
        private float _rightEntryX;
        private float _entryInnerW;

        private GameplayDatabase _db;
        private readonly List<Item> _items = new();

        private TextMesh _titleText;
        private TextMesh _pageText;
        private Button _first;
        private Button _prev;
        private Button _next;
        private Button _last;
        private Button _bell;
        private readonly List<Button> _buttons = new();
        private readonly List<Entry> _entries = new();

        private GameObject _tooltipRoot;
        private SpriteRenderer _tooltipBg;
        private TextMesh _tooltipText;
        private Entry _hovered;

        private int SpreadCount => Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)(PerSide * 2)));

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
            _w = Mathf.Max(0.5f, worldWidth);
            _h = Mathf.Max(0.5f, worldHeight);

            BuildVisual();
            _built = true;
            SetInteractable(true);
            Render();
        }

        private void BuildVisual()
        {
            float titleH = _h * 0.14f;
            float footerH = _h * 0.16f;
            float pad = _w * 0.03f;
            float halfGap = _w * 0.02f;

            // 书体背景。
            AddPanel(transform, "BookBg", _w, _h, BookColor, Vector3.zero, 0);

            // 内容区与左右书页。
            _contentTop = _h * 0.5f - titleH;
            float contentBottom = -_h * 0.5f + footerH;
            float contentH = _contentTop - contentBottom;
            _rowH = contentH / PerSide;
            float paperCenterY = (_contentTop + contentBottom) * 0.5f;

            float leftLeft = -_w * 0.5f + pad;
            float leftRight = -halfGap;
            float leftW = leftRight - leftLeft;
            float rightLeft = halfGap;
            float rightRight = _w * 0.5f - pad;
            float rightW = rightRight - rightLeft;

            AddPanel(transform, "LeftPaper", leftW, contentH, PaperColor, new Vector3((leftLeft + leftRight) * 0.5f, paperCenterY, 0.01f), 2);
            AddPanel(transform, "RightPaper", rightW, contentH, PaperColor, new Vector3((rightLeft + rightRight) * 0.5f, paperCenterY, 0.01f), 2);

            float entryPad = pad * 0.6f;
            _leftEntryX = leftLeft + entryPad;
            _rightEntryX = rightLeft + entryPad;
            _entryInnerW = leftW - entryPad * 2f;

            // 标题栏：标题居左，铃铛居右（书内，不出界）。
            float titleY = _h * 0.5f - titleH * 0.5f;
            _titleText = AddText(transform, "Title", "菜谱", titleH * 0.5f, TitleColor,
                TextAnchor.MiddleLeft, TextAlignment.Left, new Vector3(-_w * 0.5f + pad, titleY, -0.02f), 4);

            float bellW = _w * 0.20f;
            float bellH = titleH * 0.82f;
            _bell = AddButton("Bell", "铃", BellColor, bellW, bellH,
                new Vector3(_w * 0.5f - pad - bellW * 0.5f, titleY, -0.02f), () => OnBellClicked());
            _buttons.Add(_bell);

            // 页脚：首/上 + 页码 + 下/末。
            float footerY = -_h * 0.5f + footerH * 0.5f;
            float bw = _w * 0.17f;
            float bh = footerH * 0.7f;
            _first = AddButton("First", "|<", NavColor, bw, bh, new Vector3(-_w * 0.40f, footerY, -0.02f), GoFirst);
            _prev = AddButton("Prev", "<", NavColor, bw, bh, new Vector3(-_w * 0.21f, footerY, -0.02f), GoPrev);
            _pageText = AddText(transform, "PageText", "1/1", footerH * 0.42f, PageTextColor,
                TextAnchor.MiddleCenter, TextAlignment.Center, new Vector3(0f, footerY, -0.02f), 4);
            _next = AddButton("Next", ">", NavColor, bw, bh, new Vector3(_w * 0.21f, footerY, -0.02f), GoNext);
            _last = AddButton("Last", ">|", NavColor, bw, bh, new Vector3(_w * 0.40f, footerY, -0.02f), GoLast);
            _buttons.Add(_first);
            _buttons.Add(_prev);
            _buttons.Add(_next);
            _buttons.Add(_last);

            // hover 详情面板（默认隐藏）。
            _tooltipRoot = new GameObject("Tooltip");
            _tooltipRoot.transform.SetParent(transform, false);
            _tooltipBg = AddPanel(_tooltipRoot.transform, "Bg", 1f, 1f, TooltipBgColor, Vector3.zero, 30);
            _tooltipText = AddText(_tooltipRoot.transform, "Text", string.Empty, 0.12f, TooltipTextColor,
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
            int start = _spreadIndex * PerSide * 2;
            FillPage(_leftEntryX, start);
            FillPage(_rightEntryX, start + PerSide);

            if (_pageText != null)
            {
                _pageText.text = $"{_spreadIndex + 1}/{spreadCount}";
            }

            UpdateNavButtons(spreadCount);
        }

        private void FillPage(float entryX, int startIndex)
        {
            for (int i = 0; i < PerSide; i++)
            {
                int idx = startIndex + i;
                if (idx >= _items.Count)
                {
                    break;
                }

                Item item = _items[idx];
                float y = _contentTop - _rowH * (i + 0.5f);

                var go = new GameObject($"Entry_{idx}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(entryX, y, -0.01f);

                var tm = go.AddComponent<TextMesh>();
                tm.text = $"{item.Def.Name} x{item.Count}";
                tm.fontSize = FontSize;
                tm.characterSize = (_rowH * 0.5f) / TextUnit;
                tm.color = EntryColor;
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
                    Tooltip = DishInfoText.Tooltip(item.Def, item.Def.InherentTags, _db),
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

            if (_interactable && WorldInput.PrimaryPressedThisFrame)
            {
                foreach (Button b in _buttons)
                {
                    if (b.Enabled && b.Col != null && b.Col.OverlapPoint(world))
                    {
                        b.OnClick?.Invoke();
                        break;
                    }
                }
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

            e.Text.color = on ? EntryHoverColor : EntryColor;
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
            const float padX = 0.09f;
            const float padY = 0.07f;
            float ttW = Mathf.Max(_w * 0.7f, 1.7f);
            float ttH = Mathf.Max(lines.Length, 1) * lineH + padY * 2f;

            _tooltipRoot.transform.localPosition = new Vector3(_w * 0.5f + ttW * 0.5f + 0.06f, e.Go.transform.localPosition.y, -0.05f);
            _tooltipBg.transform.localScale = new Vector3(ttW, ttH, 1f);
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

        private Button AddButton(string name, string label, Color color, float w, float h, Vector3 localPos, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            SpriteRenderer bg = AddPanel(go.transform, "Bg", w, h, color, Vector3.zero, 3);
            AddText(go.transform, "Label", label, h * 0.5f, Color.white,
                TextAnchor.MiddleCenter, TextAlignment.Center, new Vector3(0f, 0f, -0.01f), 4);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(w, h);

            return new Button { Bg = bg, Col = col, Normal = color, OnClick = onClick };
        }

        private sealed class Button
        {
            public SpriteRenderer Bg;
            public BoxCollider2D Col;
            public Color Normal;
            public Action OnClick;
            public bool Enabled = true;

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
                if (Bg != null)
                {
                    Bg.color = enabled
                        ? Normal
                        : new Color(Normal.r * 0.5f, Normal.g * 0.5f, Normal.b * 0.5f, 0.6f);
                }
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
