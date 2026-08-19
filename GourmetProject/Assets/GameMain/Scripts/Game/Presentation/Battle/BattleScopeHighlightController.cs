using System.Collections.Generic;
using System.Threading;
using GourmetProject.Gameplay.Board;
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

        private static readonly Color ScopeColor = new Color(0.04f, 0.72f, 1f, 1f);

        [SerializeField] private Color[] _subSkillPalette;
        [SerializeField] private float _persistentCellWidth = 0.048f;
        [SerializeField] private float _flashCellWidth = 0.085f;
        [SerializeField] private float _settlementCellWidth = 0.072f;
        [SerializeField] private float _flashDuration = 0.32f;
        [SerializeField] private Material _sweetTransferMaterial;
        [SerializeField] private Material _copySkillMaterial;

        private readonly Dictionary<BattleScopeHighlightChannel, HashSet<DishPieceView>> _targetPiecesByChannel =
            new Dictionary<BattleScopeHighlightChannel, HashSet<DishPieceView>>();
        private DiningTableView _activeTableView;
        private IReadOnlyDictionary<int, DishPieceView> _activeDishViews;
        private int _flashVersion;

        public void ShowPersistent(
            DiningTableView tableView,
            IReadOnlyList<SkillExecutionTrace> traces,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            _activeTableView = tableView;
            _activeDishViews = dishViews;
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
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            CancellationToken cancellationToken)
        {
            _activeTableView = tableView;
            _activeDishViews = dishViews;
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
        public void ShowSettlement(
            DiningTableView tableView,
            SkillExecutionTrace trace,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            ShowSettlement(
                tableView,
                trace != null ? new[] { trace } : null,
                dishViews);
        }

        public void ShowSettlement(
            DiningTableView tableView,
            IReadOnlyList<SkillExecutionTrace> traces,
            IReadOnlyDictionary<int, DishPieceView> dishViews)
        {
            _activeTableView = tableView;
            _activeDishViews = dishViews;
            ClearChannel(BattleScopeHighlightChannel.Settlement);
            if (traces == null)
            {
                return;
            }

            for (int i = 0; i < traces.Count; i++)
            {
                RenderTrace(BattleScopeHighlightChannel.Settlement, traces[i], i, persistent: false);
            }
        }

        public void ClearSettlement()
        {
            ClearChannel(BattleScopeHighlightChannel.Settlement);
        }

        private void OnDisable()
        {
            ClearAll();
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
            if (trace == null || !CanRenderTrace(trace))
            {
                return;
            }

            float cellWidth = channel == BattleScopeHighlightChannel.Settlement
                ? _settlementCellWidth
                : persistent ? _persistentCellWidth : _flashCellWidth;
            int baseLayer = Mathf.Max(0, index);
            int visualIndex = trace.VisualIndex >= 0 ? trace.VisualIndex : baseLayer;
            Color targetColor = channel == BattleScopeHighlightChannel.Settlement
                ? SettlementThemeColor(trace)
                : PaletteColor(visualIndex * 2);
            Material material = MaterialFor(trace);

            if (trace.ScopeRegionCells.Count > 0)
            {
                _activeTableView?.SetScopeRegionHighlight(
                    trace.ScopeRegionCells,
                    channel,
                    2 + baseLayer * 2,
                    ScopeColor,
                    cellWidth,
                    material);
            }

            RenderTargetDishes(channel, trace, targetColor, visualIndex);
        }

        private bool CanRenderTrace(SkillExecutionTrace trace)
        {
            if (_activeDishViews == null
                || trace.RuntimeSelfDishInstanceId <= 0
                || !_activeDishViews.TryGetValue(trace.RuntimeSelfDishInstanceId, out DishPieceView view)
                || view == null
                || view.Instance == null)
            {
                // 尚未放上餐桌的实时摆放预览没有 DishPieceView，由构建入口直接检查 DishInstance。
                return true;
            }

            return CanDisplayScopeForDish(view.Instance);
        }

        internal static bool CanDisplayScopeForDish(DishInstance dish)
        {
            return dish != null && !dish.SkillsDisabled && !dish.ExcludedFromScore;
        }

        private void RenderTargetDishes(
            BattleScopeHighlightChannel channel,
            SkillExecutionTrace trace,
            Color color,
            int visualIndex)
        {
            if (_activeDishViews == null || trace?.VisualTargetDishInstanceIds == null)
            {
                return;
            }

            if (!_targetPiecesByChannel.TryGetValue(channel, out HashSet<DishPieceView> pieces))
            {
                pieces = new HashSet<DishPieceView>();
                _targetPiecesByChannel[channel] = pieces;
            }

            foreach (int dishId in trace.VisualTargetDishInstanceIds)
            {
                if (dishId <= 0
                    || !_activeDishViews.TryGetValue(dishId, out DishPieceView piece)
                    || piece == null)
                {
                    continue;
                }

                piece.SetScopeTargetGlow(channel, color, visualIndex);
                pieces.Add(piece);
            }
        }

        private void ClearChannel(BattleScopeHighlightChannel channel)
        {
            _activeTableView?.ClearScopeHighlights(channel);
            if (!_targetPiecesByChannel.TryGetValue(channel, out HashSet<DishPieceView> pieces))
            {
                return;
            }

            foreach (DishPieceView piece in pieces)
            {
                piece?.ClearScopeTargetGlow(channel);
            }

            pieces.Clear();
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

        private Color PaletteColor(int index)
        {
            Color[] palette = _subSkillPalette != null && _subSkillPalette.Length > 0
                ? _subSkillPalette
                : DefaultPalette;
            return palette[Mathf.Abs(index) % palette.Length];
        }

        private static Color SettlementThemeColor(SkillExecutionTrace trace)
        {
            return trace.Kind switch
            {
                SkillExecutionKind.SweetTransfer =>
                    SettlementColorPalette.WithAlpha(SettlementColorPalette.SweetTransferSource, 0.96f),
                SkillExecutionKind.CopiedSkill =>
                    SettlementColorPalette.WithAlpha(SettlementColorPalette.CopiedSkillSource, 0.96f),
                _ => SettlementColorPalette.WithAlpha(SettlementColorPalette.NativeSource, 0.96f),
            };
        }

    }
}
