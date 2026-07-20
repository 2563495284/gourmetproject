#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>复制当前战斗的棋盘、结算顺序和明细，供定位演出顺序问题。</summary>
    public sealed class BoardCopyCommand : ConsoleCommand
    {
        public override string CmdName => "boardcopy";

        public override string Args => string.Empty;

        public override string Description => "复制当前棋盘与结算顺序调试数据到剪贴板。";

        public override CmdResult Execute(string[] args)
        {
            BattleSession session = BattleForm.Active?.Session;
            if (session?.DiningTable == null || session.Database == null)
            {
                return CmdResult.Fail("当前没有可导出的美食战斗棋盘。");
            }

            ScoreResult result = session.LastResult ?? session.PreviewScore();
            string snapshot = BuildSnapshot(session, result);
            GUIUtility.systemCopyBuffer = snapshot;
            return CmdResult.Ok(
                $"已复制棋盘调试数据：{session.DiningTable.DishCount} 道菜，"
                + $"{result.ScoreLines.Count} 条 ScoreLine，"
                + $"{result.ScoreEvents.Count} 条 ScoreEvent。");
        }

        private static string BuildSnapshot(BattleSession session, ScoreResult result)
        {
            DiningTable board = session.DiningTable;
            Gameplay.Data.GameplayDatabase db = session.Database;
            var sb = new StringBuilder(8192);

            sb.AppendLine("GOURMET_BATTLE_DEBUG_V1");
            sb.Append("isSettled=").Append(session.IsSettled)
                .Append(" reverseDishOrder=").Append(session.ReverseSettlementOrder)
                .Append(" board=").Append(board.Width).Append('x').Append(board.Height)
                .Append(" capacity=").Append(board.CellCapacity)
                .Append(" empty=").Append(board.EmptyCellCount)
                .Append(" dishes=").Append(board.DishCount)
                .AppendLine();

            AppendGrid(sb, board);

            sb.AppendLine("EXPECTED_SETTLEMENT_ORDER");
            IEnumerable<DishInstance> expectedOrder = session.ReverseSettlementOrder
                ? board.Dishes
                    .Where(d => !d.ExcludedFromScore)
                    .OrderByDescending(d => ScoreSnapshot.SettlementLayerOf(d, db))
                    .ThenByDescending(d => ScoreSnapshot.SettlementBoardOrderOf(d, board.Width))
                    .ThenBy(d => d.Id)
                : board.Dishes
                    .Where(d => !d.ExcludedFromScore)
                    .OrderByDescending(d => ScoreSnapshot.SettlementLayerOf(d, db))
                    .ThenBy(d => ScoreSnapshot.SettlementBoardOrderOf(d, board.Width))
                    .ThenBy(d => d.Id);
            int expectedIndex = 0;
            foreach (DishInstance dish in expectedOrder)
            {
                sb.Append('[').Append(expectedIndex++).Append("] ");
                AppendDishIdentity(sb, dish, db);
                sb.AppendLine();
            }

            sb.AppendLine("BOARD_DISH_LIST");
            for (int i = 0; i < board.Dishes.Count; i++)
            {
                DishInstance dish = board.Dishes[i];
                sb.Append('[').Append(i).Append("] ");
                AppendDishIdentity(sb, dish, db);
                sb.Append(" occupied=").Append(Cells(dish.OccupiedCells))
                    .Append(" sourceRecipe=").Append(dish.SourceSlotIndex).Append('/').Append(dish.SourceDishIndex)
                    .Append(" disabled=").Append(dish.SkillsDisabled)
                    .Append(" excluded=").Append(dish.ExcludedFromScore)
                    .Append(" temporary=").Append(dish.IsTemporary)
                    .Append(" base=").Append(Number(dish.BaseScoreBeforeSettlement))
                    .Append(" mult=").Append(Number(dish.BaseMultiplierBeforeSettlement))
                    .Append(" flavors=[").Append(string.Join(",", dish.FlavorIds)).Append(']')
                    .AppendLine();

                AppendDishSkills(sb, dish, db);
            }

            sb.AppendLine("DISH_SCORES");
            for (int i = 0; i < result.DishScores.Count; i++)
            {
                DishScore score = result.DishScores[i];
                sb.Append('[').Append(i).Append("] instance=").Append(score.DishInstanceId)
                    .Append(" dish=").Append(score.DishId)
                    .Append(" base=").Append(Number(score.BaseValue))
                    .Append(" flat=").Append(Number(score.FlatBonus))
                    .Append(" mult=").Append(Number(score.Multiplier))
                    .Append(" contribution=").Append(Number(score.Contribution))
                    .AppendLine();
            }

            sb.AppendLine("SCORE_LINES");
            for (int i = 0; i < result.ScoreLines.Count; i++)
            {
                ScoreLine line = result.ScoreLines[i];
                sb.Append('[').Append(i).Append("] phase=").Append(line.Phase)
                    .Append(" kind=").Append(line.Kind)
                    .Append(" instance=").Append(line.DishInstanceId)
                    .Append(" dish=").Append(line.DishId)
                    .Append(" value=").Append(Number(line.Value))
                    .Append(" before=").Append(Number(line.Before))
                    .Append(" after=").Append(Number(line.After));
                AppendSource(sb, line.Source);
                AppendTrace(sb, line.Trace);
                sb.Append(" message=").Append(line.Message).AppendLine();
            }

            sb.AppendLine("SCORE_EVENTS");
            for (int i = 0; i < result.ScoreEvents.Count; i++)
            {
                ScoreEvent scoreEvent = result.ScoreEvents[i];
                sb.Append('[').Append(i).Append("] type=").Append(scoreEvent.Type)
                    .Append(" phase=").Append(scoreEvent.Phase)
                    .Append(" instance=").Append(scoreEvent.DishInstanceId)
                    .Append(" dish=").Append(scoreEvent.DishId);
                AppendSource(sb, scoreEvent.Source);
                AppendTrace(sb, scoreEvent.Trace);
                sb.Append(" message=").Append(scoreEvent.Message).AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendGrid(StringBuilder sb, DiningTable board)
        {
            sb.AppendLine("GRID (#=不存在 X=禁用 .=空 其他=实例ID)");
            for (int y = 0; y < board.Height; y++)
            {
                sb.Append("y=").Append(y).Append(' ');
                for (int x = 0; x < board.Width; x++)
                {
                    var cell = new GridPos(x, y);
                    string token;
                    if (!board.Exists(cell))
                    {
                        token = "#";
                    }
                    else if (board.IsDisabled(cell))
                    {
                        token = "X";
                    }
                    else
                    {
                        DishInstance dish = board.DishAt(cell);
                        token = dish != null ? dish.Id.ToString(CultureInfo.InvariantCulture) : ".";
                    }

                    sb.Append(token.PadLeft(3));
                }

                sb.AppendLine();
            }

            sb.AppendLine("CELL_MATERIALS");
            foreach (GridPos cell in board.ExistingCells())
            {
                IReadOnlyList<string> materials = board.MaterialsAt(cell);
                if (materials.Count > 0)
                {
                    sb.Append(Cell(cell)).Append("=[").Append(string.Join(",", materials)).AppendLine("]");
                }
            }
        }

        private static void AppendDishIdentity(
            StringBuilder sb,
            DishInstance dish,
            Gameplay.Data.GameplayDatabase db)
        {
            sb.Append("instance=").Append(dish.Id)
                .Append(" dish=").Append(dish.Def.Id)
                .Append(" name=").Append(dish.Def.Name)
                .Append(" layer=").Append(ScoreSnapshot.SettlementLayerOf(dish, db))
                .Append(" origin=").Append(Cell(dish.Placement.Origin))
                .Append(" rotation=").Append(dish.Placement.RotationIndex);
        }

        private static void AppendDishSkills(
            StringBuilder sb,
            DishInstance dish,
            Gameplay.Data.GameplayDatabase db)
        {
            for (int skillIndex = 0; skillIndex < dish.SkillIds.Count; skillIndex++)
            {
                string skillId = dish.SkillIds[skillIndex];
                SkillDef skill = db.GetSkill(skillId);
                sb.Append("  skill[").Append(skillIndex).Append("] id=").Append(skillId)
                    .Append(" name=").Append(skill?.Name)
                    .Append(" source=").Append(dish.GetSkillSource(skillId))
                    .AppendLine();
                if (skill == null)
                {
                    continue;
                }

                foreach (SkillRuleDef rule in skill.Rules)
                {
                    AppendRule(sb, "    rule", rule);
                }
            }

            for (int i = 0; i < dish.TransferredSkills.Count; i++)
            {
                TransferredSkill transferred = dish.TransferredSkills[i];
                sb.Append("  transferred[").Append(i).Append("] sourceInstance=")
                    .Append(transferred.SourceInstanceId)
                    .Append(" source=").Append(transferred.SourceLabel)
                    .AppendLine();
                AppendRule(sb, "    rule", transferred.Rule);
            }
        }

        private static void AppendRule(StringBuilder sb, string prefix, SkillRuleDef rule)
        {
            if (rule == null)
            {
                sb.Append(prefix).AppendLine("=<null>");
                return;
            }

            sb.Append(prefix)
                .Append(" id=").Append(rule.Id)
                .Append(" order=").Append(rule.Order)
                .Append(" trigger=").Append(rule.Trigger)
                .Append(" condition=").Append(rule.CondType)
                .Append('/').Append(rule.CondScope)
                .Append('/').Append(rule.CondUnit)
                .Append('/').Append(rule.CondMode)
                .Append(" condParam=").Append(rule.CondParam)
                .Append(" action=").Append(rule.ActionType)
                .Append('/').Append(rule.ActionScope)
                .Append(" count=").Append(rule.ActionCount)
                .Append(" values=[").Append(string.Join(",", rule.ActionValues.Select(Number))).Append(']')
                .Append(" params=[").Append(string.Join(",", rule.ActionParams)).Append(']')
                .AppendLine();
        }

        private static void AppendSource(StringBuilder sb, ScoreSource source)
        {
            if (source == null)
            {
                sb.Append(" source=<null>");
                return;
            }

            sb.Append(" source=").Append(source.Type)
                .Append('/').Append(source.Id)
                .Append('/').Append(source.Name)
                .Append(" sourceInstance=").Append(source.DishInstanceId);
        }

        private static void AppendTrace(StringBuilder sb, SkillExecutionTrace trace)
        {
            if (trace == null)
            {
                return;
            }

            sb.Append(" trace=")
                .Append(trace.Kind)
                .Append(" owner=").Append(trace.OwnerDishInstanceId)
                .Append('/').Append(trace.OwnerDishName)
                .Append(" runtimeSelf=").Append(trace.RuntimeSelfDishInstanceId)
                .Append('/').Append(trace.RuntimeSelfDishName)
                .Append(" skill=").Append(trace.SkillId)
                .Append(" rule=").Append(trace.RuleId)
                .Append('#').Append(trace.RuleOrder)
                .Append(" trigger=").Append(trace.Trigger)
                .Append(" condition=").Append(trace.ConditionType)
                .Append('/').Append(trace.ConditionScope)
                .Append(" action=").Append(trace.ActionType)
                .Append('/').Append(trace.ActionScope)
                .Append(" targets=[").Append(string.Join(",", trace.VisualTargetDishInstanceIds)).Append(']');
        }

        private static string Cells(IEnumerable<GridPos> cells)
            => "[" + string.Join(",", cells.Select(Cell)) + "]";

        private static string Cell(GridPos cell)
            => $"({cell.X},{cell.Y})";

        private static string Number(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
#endif
