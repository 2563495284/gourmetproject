using System.Collections.Generic;
using System.Linq;
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
    /// 根据局外 Run 状态构建一场局内经营挑战，避免 GameRun 同时承担经营挑战装配细节。
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
            var debuffStream = run.Random.DomainStream(SeedDomains.Combat, $"{key}_debuff_setup");

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
            var slots = new List<RecipeSlot> { new RecipeSlot("食谱", entries) };

            BossDebuffPresentationPlan presentation = CreatePresentationPlan(bossDebuff, slots);

            bossDebuffModel?.ModifyRecipeSlots(slots, debuffStream);
            CaptureRecipePresentation(presentation, slots);

            int recipeEntryCount = TotalRecipeEntries(slots);
            GpTable board = BuildTable(
                run,
                character,
                bossDebuffModel,
                recipeEntryCount,
                debuffStream,
                presentation);
            HashSet<GridPos> disabledBefore = CaptureDisabledCells(board);
            bossDebuffModel?.ModifyPreparedTable(board, recipeEntryCount, debuffStream);
            CaptureDisabledPresentation(presentation, board, disabledBefore);

            var battleStream = run.Random.DomainStream(SeedDomains.Combat, key);

            // 结算类装饰品（逐菜/条件/顺序）作为效果来源注入结算器。
            // FinalFlat/Multiplier 目前只保留存档和结算器兼容入口，没有现行配置产出。
            var calculator = new ScoreCalculator(effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            var session = new BattleSession(board, run.Database, battleStream, slots, requiredScore, calculator, runSettledCounts: run.RunSettledCounts);
            session.AttachBossDebuffPresentation(presentation);
            session.ExtraCountAsPerDish = ItemScoreEffectAdapter.ExtraCountAsPerDish(run);
            session.PassiveItemCount = run.PassiveItemStates.Count();
            var itemRuntime = new ItemRuntime(run);
            cfg.GameBase gameBase = run.Tables.TbGameBase.Data;
            session.ConfigureFoodDiscardLimit(itemRuntime.FoodDiscardCapacity());
            session.ConfigureRandomServeMultiplier(
                gameBase.RandomServeMultiplierMin,
                gameBase.RandomServeMultiplierMax,
                gameBase.RandomServeMultiplierStep);
            session.ConfigureCookieServePity(
                gameBase.ServeCookiePityCount,
                gameBase.ServeCookieDishIds);

            // 蛋糕层数族装饰品和消耗品：初始层数 / 阈值下调 / 叠层加速。
            session.CakeLayerThresholdReduction = itemRuntime.CakeThresholdReduction();
            session.CakeLayerAccelBonus = itemRuntime.CakeAccelBonus();
            int initLayers = itemRuntime.CakeInitialLayers() + run.ConsumeRetainedHappyCakeLayers();
            if (initLayers > 0)
            {
                session.SeedHappyCakeLayers(initLayers);
            }

            session.SweetTransferTargetMultiplier = itemRuntime.SweetTransferTargetMultiplier();
            session.SweetTransferSourceMultiplier = itemRuntime.SweetTransferSourceMultiplier();
            session.SweetTransferExtraTargetCount = itemRuntime.SweetTransferExtraTargetCount();

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
            IRandomStream previewStream = run?.Random != null && run.Random.IsInitialized
                ? run.Random.DomainStream(SeedDomains.Combat, $"table_preview_{bossDebuffId}_debuff_setup")
                : new Xoshiro256SS(StablePreviewSeed(run, bossDebuffId));
            int recipeEntryCount = run?.RecipeEntries?.Count ?? 0;
            GpTable table = BuildTable(run, character, model, recipeEntryCount, previewStream);
            model?.ModifyPreparedTable(table, recipeEntryCount, previewStream);

            return table;
        }

        private static ulong StablePreviewSeed(GameRun run, string bossDebuffId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                string text = $"{run?.CharacterId}|{run?.WeekIndex}|{bossDebuffId}";
                for (int i = 0; i < text.Length; i++) { hash ^= text[i]; hash *= 1099511628211UL; }
                return hash;
            }
        }

        /// <summary>
        /// 数值实验室入口：对已按快照摆好的餐桌装配正式装饰品和消耗品、蛋糕与 Boss 结算规则。
        /// 随机流由调用方注入，不读取或推进全局随机状态。
        /// </summary>
        public static BattleSession BuildBalancePreview(
            GameRun run,
            GpTable populatedBoard,
            int requiredScore,
            string bossDebuffId,
            IRandomStream random,
            int initialHappyCakeLayers)
        {
            if (run == null) throw new System.ArgumentNullException(nameof(run));
            if (populatedBoard == null) throw new System.ArgumentNullException(nameof(populatedBoard));
            if (random == null) throw new System.ArgumentNullException(nameof(random));

            var calculator = new ScoreCalculator(effectSources: ItemScoreEffectAdapter.BuildScoreSources(run));
            var session = new BattleSession(
                populatedBoard,
                run.Database,
                random,
                System.Array.Empty<RecipeSlot>(),
                requiredScore,
                calculator,
                run.RunSettledCounts);
            var itemRuntime = new ItemRuntime(run);
            session.ExtraCountAsPerDish = ItemScoreEffectAdapter.ExtraCountAsPerDish(run);
            session.PassiveItemCount = run.PassiveItemStates.Count();
            session.CakeLayerThresholdReduction = itemRuntime.CakeThresholdReduction();
            session.CakeLayerAccelBonus = itemRuntime.CakeAccelBonus();
            session.SweetTransferTargetMultiplier = itemRuntime.SweetTransferTargetMultiplier();
            session.SweetTransferSourceMultiplier = itemRuntime.SweetTransferSourceMultiplier();
            session.SweetTransferExtraTargetCount = itemRuntime.SweetTransferExtraTargetCount();
            session.ConfigureFoodDiscardLimit(itemRuntime.FoodDiscardCapacity());
            session.SeedHappyCakeLayers(System.Math.Max(0, initialHappyCakeLayers + itemRuntime.CakeInitialLayers()));

            cfg.BossDebuff bossDebuff = ResolveBossDebuff(run, bossDebuffId);
            BossDebuffModel bossModel = bossDebuff != null ? BossDebuffModelRegistry.Create(run, bossDebuff) : null;
            bossModel?.ApplyToBattle(session);
            ApplyPassiveItems(run, session);
            return session;
        }

        /// <summary>数值实验室专用造盘入口；显式随机流保证不污染正式运行。</summary>
        public static GpTable BuildTablePreviewForBalance(GameRun run, string bossDebuffId, IRandomStream random)
        {
            if (run == null) throw new System.ArgumentNullException(nameof(run));
            if (random == null) throw new System.ArgumentNullException(nameof(random));
            cfg.Character character = run.Tables.TbCharacter.GetOrDefault(run.CharacterId);
            cfg.BossDebuff bossDebuff = ResolveBossDebuff(run, bossDebuffId);
            BossDebuffModel model = bossDebuff != null ? BossDebuffModelRegistry.Create(run, bossDebuff) : null;
            int recipeEntryCount = run.RecipeEntries?.Count ?? 0;
            GpTable table = BuildTable(run, character, model, recipeEntryCount, random);
            model?.ModifyPreparedTable(table, recipeEntryCount, random);
            return table;
        }

        /// <summary>
        /// 由经营方向配置构建本局餐桌：初始胃形状取自碎片库，最大包围盒取经营方向 max 尺寸。
        /// Boss Debuff 模型可在构建前调整最大包围盒（初始碎片超出部分自动裁掉）。
        /// </summary>
        private static GpTable BuildTable(
            GameRun run,
            cfg.Character character,
            BossDebuffModel bossDebuffModel,
            int recipeEntryCount,
            IRandomStream rng,
            BossDebuffPresentationPlan presentation = null)
        {
            int maxW = character.MaxDiningTableWidth;
            int maxH = character.MaxDiningTableHeight;
            bossDebuffModel?.ModifyTableBounds(ref maxW, ref maxH);

            TableFragmentDef fragment = run.Database.GetFragment(character?.InitialFragmentId);
            if (fragment == null)
            {
                Log.Warning($"Character '{run.CharacterId}' 无有效初始餐桌格 '{character?.InitialFragmentId}'，回退为满 {maxW}x{maxH} 餐桌。", "GameRun");
                GpTable fallback = new GpTable(maxW, maxH);
                HashSet<GridPos> before = CaptureExistingCells(fallback);
                bossDebuffModel?.ModifyBuiltTable(fallback, recipeEntryCount, rng);
                CaptureBuiltTablePresentation(presentation, fallback, before);
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
            HashSet<GridPos> existingBefore = CaptureExistingCells(board);
            bossDebuffModel?.ModifyBuiltTable(board, recipeEntryCount, rng);
            CaptureBuiltTablePresentation(presentation, board, existingBefore);
            ApplyCellMaterialOverrides(board, run);
            return board;
        }

        private static BossDebuffPresentationPlan CreatePresentationPlan(
            cfg.BossDebuff definition,
            List<RecipeSlot> slots)
        {
            if (definition == null)
            {
                return null;
            }

            var presentation = new BossDebuffPresentationPlan(definition.Id, definition.Dialogues);
            int initial = TotalRecipeEntries(slots);
            presentation.SetRecipeEntryCounts(initial, initial);
            return presentation;
        }

        private static void CaptureRecipePresentation(
            BossDebuffPresentationPlan presentation,
            List<RecipeSlot> slots)
        {
            if (presentation == null)
            {
                return;
            }

            presentation.SetRecipeEntryCounts(
                presentation.InitialRecipeEntryCount,
                TotalRecipeEntries(slots));
            int skip = presentation.InitialRecipeEntryCount;
            int index = 0;
            foreach (RecipeSlot slot in slots)
            {
                foreach (RecipeSlotEntry entry in slot.Entries)
                {
                    if (index++ >= skip)
                    {
                        presentation.DuplicatedDishIds.Add(entry.DishId);
                    }
                }
            }
        }

        private static HashSet<GridPos> CaptureExistingCells(GpTable board)
            => board != null
                ? new HashSet<GridPos>(board.ExistingCells())
                : new HashSet<GridPos>();

        private static HashSet<GridPos> CaptureDisabledCells(GpTable board)
        {
            var result = new HashSet<GridPos>();
            if (board == null)
            {
                return result;
            }

            foreach (GridPos cell in board.ExistingCells())
            {
                if (board.IsDisabled(cell))
                {
                    result.Add(cell);
                }
            }

            return result;
        }

        private static void CaptureBuiltTablePresentation(
            BossDebuffPresentationPlan presentation,
            GpTable board,
            HashSet<GridPos> before)
        {
            if (presentation == null || board == null || before == null)
            {
                return;
            }

            HashSet<GridPos> after = CaptureExistingCells(board);
            foreach (GridPos cell in after)
            {
                if (!before.Contains(cell))
                {
                    presentation.AddedCells.Add(cell);
                }
            }

            foreach (GridPos cell in before)
            {
                if (!after.Contains(cell))
                {
                    presentation.RemovedCells.Add(cell);
                }
            }
        }

        private static void CaptureDisabledPresentation(
            BossDebuffPresentationPlan presentation,
            GpTable board,
            HashSet<GridPos> before)
        {
            if (presentation == null || board == null)
            {
                return;
            }

            foreach (GridPos cell in CaptureDisabledCells(board))
            {
                if (before == null || !before.Contains(cell))
                {
                    presentation.DisabledCells.Add(cell);
                }
            }
        }

        /// <summary>把玩家用「铺台小票」永久附加的格子材质叠加进餐桌（拼桌后统一 merge，经营挑战与预览一致）。</summary>
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

        /// <summary>把装饰品局级修正注入经营挑战会话（各模型 ApplyToBattle）。</summary>
        private static void ApplyPassiveItems(GameRun run, BattleSession session)
        {
            foreach (GourmetProject.Game.Meta.Passives.PassiveItemModel model in run.PassiveModels)
            {
                model.ApplyToBattle(session);
            }
        }
    }
}
