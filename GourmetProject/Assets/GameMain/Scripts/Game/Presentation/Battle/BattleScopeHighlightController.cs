using System.Collections.Generic;
using System.Threading;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>统一管理技能 scope 高亮：hover 常驻与结算短闪互不覆盖。</summary>
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

        [SerializeField] private Color _ownerColor = new Color(1f, 0.92f, 0.35f, 0.9f);
        [SerializeField] private Color _settlementOwnerColor = new Color(1f, 0.82f, 0.15f, 0.98f);
        [SerializeField] private Color _runtimeSelfColor = new Color(0.42f, 0.92f, 1f, 0.9f);
        [SerializeField] private Color[] _subSkillPalette;
        [SerializeField] private float _persistentCellWidth = 0.048f;
        [SerializeField] private float _flashCellWidth = 0.085f;
        [SerializeField] private float _persistentDishInflate = 1.08f;
        [SerializeField] private float _flashDishInflate = 1.12f;
        [SerializeField] private float _dishInflateStep = 0.018f;
        [SerializeField, Range(0f, 0.2f)] private float _persistentDishOutlineWidth = 0.095f;
        [SerializeField, Range(0f, 0.2f)] private float _flashDishOutlineWidth = 0.13f;
        [SerializeField] private float _settlementOwnerInflate = 1.16f;
        [SerializeField, Range(0f, 0.2f)] private float _settlementOwnerOutlineWidth = 0.16f;
        [SerializeField] private float _flashDuration = 0.32f;
        [SerializeField] private Material _sweetTransferMaterial;
        [SerializeField] private Material _copySkillMaterial;
        [SerializeField] private Material _settlementOwnerMaterial;

        private DiningTableView _activeTableView;
        private IReadOnlyDictionary<int, DishPieceView> _activeDishViews;
        private int _flashVersion;
        private int _settlementOwnerDishInstanceId;

        public void ShowPersistent(
            DiningTableView tableView,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            IReadOnlyList<SkillExecutionTrace> traces)
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

        public void ShowSettlementOwner(
            DiningTableView tableView,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            int ownerDishInstanceId)
        {
            _activeTableView = tableView;
            _activeDishViews = dishViews;
            if (_settlementOwnerDishInstanceId == ownerDishInstanceId && ownerDishInstanceId > 0)
            {
                return;
            }

            ClearSettlementOwner();
            if (ownerDishInstanceId <= 0)
            {
                return;
            }

            _settlementOwnerDishInstanceId = ownerDishInstanceId;
            SetDishGlow(
                ownerDishInstanceId,
                BattleScopeHighlightChannel.SettlementOwner,
                0,
                _settlementOwnerColor,
                _settlementOwnerInflate,
                _settlementOwnerOutlineWidth,
                _settlementOwnerMaterial);
        }

        public void ClearSettlementOwner()
        {
            ClearChannel(BattleScopeHighlightChannel.SettlementOwner);
            _settlementOwnerDishInstanceId = 0;
        }

        public void Flash(
            DiningTableView tableView,
            IReadOnlyDictionary<int, DishPieceView> dishViews,
            SettlementScopeSignal signal,
            CancellationToken cancellationToken)
        {
            _activeTableView = tableView;
            _activeDishViews = dishViews;
            ClearChannel(BattleScopeHighlightChannel.Flash);
            if (signal.IsEmpty)
            {
                return;
            }

            if (signal.Trace != null)
            {
                int index = signal.Trace.VisualIndex >= 0 ? signal.Trace.VisualIndex : signal.Trace.RuleOrder;
                RenderTrace(BattleScopeHighlightChannel.Flash, signal.Trace, index, persistent: false);
            }
            else if (signal.OwnerDishInstanceId > 0)
            {
                SetDishGlow(
                    signal.OwnerDishInstanceId,
                    BattleScopeHighlightChannel.Flash,
                    0,
                    _ownerColor,
                    _flashDishInflate,
                    _flashDishOutlineWidth,
                    null);
            }

            int version = ++_flashVersion;
            _ = ClearFlashAfterAsync(version, cancellationToken);
        }

        public void ClearAll()
        {
            ClearChannel(BattleScopeHighlightChannel.Persistent);
            ClearChannel(BattleScopeHighlightChannel.Flash);
            ClearSettlementOwner();
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

            float dishInflate = persistent ? _persistentDishInflate : _flashDishInflate;
            float dishOutlineWidth = persistent ? _persistentDishOutlineWidth : _flashDishOutlineWidth;
            float cellWidth = persistent ? _persistentCellWidth : _flashCellWidth;
            int baseLayer = Mathf.Max(0, index);
            Color subSkillColor = PaletteColor(baseLayer);
            Material material = MaterialFor(trace);

            if (trace.OwnerDishInstanceId > 0)
            {
                SetDishGlow(trace.OwnerDishInstanceId, channel, 0, _ownerColor, dishInflate, dishOutlineWidth, null);
            }

            if (trace.RuntimeSelfDishInstanceId > 0
                && trace.RuntimeSelfDishInstanceId != trace.OwnerDishInstanceId)
            {
                SetDishGlow(
                    trace.RuntimeSelfDishInstanceId,
                    channel,
                    1,
                    _runtimeSelfColor,
                    dishInflate + _dishInflateStep,
                    dishOutlineWidth,
                    null);
            }

            int conditionLayer = 2 + baseLayer * 2;
            int targetLayer = conditionLayer + 1;
            if (trace.ConditionType != SkillConditionType.None
                && trace.ConditionCells.Count > 0
                && !SameCells(trace.ConditionCells, trace.VisualTargetCells))
            {
                Color conditionColor = Color.Lerp(subSkillColor, Color.white, 0.28f);
                conditionColor.a = subSkillColor.a * 0.62f;
                RenderConditionScope(
                    channel,
                    trace.ConditionCells,
                    conditionLayer,
                    conditionColor,
                    dishInflate,
                    dishOutlineWidth * 0.72f,
                    cellWidth * 0.78f,
                    persistent);
            }

            foreach (int dishId in trace.VisualTargetDishInstanceIds)
            {
                SetDishGlow(
                    dishId,
                    channel,
                    targetLayer,
                    subSkillColor,
                    dishInflate + _dishInflateStep * baseLayer,
                    dishOutlineWidth,
                    material);
            }

            foreach (GridPos cell in trace.VisualTargetCells)
            {
                _activeTableView?.SetScopeHighlight(
                    cell,
                    channel,
                    targetLayer,
                    subSkillColor,
                    cellWidth,
                    persistent ? 0.14f : 0.26f);
            }
        }

        private void RenderConditionScope(
            BattleScopeHighlightChannel channel,
            IReadOnlyList<GridPos> cells,
            int layer,
            Color color,
            float dishInflate,
            float dishOutlineWidth,
            float cellWidth,
            bool persistent)
        {
            var conditionCells = new HashSet<GridPos>(cells);
            foreach (GridPos cell in cells)
            {
                _activeTableView?.SetScopeHighlight(
                    cell,
                    channel,
                    layer,
                    color,
                    cellWidth,
                    persistent ? 0.08f : 0.16f);
            }

            if (_activeDishViews == null)
            {
                return;
            }

            foreach (KeyValuePair<int, DishPieceView> pair in _activeDishViews)
            {
                DishPieceView view = pair.Value;
                if (view?.Instance == null || !TouchesAnyCell(view.Instance.OccupiedCells, conditionCells))
                {
                    continue;
                }

                view.SetScopeGlow(channel, layer, color, dishInflate, dishOutlineWidth);
            }
        }

        private static bool TouchesAnyCell(
            IReadOnlyList<GridPos> occupiedCells,
            HashSet<GridPos> scopeCells)
        {
            if (occupiedCells == null || scopeCells == null)
            {
                return false;
            }

            foreach (GridPos cell in occupiedCells)
            {
                if (scopeCells.Contains(cell))
                {
                    return true;
                }
            }

            return false;
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

        private void SetDishGlow(
            int dishInstanceId,
            BattleScopeHighlightChannel channel,
            int layer,
            Color color,
            float inflate,
            float outlineWidth,
            Material material)
        {
            if (_activeDishViews == null
                || !_activeDishViews.TryGetValue(dishInstanceId, out DishPieceView view)
                || view == null)
            {
                return;
            }

            view.SetScopeGlow(channel, layer, color, inflate, outlineWidth, material);
        }

        private void ClearChannel(BattleScopeHighlightChannel channel)
        {
            _activeTableView?.ClearScopeHighlights(channel);
            if (_activeDishViews == null)
            {
                return;
            }

            foreach (DishPieceView view in _activeDishViews.Values)
            {
                view?.ClearScopeGlows(channel);
            }
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
    }
}
