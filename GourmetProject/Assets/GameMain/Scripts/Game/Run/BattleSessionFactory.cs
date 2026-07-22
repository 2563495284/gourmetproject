using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 根据局外 Run 状态构建一场局内战斗，避免 GameRun 同时承担战斗装配细节。
    /// </summary>
    public static class BattleSessionFactory
    {
        public static BattleSession Build(GameRun run, int requiredScore, string modifier, string key)
        {
            modifier ??= string.Empty;
            requiredScore = ApplyRequiredScoreModifier(requiredScore, modifier);

            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            var debuffStream = GameApp.Random.DomainStream(SeedDomains.Combat, $"{key}_debuff_setup");

            int recipeBookCount = run.RecipeBookCount;
            var slots = new List<RecipeSlot>(recipeBookCount);
            for (int i = 0; i < recipeBookCount; i++)
            {
                var entries = new List<RecipeSlotEntry>();
                IReadOnlyList<RecipeBookSlot> bookSlots = run.GetRecipeBookEntries(i);
                for (int dishIndex = 0; dishIndex < bookSlots.Count; dishIndex++)
                {
                    RecipeBookSlot bookSlot = bookSlots[dishIndex];
                    if (run.Database.GetDish(bookSlot.DishId) != null)
                    {
                        entries.Add(new RecipeSlotEntry(
                            bookSlot.DishId,
                            bookSlot.ExtraFlavorIds,
                            bookSlot.ExtraSkillIds,
                            bookSlot.ScoreMultiplier,
                            bookSlot.ScoreFlatBonus,
                            i,
                            dishIndex));
                    }
                }

                slots.Add(new RecipeSlot($"菜谱{i + 1}", entries));
            }

            ApplyRecipeModifiers(slots, run, modifier, debuffStream);

            GpTable board = BuildTable(run, character, modifier);
            ApplyTableModifiers(board, TotalRecipeEntries(slots), modifier, debuffStream);

            var battleStream = GameApp.Random.DomainStream(SeedDomains.Combat, key);

            // 结算类被动道具（逐菜/条件/顺序）作为效果来源注入结算器；局级加/乘仍走 FinalFlat/Multiplier 快路径。
            var calculator = new ScoreCalculator(effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            var session = new BattleSession(board, run.Database, battleStream, slots, requiredScore, calculator, runSettledCounts: run.RunSettledCounts);
            session.ExtraCountAsPerDish = ItemScoreEffectAdapter.ExtraCountAsPerDish(run);
            cfg.GameBase gameBase = run.Tables.TbGameBase.Data;
            session.ConfigureRandomServeMultiplier(
                gameBase.RandomServeMultiplierMin,
                gameBase.RandomServeMultiplierMax,
                gameBase.RandomServeMultiplierStep);

            // 蛋糕层数族道具：初始层数 / 阈值下调 / 叠层加速。
            var itemRuntime = new ItemRuntime(run);
            session.CakeLayerThresholdReduction = itemRuntime.CakeThresholdReduction();
            session.CakeLayerAccelBonus = itemRuntime.CakeAccelBonus();
            int initLayers = itemRuntime.CakeInitialLayers() + run.ConsumeRetainedHappyCakeLayers();
            if (initLayers > 0)
            {
                session.SeedHappyCakeLayers(initLayers);
            }

            session.SweetTransferTargetMultiplier = itemRuntime.SweetTransferTargetMultiplier();
            session.SweetTransferSourceMultiplier = itemRuntime.SweetTransferSourceMultiplier();

            ApplySessionModifiers(session, modifier);

            ApplyPassiveItems(run, session);
            return session;
        }

        public static GpTable BuildTablePreview(GameRun run, string modifier = "")
        {
            cfg.Character character = run?.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            return BuildTable(run, character, modifier ?? string.Empty);
        }

        /// <summary>
        /// 由角色配置构建本局餐桌：初始胃形状取自碎片库，最大包围盒取角色 max 尺寸。
        /// Boss「small_board」修正收缩最大包围盒（初始碎片超出部分自动裁掉）。
        /// </summary>
        private static GpTable BuildTable(GameRun run, cfg.Character character, string modifier)
        {
            int maxW = character.MaxDiningTableWidth;
            int maxH = character.MaxDiningTableHeight;

            if (BossDebuffModifiers.IsSmallBoard(modifier))
            {
                maxW = System.Math.Min(maxW, 3);
                maxH = System.Math.Min(maxH, 3);
            }
            else if (modifier == BossDebuffModifiers.Indulgent)
            {
                maxW += 1;
                maxH += 1;
            }

            TableFragmentDef fragment = run.Database.GetFragment(character?.InitialFragmentId);
            if (fragment == null)
            {
                Log.Warning($"Character '{run.CharacterId}' 无有效初始餐桌碎片 '{character?.InitialFragmentId}'，回退为满 {maxW}x{maxH} 餐桌。", "GameRun");
                GpTable fallback = new GpTable(maxW, maxH);
                ApplyShapeModifier(fallback, modifier);
                ApplyCellMaterialOverrides(fallback, run);
                return fallback;
            }

            // 统一造盘：用更大的隐藏画布承载局部坐标，maxW/maxH 只限制最终胃形局部包围框。
            int canvasW = maxW * 3;
            int canvasH = maxH * 3;
            GridPos localOrigin = TableFragmentBuilder.CenteredOrigin(fragment, maxW, maxH);
            var initialOrigin = new GridPos(localOrigin.X + maxW, localOrigin.Y + maxH);
            GpTable board = TableFragmentBuilder.BuildFromExpandedLocalBounds(
                fragment,
                GetAcquiredFragments(run),
                run.FragmentPlacements,
                run.GetTableFragmentDef,
                maxW,
                maxH,
                canvasW,
                canvasH,
                initialOrigin);
            ApplyShapeModifier(board, modifier);
            ApplyCellMaterialOverrides(board, run);
            return board;
        }

        /// <summary>把玩家用「铺台小票」永久附加的格子材质叠加进餐桌（拼桌后统一 merge，战斗与预览一致）。</summary>
        private static void ApplyCellMaterialOverrides(GpTable board, GameRun run)
        {
            if (board == null || run == null)
            {
                return;
            }

            foreach (CellMaterialOverride m in run.CellMaterialOverrides)
            {
                board.AddMaterialAt(m.Pos, m.MaterialId);
            }
        }

        private static int ApplyRequiredScoreModifier(int requiredScore, string modifier)
        {
            float multiplier = 1f;
            switch (modifier)
            {
                case BossDebuffModifiers.Indulgent:
                    multiplier = 1.5f;
                    break;
                case BossDebuffModifiers.KidsMeal:
                    multiplier = 0.8f;
                    break;
                case BossDebuffModifiers.Gluttony:
                    multiplier = 1.1f;
                    break;
            }

            return (int)System.Math.Round(requiredScore * multiplier, System.MidpointRounding.AwayFromZero);
        }

        private static void ApplyRecipeModifiers(List<RecipeSlot> slots, GameRun run, string modifier, IRandomStream rng)
        {
            if (modifier == BossDebuffModifiers.Gluttony)
            {
                foreach (RecipeSlot slot in slots)
                {
                    var copies = new List<RecipeSlotEntry>();
                    foreach (RecipeSlotEntry entry in slot.Entries)
                    {
                        copies.Add(entry.Clone());
                    }

                    foreach (RecipeSlotEntry copy in copies)
                    {
                        slot.AddEntry(copy);
                    }
                }
            }
            else if (modifier == BossDebuffModifiers.Omakase)
            {
                var all = new List<RecipeSlotEntry>();
                foreach (RecipeSlot slot in slots)
                {
                    foreach (RecipeSlotEntry entry in slot.Entries)
                    {
                        all.Add(entry.Clone());
                    }
                }

                rng.Shuffle(all);
                foreach (RecipeSlot slot in slots)
                {
                    slot.ReplaceEntries(System.Array.Empty<RecipeSlotEntry>());
                }

                for (int i = 0; i < all.Count; i++)
                {
                    slots[i % slots.Count].AddEntry(all[i]);
                }
            }
            else if (modifier == BossDebuffModifiers.LightMeal)
            {
                MarkRandomRecipeEntries(slots, System.Math.Max(1, TotalRecipeEntries(slots) / 8 + 1), rng, disableSkills: true);
            }
            else if (modifier == BossDebuffModifiers.VeganMeal)
            {
                MarkRandomRecipeEntries(slots, System.Math.Max(1, TotalRecipeEntries(slots) / 8), rng, excludeFromScore: true);
            }
        }

        private static void MarkRandomRecipeEntries(
            List<RecipeSlot> slots,
            int count,
            IRandomStream rng,
            bool disableSkills = false,
            bool excludeFromScore = false)
        {
            if (count <= 0)
            {
                return;
            }

            var entries = new List<RecipeSlotEntry>();
            foreach (RecipeSlot slot in slots)
            {
                entries.AddRange(slot.Entries);
            }

            rng.Shuffle(entries);
            int take = System.Math.Min(count, entries.Count);
            for (int i = 0; i < take; i++)
            {
                if (disableSkills)
                {
                    entries[i].MarkSkillsDisabled();
                }

                if (excludeFromScore)
                {
                    entries[i].MarkExcludedFromScore();
                }
            }
        }

        private static int TotalRecipeEntries(List<RecipeSlot> slots)
        {
            int total = 0;
            foreach (RecipeSlot slot in slots)
            {
                total += slot.Count;
            }

            return total;
        }

        private static void ApplyShapeModifier(GpTable board, string modifier)
        {
            if (board == null)
            {
                return;
            }

            if (modifier == BossDebuffModifiers.Indulgent)
            {
                AddBottomAndRightCells(board);
            }
            else if (modifier == BossDebuffModifiers.KidsMeal)
            {
                RemoveBottomAndRightCells(board);
            }
        }

        private static void AddBottomAndRightCells(GpTable board)
        {
            List<GridPos> cells = board.ExistingCells();
            var toAdd = new List<GridPos>();
            for (int x = 0; x < board.Width; x++)
            {
                int maxY = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.X == x && cell.Y > maxY)
                    {
                        maxY = cell.Y;
                    }
                }

                if (maxY >= 0)
                {
                    toAdd.Add(new GridPos(x, maxY + 1));
                }
            }

            for (int y = 0; y < board.Height; y++)
            {
                int maxX = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.Y == y && cell.X > maxX)
                    {
                        maxX = cell.X;
                    }
                }

                if (maxX >= 0)
                {
                    toAdd.Add(new GridPos(maxX + 1, y));
                }
            }

            foreach (GridPos cell in toAdd)
            {
                board.SetExists(cell, true);
            }
        }

        private static void RemoveBottomAndRightCells(GpTable board)
        {
            List<GridPos> cells = board.ExistingCells();
            var toRemove = new HashSet<GridPos>();
            for (int x = 0; x < board.Width; x++)
            {
                int maxY = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.X == x && cell.Y > maxY)
                    {
                        maxY = cell.Y;
                    }
                }

                if (maxY >= 0)
                {
                    toRemove.Add(new GridPos(x, maxY));
                }
            }

            for (int y = 0; y < board.Height; y++)
            {
                int maxX = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.Y == y && cell.X > maxX)
                    {
                        maxX = cell.X;
                    }
                }

                if (maxX >= 0)
                {
                    toRemove.Add(new GridPos(maxX, y));
                }
            }

            foreach (GridPos cell in toRemove)
            {
                board.SetExists(cell, false);
            }
        }

        private static void ApplyTableModifiers(GpTable board, int foodCount, string modifier, IRandomStream rng)
        {
            if (board == null || modifier != BossDebuffModifiers.Vegetarian)
            {
                return;
            }

            int disableCount = foodCount / 12 + 1;
            List<GridPos> cells = board.ExistingCells();
            rng.Shuffle(cells);
            int disabled = 0;
            foreach (GridPos cell in cells)
            {
                if (!board.IsEmpty(cell))
                {
                    continue;
                }

                board.SetDisabled(cell, true);
                disabled++;
                if (disabled >= disableCount)
                {
                    break;
                }
            }
        }

        private static void ApplySessionModifiers(BattleSession session, string modifier)
        {
            if (session == null)
            {
                return;
            }

            switch (modifier)
            {
                case BossDebuffModifiers.LegacyLimitServe:
                    session.MaxServes = 5;
                    break;
                case BossDebuffModifiers.DineAndDash:
                    session.GoldCostPerServe = 5;
                    break;
                case BossDebuffModifiers.FineDining:
                    session.HalveBaseScore = true;
                    break;
                case BossDebuffModifiers.DarkCuisine:
                    session.RandomServeMultiplier = true;
                    break;
                case BossDebuffModifiers.ComboMeal:
                    session.AutoServeSecondDish = true;
                    break;
                case BossDebuffModifiers.LateNight:
                    session.ReverseSettlementOrder = true;
                    break;
                case BossDebuffModifiers.Appetizer:
                    session.RemoveFirstServedDishes = true;
                    session.FirstServedDishesToRemove = 3;
                    break;
                case BossDebuffModifiers.Tasting:
                    session.AlternateServeMultiplier = true;
                    break;
                case BossDebuffModifiers.Buffet:
                    session.MinimumServesForScore = 10;
                    break;
            }
        }

        private static List<TableFragmentDef> GetAcquiredFragments(GameRun run)
        {
            var fragments = new List<TableFragmentDef>(run.TableFragmentIds.Count);
            foreach (string fragmentId in run.TableFragmentIds)
            {
                TableFragmentDef fragment = run.GetTableFragmentDef(fragmentId);
                if (fragment != null)
                {
                    fragments.Add(fragment);
                }
            }

            return fragments;
        }

        /// <summary>把被动道具局级修正注入战斗会话（各模型 ApplyToBattle）。</summary>
        private static void ApplyPassiveItems(GameRun run, BattleSession session)
        {
            foreach (GourmetProject.Game.Meta.Passives.PassiveItemModel model in run.PassiveModels)
            {
                model.ApplyToBattle(session);
            }
        }
    }
}
