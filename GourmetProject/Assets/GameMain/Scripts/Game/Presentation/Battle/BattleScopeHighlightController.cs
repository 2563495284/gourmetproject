using System.Collections.Generic;
using System.Threading;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>统一管理技能 scope 高亮：hover 常驻与上菜短闪互不覆盖。</summary>
    public sealed class BattleScopeHighlightController : MonoBehaviour
    {
        private static readonly Color[] DefaultPalette =
        {
            new Color(0.22f, 0.74f, 1f, 0.86f),
            new Color(1f, 0.58f, 0.2f, 0.86f),
            new Color(0.42f, 1f, 0.47f, 0.86f),
            new Color(1f, 0.36f, 0.66f, 0.86f),
            new Color(0.98f, 0.88f, 0.24f, 0.86f),
        };

        [SerializeField] private Color[] _subSkillPalette;
        [SerializeField] private float _persistentCellWidth = 0.048f;
        [SerializeField] private float _flashCellWidth = 0.085f;
        [SerializeField] private float _settlementCellWidth = 0.072f;
        [SerializeField] private float _flashDuration = 0.32f;
        [SerializeField] private Material _sweetTransferMaterial;
        [SerializeField] private Material _copySkillMaterial;

        private DiningTableView _activeTableView;
        private int _flashVersion;

        public void ShowPersistent(
            DiningTableView tableView,
            IReadOnlyList<SkillExecutionTrace> traces)
        {
            _activeTableView = tableView;
            ClearChannel(BattleScopeHighlightChannel.Persistent);
            if (traces == null)
            {
                return;
            }

            for (int i = 0; i < traces.Count; i++)
            {
                RenderTrace(BattleScopeHighlightChannel.Persistent, traces[i], i, persistent: true);
            }
        }

        public void ClearPersistent()
        {
            ClearChannel(BattleScopeHighlightChannel.Persistent);
        }

        public void Flash(
            DiningTableView tableView,
            IReadOnlyList<SkillExecutionTrace> traces,
            CancellationToken cancellationToken)
        {
            _activeTableView = tableView;
            ClearChannel(BattleScopeHighlightChannel.Flash);
            if (traces == null || traces.Count == 0)
            {
                return;
            }

            for (int i = 0; i < traces.Count; i++)
            {
                RenderTrace(BattleScopeHighlightChannel.Flash, traces[i], i, persistent: false);
            }

            int version = ++_flashVersion;
            _ = ClearFlashAfterAsync(version, cancellationToken);
        }

        public void ClearAll()
        {
            ClearChannel(BattleScopeHighlightChannel.Persistent);
            ClearChannel(BattleScopeHighlightChannel.Flash);
            ClearChannel(BattleScopeHighlightChannel.Settlement);
        }

        /// <summary>结算舞台专用的持续范围，直到当前效果组收束时显式清除。</summary>
        public void ShowSettlement(DiningTableView tableView, SkillExecutionTrace trace)
        {
            _activeTableView = tableView;
            ClearChannel(BattleScopeHighlightChannel.Settlement);
            if (trace != null)
            {
                RenderTrace(BattleScopeHighlightChannel.Settlement, trace, 0, persistent: false);
            }
        }

        public void ClearSettlement()
        {
            ClearChannel(BattleScopeHighlightChannel.Settlement);
        }

        private async Awaitable ClearFlashAfterAsync(int version, CancellationToken cancellationToken)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(Mathf.Max(0.01f, _flashDuration), cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            if (version == _flashVersion)
            {
                ClearChannel(BattleScopeHighlightChannel.Flash);
            }
        }

        private void RenderTrace(BattleScopeHighlightChannel channel, SkillExecutionTrace trace, int index, bool persistent)
        {
            if (trace == null)
            {
                return;
            }

            float cellWidth = channel == BattleScopeHighlightChannel.Settlement
                ? _settlementCellWidth
                : persistent ? _persistentCellWidth : _flashCellWidth;
            int baseLayer = Mathf.Max(0, index);
            int visualIndex = trace.VisualIndex >= 0 ? trace.VisualIndex : baseLayer;
            Color targetColor = PaletteColor(visualIndex * 2);
            Color conditionColor = PaletteColor(visualIndex * 2 + 1);
            if (channel == BattleScopeHighlightChannel.Settlement)
            {
                targetColor = SettlementThemeColor(trace);
                conditionColor = Color.Lerp(targetColor, new Color(1f, 0.88f, 0.46f, 0.88f), 0.42f);
            }
            Material material = MaterialFor(trace);

            int conditionLayer = 2 + baseLayer * 2;
            int targetLayer = conditionLayer + 1;
            if (trace.ConditionType != SkillConditionType.None
                && trace.ConditionCells.Count > 0
                && !SameCells(trace.ConditionCells, trace.VisualTargetCells))
            {
                RenderConditionScope(
                    channel,
                    trace.ConditionCells,
                    conditionLayer,
                    conditionColor,
                    cellWidth * 0.78f,
                    material);
            }

            if (trace.ActionType == SkillActionType.TransferSkills
                && trace.ActionScope == SkillScope.Other)
            {
                _activeTableView?.SetAllExistingScopeHighlight(
                    channel,
                    targetLayer,
                    targetColor,
                    cellWidth,
                    material);
            }
            else
            {
                _activeTableView?.SetScopeRegionHighlight(
                    trace.VisualTargetCells,
                    channel,
                    targetLayer,
                    targetColor,
                    cellWidth,
                    material);
            }
        }

        private void RenderConditionScope(
            BattleScopeHighlightChannel channel,
            IReadOnlyList<GridPos> cells,
            int layer,
            Color color,
            float cellWidth,
            Material material)
        {
            _activeTableView?.SetScopeRegionHighlight(
                cells,
                channel,
                layer,
                color,
                cellWidth,
                material);
        }

        private static bool SameCells(IReadOnlyList<GridPos> a, IReadOnlyList<GridPos> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            var cells = new HashSet<GridPos>(a);
            foreach (GridPos cell in b)
            {
                if (!cells.Contains(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private void ClearChannel(BattleScopeHighlightChannel channel)
        {
            _activeTableView?.ClearScopeHighlights(channel);
        }

        private Color PaletteColor(int index)
        {
            Color[] palette = _subSkillPalette != null && _subSkillPalette.Length > 0
                ? _subSkillPalette
                : DefaultPalette;
            return palette[Mathf.Abs(index) % palette.Length];
        }

        private Material MaterialFor(SkillExecutionTrace trace)
        {
            return trace.Kind switch
            {
                SkillExecutionKind.SweetTransfer => _sweetTransferMaterial,
                SkillExecutionKind.CopiedSkill => _copySkillMaterial,
                _ => null,
            };
        }

        private static Color SettlementThemeColor(SkillExecutionTrace trace)
        {
            return trace.Kind switch
            {
                SkillExecutionKind.SweetTransfer => new Color(1f, 0.30f, 0.68f, 0.96f),
                SkillExecutionKind.CopiedSkill => new Color(0.24f, 0.88f, 1f, 0.96f),
                _ => new Color(1f, 0.72f, 0.18f, 0.96f),
            };
        }
    }
}
