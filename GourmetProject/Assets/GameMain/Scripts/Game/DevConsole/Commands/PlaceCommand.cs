using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>
    /// 把出餐口食物随机摆到餐桌，直到当前食物放不下或出餐口没有食物。
    /// 出餐口抽菜仍走 <see cref="BattleSession.PrepareServeAutomatically"/>，摆位使用独立随机流。
    /// </summary>
    public sealed class PlaceCommand : ConsoleCommand
    {
        private const string PlacementStreamKey = "gm_place";
        private const int MinimumIterationGuard = 64;
        private const int IterationGuardPerCell = 8;

        public override string CmdName => "place";

        public override string Args => string.Empty;

        public override string Description => "随机把出餐口食物摆到餐桌，直到放不下或出餐口没有食物。";

        public override CmdResult Execute(string[] args)
        {
            BattleForm battle = BattleForm.Active;
            BattleSession session = battle?.Session;
            if (battle == null || session == null || !battle.InBattle)
            {
                return CmdResult.Fail("需要在经营挑战中使用。");
            }

            if (battle.CurrentView != GameplayView.Food)
            {
                return CmdResult.Fail("请先进入经营挑战餐桌界面。");
            }

            if (session.IsSettled)
            {
                return CmdResult.Fail("当前经营挑战已经结算。");
            }

            BattleWorldController world = battle.ActiveWorld ?? BattleWorldController.Instance;
            if (world != null && world.IsFoodInteractionBusy)
            {
                return CmdResult.Fail("当前正在演出或拖拽，请结束后再摆放。");
            }

            RandomPlaceResult result = Fill(session, CreatePlacementRandom());
            battle.RefreshAfterActiveItem(boardChanged: true);
            return CmdResult.Ok(FormatMessage(result));
        }

        internal static RandomPlaceResult Fill(BattleSession session, IRandomStream placementRandom)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (placementRandom == null)
            {
                throw new ArgumentNullException(nameof(placementRandom));
            }

            var result = new RandomPlaceResult();
            if (session.IsSettled)
            {
                result.StopReason = RandomPlaceStopReason.Settled;
                return result;
            }

            int maxIterations = Math.Max(
                MinimumIterationGuard,
                session.DiningTable.CellCapacity * IterationGuardPerCell);
            for (int i = 0; i < maxIterations; i++)
            {
                if (session.HasPendingTablePlacements)
                {
                    IReadOnlyList<PendingDishConfirmResult> confirmed = session.ConfirmAllPendingTableDishes();
                    result.ConfirmedPendingCount += confirmed.Count;
                }

                if (session.PreparedServe == null)
                {
                    ServePrepareResult prepared = session.PrepareServeAutomatically(0);
                    result.LastPrepareOutcome = prepared.Outcome;
                    if (!prepared.Success)
                    {
                        result.StopReason = RandomPlaceStopReason.OutletEmpty;
                        return result;
                    }
                }

                IReadOnlyList<Placement> placements = session.FindPreparedServePlacements();
                if (placements == null || placements.Count == 0)
                {
                    result.StopReason = RandomPlaceStopReason.CannotPlace;
                    result.OutletHasUnplaceableDish = session.PreparedServe != null;
                    return result;
                }

                Placement chosen = placementRandom.Pick(placements);
                ServeResult placed = session.CommitPreparedServe(chosen);
                if (!placed.Success)
                {
                    result.StopReason = RandomPlaceStopReason.CommitFailed;
                    result.LastServeOutcome = placed.Outcome;
                    return result;
                }

                result.PlacedCount++;
                result.LastServeOutcome = placed.Outcome;
            }

            result.StopReason = RandomPlaceStopReason.IterationLimit;
            result.OutletHasUnplaceableDish = session.PreparedServe != null;
            return result;
        }

        private static IRandomStream CreatePlacementRandom()
        {
            return GameApp.Random != null && GameApp.Random.IsInitialized
                ? GameApp.Random.Cosmetic(PlacementStreamKey)
                : new Xoshiro256SS(1UL);
        }

        private static string FormatMessage(RandomPlaceResult result)
        {
            string placed = result.PlacedCount > 0
                ? $"已随机摆放 {result.PlacedCount} 道菜"
                : "没有新摆放的食物";
            switch (result.StopReason)
            {
                case RandomPlaceStopReason.CannotPlace:
                    return $"{placed}。出餐口食物当前无法摆放。";
                case RandomPlaceStopReason.OutletEmpty:
                    return $"{placed}。出餐口已无食物。";
                case RandomPlaceStopReason.CommitFailed:
                    return $"{placed}。确认摆放失败。";
                case RandomPlaceStopReason.IterationLimit:
                    return $"{placed}。达到摆放次数上限。";
                case RandomPlaceStopReason.Settled:
                    return "当前经营挑战已经结算。";
                default:
                    return placed + "。";
            }
        }
    }

    internal enum RandomPlaceStopReason
    {
        None,
        OutletEmpty,
        CannotPlace,
        CommitFailed,
        Settled,
        IterationLimit,
    }

    internal sealed class RandomPlaceResult
    {
        public int PlacedCount;
        public int ConfirmedPendingCount;
        public bool OutletHasUnplaceableDish;
        public RandomPlaceStopReason StopReason;
        public ServePrepareOutcome LastPrepareOutcome;
        public ServeOutcome LastServeOutcome;
    }
}
