using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 被动道具「获得时(OnAcquire)」一次性效果的共享实现工具。
    /// 重构后不再集中 switch：各道具模型（<see cref="Passives.PassiveItemModel"/>）在 OnAcquired 里按需调用这些工具。
    /// 仅纯数值 / 资源类效果；需要选目标 / UI / 未就绪子系统的效果由对应模型留占位日志。
    /// </summary>
    public static class PassiveOnAcquireEffects
    {
        private const string Tag = "Item";
        private const string NegativeTag = "Negative";

        private static IRandomStream Rng(string itemId)
        {
            // 需要随机的效果按 SeedDomains.Item 派生确定性流；未初始化随机系统（如 EditMode 单测）时为 null，逐效果兜底。
            return GameApp.Random?.DomainStream(SeedDomains.Item, $"onacq_{itemId}");
        }

        /// <summary>随机金币：effectParam="range:min,max"，闭区间随机；无随机流时取区间中值兜底。</summary>
        public static void ApplyGoldNow(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return;
            }

            int min = 1;
            int max = 1;
            ParseRange(item.EffectParam, ref min, ref max);
            if (max < min)
            {
                max = min;
            }

            IRandomStream rng = Rng(item.Id);
            int gold = rng != null ? rng.Range(min, max + 1) : (min + max) / 2;
            run.Gold += System.Math.Max(0, gold);
        }

        /// <summary>高利贷：立即获得 effectValue 金币；effectParam="repay:N" 登记下一周应扣的债务。</summary>
        public static void ApplyLoan(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return;
            }

            run.Gold += System.Math.Max(0, (int)item.EffectValue);
            int repay = ParseToken(item.EffectParam, "repay");
            run.RegisterLoanDebt(System.Math.Max(0, repay));
        }

        /// <summary>丢弃负面道具：最多丢 maxCount 个带 Negative 标签的道具；goldPer>0 时每丢一个给钱。</summary>
        public static void DiscardNegatives(GameRun run, int maxCount, int goldPer)
        {
            if (run == null || maxCount <= 0)
            {
                return;
            }

            var negatives = new List<string>();
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition def = ItemDefinition.Get(run.Tables, state.ItemId, cfg.ItemKind.Passive);
                if (def != null && HasNegativeTag(def))
                {
                    negatives.Add(state.ItemId);
                }
            }

            int discarded = 0;
            foreach (string id in negatives)
            {
                if (discarded >= maxCount)
                {
                    break;
                }

                if (run.RemoveItem(id))
                {
                    discarded++;
                }
            }

            if (goldPer > 0 && discarded > 0)
            {
                run.Gold += goldPer * discarded;
            }
        }

        /// <summary>随机发放 count 个被动道具（无随机流时跳过）。</summary>
        public static void GrantRandomPassives(GameRun run, int count, string rngKeyItemId)
        {
            if (run == null || count <= 0)
            {
                return;
            }

            IRandomStream rng = Rng(rngKeyItemId);
            if (rng == null)
            {
                Log.Info("GrantRandomPassive 缺少随机流，已跳过。", Tag);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                ItemPoolService.GrantRandom(run.Tables, run, cfg.ItemKind.Passive, rng, fallbackGold: 0);
            }
        }

        /// <summary>全家福：生成 effectValue 金币 + 一个随机被动道具 + 一个随机食物的通用领奖包。</summary>
        public static void ApplyFamilyPack(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return;
            }

            IRandomStream rng = Rng(item.Id);
            RewardOffer offer = RewardGranter.GenerateFamilyPackOffer(run, item, rng);
            if (offer == null)
            {
                return;
            }

            string day = run.CurrentDay.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string key = $"onacq_{item.Id}_w{run.WeekIndex}_d{day}_s{run.RunActionStepIndex}";
            run.EnqueueGenericRewardOffer(key, item.Name, offer);
            Log.Info($"已生成通用领奖包：{item.Name}。", Tag);
        }

        public static void OpenItemChoice(GameRun run, ItemDefinition sourceItem, cfg.ItemKind kind, int count)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            List<RewardChoice> choices = RollItemChoices(run, kind, count, sourceItem.Id);
            if (choices.Count == 0)
            {
                Log.Info($"{sourceItem.Name} 没有可选道具，已跳过。", Tag);
                return;
            }

            BattleForm battle = BattleForm.Active;
            if (battle != null && battle.OpenRewardItemChoices(sourceItem.Name, choices, kind))
            {
                return;
            }

            string key = BuildAcquireKey(run, sourceItem.Id);
            run.EnqueueGenericRewardOffer(key, sourceItem.Name, new RewardOffer(0, choices, null, baseGoldClaimed: true));
            OpenGenericRewardForm();
        }

        public static void GrantRandomActivesViaRewardForm(GameRun run, ItemDefinition sourceItem, int count)
        {
            if (run == null || sourceItem == null || count <= 0)
            {
                return;
            }

            IRandomStream rng = Rng(sourceItem.Id);
            if (rng == null)
            {
                Log.Info("GrantRandomActive 缺少随机流，已跳过。", Tag);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                List<RewardChoice> choices = RollItemChoices(run, cfg.ItemKind.Active, 1, $"{sourceItem.Id}_{i}");
                if (choices.Count == 0)
                {
                    continue;
                }

                string key = $"{BuildAcquireKey(run, sourceItem.Id)}_active_{i}";
                run.EnqueueGenericRewardOffer(key, $"{sourceItem.Name} {i + 1}/{count}", new RewardOffer(0, choices, null, baseGoldClaimed: true));
            }

            if (run.HasPendingGenericRewards)
            {
                OpenGenericRewardForm();
            }
        }

        public static void OpenDishChoice(GameRun run, ItemDefinition sourceItem, int count)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            List<RewardChoice> choices = RollDishChoices(run, count, sourceItem.Id);
            if (choices.Count == 0)
            {
                Log.Info($"{sourceItem.Name} 没有可选菜品，已跳过。", Tag);
                return;
            }

            BattleForm battle = BattleForm.Active;
            if (battle != null && battle.OpenAcquireDishPack(sourceItem.Name, choices))
            {
                return;
            }

            string key = BuildAcquireKey(run, sourceItem.Id);
            run.EnqueueGenericRewardOffer(key, sourceItem.Name, new RewardOffer(0, choices, null, baseGoldClaimed: true));
            OpenGenericRewardForm();
        }

        public static void OpenFragmentChoice(GameRun run, ItemDefinition sourceItem, int count)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            List<string> fragmentIds = RollFragmentIds(run, count, sourceItem.Id);
            if (fragmentIds.Count == 0)
            {
                Log.Info($"{sourceItem.Name} 没有可用餐桌碎片，已跳过。", Tag);
                return;
            }

            run.SetPendingFragmentPack(fragmentIds);
            BattleForm battle = BattleForm.Active;
            if (battle != null)
            {
                battle.OpenRewardTableEdit(fragmentIds, placed =>
                {
                    RunPersistence.Save(run);
                    if (!placed)
                    {
                        battle.OpenShop();
                    }
                });
            }
        }

        public static void GrantRecipeBook(GameRun run, ItemDefinition sourceItem)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            BattleForm battle = BattleForm.Active;
            if (battle != null)
            {
                battle.TryGrantRecipeBookFromPassive(sourceItem.Name);
                return;
            }

            if (run.AddRecipeBook())
            {
                RunPersistence.Save(run);
            }
            else
            {
                Log.Info($"{sourceItem.Name} 使用失败：菜谱已满。", Tag);
            }
        }

        public static void RandomizeItems(GameRun run, ItemDefinition sourceItem)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            IRandomStream rng = Rng(sourceItem.Id);
            if (rng == null)
            {
                Log.Info("RandomizeItems 缺少随机流，已跳过。", Tag);
                return;
            }

            int passiveCount = 0;
            int activeCount = 0;
            foreach (RunItemState state in run.Items)
            {
                ItemDefinition item = ItemDefinition.Get(run.Tables, state.ItemId);
                if (item == null)
                {
                    continue;
                }

                if (item.Kind == cfg.ItemKind.Active)
                {
                    activeCount++;
                }
                else
                {
                    passiveCount++;
                }
            }

            var newIds = new List<string>();
            newIds.AddRange(ItemPoolService.Roll(run.Tables, run, cfg.ItemKind.Passive, rng, passiveCount));
            newIds.AddRange(ItemPoolService.Roll(run.Tables, run, cfg.ItemKind.Active, rng, activeCount));

            List<ItemAcquireResult> acquireResults = run.ReplaceItems(newIds);
            var results = new List<RandomizedItemResult>(acquireResults.Count);
            for (int i = 0; i < acquireResults.Count; i++)
            {
                ItemAcquireResult result = acquireResults[i];
                ItemDefinition item = ItemDefinition.Get(run.Tables, result.ItemId);
                results.Add(new RandomizedItemResult(item, result));
            }

            RunPersistence.Save(run);

            BattleForm battle = BattleForm.Active;
            if (battle != null)
            {
                battle.OpenRandomizedItemsPanel(sourceItem.Name, results);
            }
        }

        public static void AddActionRerolls(GameRun run, int count)
        {
            if (run == null || count <= 0)
            {
                return;
            }

            run.AddActionRerollCount(count);
            RunPersistence.Save(run);
        }

        private static bool HasNegativeTag(ItemDefinition item)
        {
            if (string.IsNullOrEmpty(item.SpecialTags))
            {
                return false;
            }

            foreach (string tag in item.SpecialTags.Split('|', ';', ','))
            {
                if (string.Equals(tag.Trim(), NegativeTag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<RewardChoice> RollItemChoices(GameRun run, cfg.ItemKind kind, int count, string key)
        {
            var choices = new List<RewardChoice>();
            IRandomStream rng = Rng(key);
            if (run == null || rng == null || count <= 0)
            {
                return choices;
            }

            List<string> ids = ItemPoolService.Roll(run.Tables, run, kind, rng, count);
            foreach (string id in ids)
            {
                ItemDefinition item = ItemDefinition.Get(run.Tables, id, kind);
                if (item == null)
                {
                    continue;
                }

                choices.Add(new RewardChoice(
                    kind == cfg.ItemKind.Passive ? cfg.RewardKind.PassiveItemChoice : cfg.RewardKind.ActiveItemGrant,
                    item.Id,
                    item.Name,
                    item.IsPassive ? $"被动道具 · {item.Quality}" : "主动道具"));
            }

            return choices;
        }

        private static List<RewardChoice> RollDishChoices(GameRun run, int count, string key)
        {
            var choices = new List<RewardChoice>();
            IRandomStream rng = Rng(key);
            if (run == null || rng == null || count <= 0)
            {
                return choices;
            }

            int hidden = HiddenScoreService.DishHiddenScore(run, run.LastActionContext);
            var candidates = new List<GourmetProject.Gameplay.Model.DishDef>();
            foreach (GourmetProject.Gameplay.Model.DishDef dish in run.Library.Dishes)
            {
                if (dish.CoversHiddenScore(hidden))
                {
                    candidates.Add(dish);
                }
            }

            if (candidates.Count == 0)
            {
                candidates.AddRange(run.Library.Dishes);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (GourmetProject.Gameplay.Model.DishDef dish in candidates)
                {
                    weights.Add(RewardPoolService.HiddenScoreWeight(dish.BaseWeight, dish.HiddenMean, hidden, 5));
                }

                int index = rng.WeightedPickIndex(weights);
                GourmetProject.Gameplay.Model.DishDef chosen = candidates[index];
                candidates.RemoveAt(index);
                choices.Add(new RewardChoice(
                    cfg.RewardKind.DishChoice,
                    chosen.Id,
                    chosen.Name,
                    $"加入菜谱，美味度 {chosen.Deliciousness}"));
            }

            return choices;
        }

        private static List<string> RollFragmentIds(GameRun run, int count, string key)
        {
            var ids = new List<string>();
            IRandomStream rng = Rng(key);
            if (run == null || rng == null || count <= 0)
            {
                return ids;
            }

            int hidden = HiddenScoreService.FragmentHiddenScore(run, run.LastActionContext);
            var candidates = new List<GourmetProject.Gameplay.Model.TableFragmentDef>();
            foreach (GourmetProject.Gameplay.Model.TableFragmentDef fragment in run.Database.AllFragments)
            {
                if (fragment.BaseWeight <= 0f || hidden < fragment.HiddenMin || hidden > fragment.HiddenMax || !run.CanAttachTableFragment(fragment))
                {
                    continue;
                }

                candidates.Add(fragment);
            }

            if (candidates.Count == 0)
            {
                foreach (GourmetProject.Gameplay.Model.TableFragmentDef fragment in run.Database.AllFragments)
                {
                    if (fragment.BaseWeight > 0f && run.CanAttachTableFragment(fragment))
                    {
                        candidates.Add(fragment);
                    }
                }
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (GourmetProject.Gameplay.Model.TableFragmentDef fragment in candidates)
                {
                    weights.Add(RewardPoolService.HiddenScoreWeight(fragment.BaseWeight, fragment.HiddenMean, hidden, 5));
                }

                int index = rng.WeightedPickIndex(weights);
                ids.Add(candidates[index].Id);
                candidates.RemoveAt(index);
            }

            return ids;
        }

        private static string BuildAcquireKey(GameRun run, string itemId)
        {
            string day = run.CurrentDay.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return $"onacq_{itemId}_w{run.WeekIndex}_d{day}_s{run.RunActionStepIndex}_{run.NextActiveUseKey()}";
        }

        private static void OpenGenericRewardForm()
        {
            if (GameApp.UI.HasUIForm(UIForms.Reward))
            {
                return;
            }

            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog, RewardFormOpenArgs.GenericQueue());
        }

        /// <summary>解析 "range:min,max"（或 "min,max"）到 min/max。</summary>
        private static void ParseRange(string param, ref int min, ref int max)
        {
            if (string.IsNullOrEmpty(param))
            {
                return;
            }

            string body = param;
            int colon = param.IndexOf(':');
            if (colon >= 0)
            {
                body = param.Substring(colon + 1);
            }

            string[] parts = body.Split(',');
            if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out int lo))
            {
                min = lo;
                max = lo;
            }

            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int hi))
            {
                max = hi;
            }
        }

        /// <summary>解析形如 "key:value" 的 token（分隔符 ; , |），无则 0。</summary>
        private static int ParseToken(string param, string key)
        {
            if (string.IsNullOrEmpty(param))
            {
                return 0;
            }

            foreach (string token in param.Split(';', ',', '|'))
            {
                int idx = token.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, idx).Trim(), key, System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(idx + 1).Trim(), out int v))
                {
                    return v;
                }
            }

            return 0;
        }
    }
}
