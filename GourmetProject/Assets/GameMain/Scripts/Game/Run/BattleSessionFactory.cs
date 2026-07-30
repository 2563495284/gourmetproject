using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.BossDebuffs;
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
        public static BattleSession Build(
            GameRun run,
            int requiredScore,
            string modifier,
            string key,
            string bossDebuffId)
        {
            modifier ??= string.Empty;
            cfg.BossDebuff bossDebuff = ResolveBossDebuff(run, bossDebuffId);
            BossDebuffModel bossDebuffModel = bossDebuff != null
                ? BossDebuffModelRegistry.Create(run, bossDebuff)
                : null;

            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            var debuffStream = GameApp.Random.DomainStream(SeedDomains.Combat, $"{key}_debuff_setup");

            var entries = new List<RecipeSlotEntry>();
            IReadOnlyList<RecipeBookSlot> recipeSlots = run.RecipeEntries;
            for (int dishIndex = 0; dishIndex < recipeSlots.Count; dishIndex++)
            {
                RecipeBookSlot recipeSlot = recipeSlots[dishIndex];
                if (run.Database.GetDish(recipeSlot.DishId) != null)
                {
                    entries.Add(new RecipeSlotEntry(
                        recipeSlot.DishId,
                        recipeSlot.ExtraFlavorIds,
                        recipeSlot.ExtraSkillIds,
                        recipeSlot.ScoreMultiplier,
                        recipeSlot.ScoreFlatBonus,
                        0,
                        dishIndex));
                }
            }
            var slots = new List<RecipeSlot> { new RecipeSlot("菜谱", entries) };

            bossDebuffModel?.ModifyRecipeSlots(slots, debuffStream);

            int recipeEntryCount = TotalRecipeEntries(slots);
            GpTable board = BuildTable(run, character, bossDebuffModel, recipeEntryCount, debuffStream);
            bossDebuffModel?.ModifyPreparedTable(board, recipeEntryCount, debuffStream);

            var battleStream = GameApp.Random.DomainStream(SeedDomains.Combat, key);

            // 结算类被动道具（逐菜/条件/顺序）作为效果来源注入结算器；局级加/乘仍走 FinalFlat/Multiplier 快路径。
            var calculator = new ScoreCalculator(effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            var session = new BattleSession(board, run.Database, battleStream, slots, requiredScore, calculator, runSettledCounts: run.RunSettledCounts);
            session.ExtraCountAsPerDish = ItemScoreEffectAdapter.ExtraCountAsPerDish(run);
            var itemRuntime = new ItemRuntime(run);
            cfg.GameBase gameBase = run.Tables.TbGameBase.Data;
            session.ConfigureFoodDiscardLimit(gameBase.FoodDeleteCount + itemRuntime.FoodDiscardLimitBonus());
            session.ConfigureRandomServeMultiplier(
                gameBase.RandomServeMultiplierMin,
                gameBase.RandomServeMultiplierMax,
                gameBase.RandomServeMultiplierStep);
            session.ConfigureCookieServePity(
                gameBase.ServeCookiePityCount,
                gameBase.ServeCookieDishIds);

            // 蛋糕层数族道具：初始层数 / 阈值下调 / 叠层加速。
            session.CakeLayerThresholdReduction = itemRuntime.CakeThresholdReduction();
            session.CakeLayerAccelBonus = itemRuntime.CakeAccelBonus();
            int initLayers = itemRuntime.CakeInitialLayers() + run.ConsumeRetainedHappyCakeLayers();
            if (initLayers > 0)
            {
                session.SeedHappyCakeLayers(initLayers);
            }

            session.SweetTransferTargetMultiplier = itemRuntime.SweetTransferTargetMultiplier();
            session.SweetTransferSourceMultiplier = itemRuntime.SweetTransferSourceMultiplier();

            bossDebuffModel?.ApplyToBattle(session);

            ApplyPassiveItems(run, session);
            return session;
        }

        public static GpTable BuildTablePreview(GameRun run, string modifier = "", string bossDebuffId = "")
        {
            cfg.Character character = run?.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            modifier ??= string.Empty;
            cfg.BossDebuff bossDebuff = ResolveBossDebuff(run, bossDebuffId);
            BossDebuffModel model = bossDebuff != null
                ? BossDebuffModelRegistry.Create(run, bossDebuff)
                : null;
            var previewStream = GameApp.Random.DomainStream(
                SeedDomains.Combat,
                $"table_preview_{bossDebuffId}_debuff_setup");
            int recipeEntryCount = run?.RecipeEntries?.Count ?? 0;
            GpTable table = BuildTable(run, character, model, recipeEntryCount, previewStream);
            model?.ModifyPreparedTable(table, recipeEntryCount, previewStream);

            return table;
        }

        /// <summary>
        /// 由角色配置构建本局餐桌：初始胃形状取自碎片库，最大包围盒取角色 max 尺寸。
        /// Boss Debuff 模型可在构建前调整最大包围盒（初始碎片超出部分自动裁掉）。
        /// </summary>
        private static GpTable BuildTable(
            GameRun run,
            cfg.Character character,
            BossDebuffModel bossDebuffModel,
            int recipeEntryCount,
            IRandomStream rng)
        {
            int maxW = character.MaxDiningTableWidth;
            int maxH = character.MaxDiningTableHeight;
            bossDebuffModel?.ModifyTableBounds(ref maxW, ref maxH);

            TableFragmentDef fragment = run.Database.GetFragment(character?.InitialFragmentId);
            if (fragment == null)
            {
                Log.Warning($"Character '{run.CharacterId}' 无有效初始餐桌碎片 '{character?.InitialFragmentId}'，回退为满 {maxW}x{maxH} 餐桌。", "GameRun");
                GpTable fallback = new GpTable(maxW, maxH);
                bossDebuffModel?.ModifyBuiltTable(fallback, recipeEntryCount, rng);
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
            bossDebuffModel?.ModifyBuiltTable(board, recipeEntryCount, rng);
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

        private static cfg.BossDebuff ResolveBossDebuff(GameRun run, string bossDebuffId)
        {
            if (run?.Tables?.TbBossDebuff == null || string.IsNullOrEmpty(bossDebuffId))
            {
                return null;
            }

            return run.Tables.TbBossDebuff.GetOrDefault(bossDebuffId);
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
