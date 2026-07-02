using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 底部菜谱抽屉：可展开/收起。行动选择时默认收起、可点「展开菜谱」查看（展示态）；
    /// 战斗中由调用方切到「锁定展开」状态，隐藏收起按钮并接上菜交互（阶段二）。
    /// 固定结构在 BattleForm.prefab 的 BottomDrawer 下，条目用 <see cref="RecipeEntryView"/> 数据驱动。
    /// </summary>
    public sealed class RecipeDrawer : MonoBehaviour
    {
        /// <summary>一条菜谱条目的数据（名称、副信息、是否可点、点击回调）。</summary>
        public readonly struct EntryData
        {
            public EntryData(string name, string sub, bool interactable, Action onClick)
            {
                Name = name;
                Sub = sub;
                Interactable = interactable;
                OnClick = onClick;
            }

            public string Name { get; }
            public string Sub { get; }
            public bool Interactable { get; }
            public Action OnClick { get; }
        }

        [SerializeField] private Button _toggleButton;
        [SerializeField] private Text _toggleLabel;
        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _entryContainer;
        [SerializeField] private RecipeEntryView _entryPrefab;
        [SerializeField] private Text _emptyText;

        private readonly List<RecipeEntryView> _entries = new();
        private bool _expanded;
        private bool _locked;

        private void Awake()
        {
            if (_toggleButton != null)
            {
                _toggleButton.onClick.RemoveAllListeners();
                _toggleButton.onClick.AddListener(Toggle);
            }
        }

        /// <summary>切到行动选择态：可自由展开/收起，默认收起。</summary>
        public void ConfigureCollapsible(bool startExpanded = false)
        {
            _locked = false;
            SetExpanded(startExpanded);
            if (_toggleButton != null)
            {
                _toggleButton.gameObject.SetActive(true);
            }
        }

        /// <summary>切到战斗态：锁定展开，不可收起（隐藏收起按钮）。</summary>
        public void ConfigureLockedOpen()
        {
            _locked = true;
            SetExpanded(true);
            if (_toggleButton != null)
            {
                _toggleButton.gameObject.SetActive(false);
            }
        }

        /// <summary>用条目数据重建抽屉内容。</summary>
        public void SetEntries(IReadOnlyList<EntryData> entries)
        {
            ClearEntries();

            bool empty = entries == null || entries.Count == 0;
            if (_emptyText != null)
            {
                _emptyText.gameObject.SetActive(empty);
            }

            if (_entryContainer != null)
            {
                _entryContainer.gameObject.SetActive(!empty);
            }

            if (empty || _entryPrefab == null || _entryContainer == null)
            {
                return;
            }

            int n = entries.Count;
            const float gap = 0.01f;
            float cellW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                EntryData data = entries[i];
                float minX = gap + i * (cellW + gap);
                RecipeEntryView entry = Instantiate(_entryPrefab, _entryContainer);
                entry.gameObject.name = $"RecipeEntry_{i}";
                var rect = (RectTransform)entry.transform;
                rect.anchorMin = new Vector2(minX, 0f);
                rect.anchorMax = new Vector2(minX + cellW, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                entry.Bind(data.Name, data.Sub, data.Interactable, data.OnClick);
                _entries.Add(entry);
            }
        }

        private void Toggle()
        {
            if (_locked)
            {
                return;
            }

            SetExpanded(!_expanded);
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (_content != null)
            {
                _content.gameObject.SetActive(expanded);
            }

            if (_toggleLabel != null)
            {
                _toggleLabel.text = expanded ? "收起菜谱" : "展开菜谱";
            }
        }

        private void ClearEntries()
        {
            foreach (RecipeEntryView entry in _entries)
            {
                if (entry != null)
                {
                    Destroy(entry.gameObject);
                }
            }

            _entries.Clear();
        }
    }
}
