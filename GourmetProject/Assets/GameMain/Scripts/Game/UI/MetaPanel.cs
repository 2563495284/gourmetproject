using System.Collections.Generic;
using GourmetProject.Game.Platformer;
using GourmetProject.Game.Roguelike;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 局外成长面板（设计文档 13.10）：代码构建的屏幕 Overlay。右上角常驻"成长"按钮显示碎片，
    /// 点击展开面板，可用光之碎片提升初始能量上限、解锁默认锁定的进阶/终极技进入抽取池。
    /// 由 <see cref="GourmetProject.Game.Procedure.ProcedureMenu"/> 在菜单期间创建。
    /// </summary>
    public sealed class MetaPanel : MonoBehaviour
    {
        private Font _font;
        private GameObject _panel;
        private Text _toggleLabel;
        private Text _statsText;

        private sealed class Row
        {
            public Text Label;
            public Button Button;
            public Text ButtonText;
            public string SkillId;     // null => 能量上限行
        }

        private readonly List<Row> _rows = new List<Row>();
        private SkillCatalog _catalog;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _catalog = SkillCatalog.FromConfig();
            Build();
            Refresh();
        }

        private void Build()
        {
            var canvasGo = new GameObject("MetaPanelCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 600f);
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform rootT = canvasGo.transform;

            // —— 右上角常驻"成长"按钮 ——
            Button toggle = CreateButton("ToggleButton", rootT, out _toggleLabel, "成长");
            SetAnchored(toggle.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(150f, 36f), new Vector2(1f, 1f));
            toggle.onClick.AddListener(() => SetOpen(!_panel.activeSelf));

            // —— 面板容器（居中，默认隐藏）——
            _panel = CreatePanelRoot(rootT);

            var title = CreateText("Title", _panel.transform, "局外成长 · 光之碎片", 24, TextAnchor.UpperCenter);
            SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(460f, 32f), new Vector2(0.5f, 1f));

            _statsText = CreateText("Stats", _panel.transform, string.Empty, 15, TextAnchor.UpperCenter);
            _statsText.color = new Color(0.8f, 0.9f, 1f, 0.9f);
            SetAnchored(_statsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(460f, 24f), new Vector2(0.5f, 1f));

            float y = -92f;
            const float rowH = 40f;

            // 能量上限升级行。
            _rows.Add(CreateRow(_panel.transform, null, y));
            y -= rowH;

            // 可解锁技能行。
            foreach (string id in MetaCatalog.LockableSkillIds)
            {
                _rows.Add(CreateRow(_panel.transform, id, y));
                y -= rowH;
            }

            // 关闭按钮。
            Button close = CreateButton("CloseButton", _panel.transform, out Text closeLabel, "关闭");
            closeLabel.text = "关闭";
            SetAnchored(close.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(140f, 34f), new Vector2(0.5f, 0f));
            close.onClick.AddListener(() => SetOpen(false));

            _panel.SetActive(false);
        }

        private GameObject CreatePanelRoot(Transform parent)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.09f, 1f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(500f, 520f);
            rt.anchoredPosition = Vector2.zero;
            return go;
        }

        private Row CreateRow(Transform parent, string skillId, float y)
        {
            var row = new Row { SkillId = skillId };

            row.Label = CreateText("RowLabel", parent, string.Empty, 14, TextAnchor.MiddleLeft);
            SetAnchored(row.Label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-230f, y), new Vector2(300f, 38f), new Vector2(0f, 1f));

            row.Button = CreateButton("RowButton", parent, out row.ButtonText, string.Empty);
            SetAnchored(row.Button.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(230f, y - 2f), new Vector2(140f, 32f), new Vector2(1f, 1f));

            string captured = skillId;
            row.Button.onClick.AddListener(() => OnRowClicked(captured));
            return row;
        }

        private void OnRowClicked(string skillId)
        {
            MetaProfile meta = MetaProfile.Current;
            if (skillId == null) meta.TryUpgradeEnergyCap();
            else meta.TryUnlockSkill(skillId);
            Refresh();
        }

        private void SetOpen(bool open)
        {
            _panel.SetActive(open);
            if (open) Refresh();
        }

        private void Refresh()
        {
            MetaProfile meta = MetaProfile.Current;
            _toggleLabel.text = $"成长  碎片 {meta.Shards}";
            _statsText.text = $"碎片 {meta.Shards}   最高 {Mathf.RoundToInt(meta.BestProgress * 100f)}%   开局 {meta.RunCount}   通关 {meta.Victories}";

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.SkillId == null)
                {
                    int lvl = meta.EnergyCapLevel;
                    int max = GameConst.MetaEnergyCapMaxLevel;
                    row.Label.text = $"初始能量上限 +{(int)meta.EnergyCapBonus}  (Lv {lvl}/{max})";
                    if (meta.IsEnergyCapMaxed)
                    {
                        row.ButtonText.text = "已满级";
                        row.Button.interactable = false;
                    }
                    else
                    {
                        int cost = meta.EnergyCapCost;
                        row.ButtonText.text = $"升级 {cost}";
                        row.Button.interactable = meta.Shards >= cost;
                    }
                }
                else
                {
                    SkillDef def = _catalog?.Get(row.SkillId);
                    string name = def != null ? def.Name : row.SkillId;
                    row.Label.text = name;
                    if (meta.IsSkillUnlocked(row.SkillId))
                    {
                        row.ButtonText.text = "已解锁";
                        row.Button.interactable = false;
                    }
                    else
                    {
                        row.ButtonText.text = $"解锁 {MetaCatalog.SkillUnlockCost}";
                        row.Button.interactable = meta.Shards >= MetaCatalog.SkillUnlockCost;
                    }
                }
            }
        }

        // —— UI 构建辅助 ——

        private Button CreateButton(string name, Transform parent, out Text label, string text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.45f, 0.8f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            label = CreateText("Text", go.transform, text, 14, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return btn;
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
    }
}
