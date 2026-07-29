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
                if (def != null && def.IsNegative)
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

        /// <summary>
        /// 统一的「获得时发奖」：按道具 EffectParam 指定的 reward_slot 槽组 roll 出 offer，入通用领奖队列并打开 RewardForm。
        /// 覆盖随机发放 / 多选一 / 碎片 / 风味菜品等，等同于一次正常领奖。
        /// </summary>
        public static void GrantConfigReward(GameRun run, ItemDefinition sourceItem)
        {
            if (run == null || sourceItem == null)
            {
                return;
            }

            IRandomStream rng = Rng(sourceItem.Id);
            if (rng == null)
            {
                Log.Info($"{sourceItem.Name} 缺少随机流，已跳过。", Tag);
                return;
            }

            RewardOffer offer = RewardGranter.BuildConfigOffer(run, rng, sourceItem.EffectParam, run.LastActionContext);
            if (offer == null)
            {
                Log.Info($"{sourceItem.Name} 没有可用奖励（配置 '{sourceItem.EffectParam}'），已跳过。", Tag);
                return;
            }

            run.EnqueueGenericRewardOffer(BuildAcquireKey(run, sourceItem.Id), sourceItem.Name, offer);
            OpenGenericRewardForm();
        }

        /// <summary>全家福：EffectValue 金币 + EffectParam="被动槽组|菜品槽组" 各发一份，合成一个通用领奖包。</summary>
        public static void ApplyFamilyPack(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return;
            }

            IRandomStream rng = Rng(item.Id);
            if (rng == null)
            {
                Log.Info($"{item.Name} 缺少随机流，已跳过。", Tag);
                return;
            }

            string[] groups = (item.EffectParam ?? string.Empty).Split('|');
            string passiveGroup = groups.Length > 0 ? groups[0].Trim() : string.Empty;
            string dishGroup = groups.Length > 1 ? groups[1].Trim() : string.Empty;
            int gold = System.Math.Max(0, (int)item.EffectValue);

            RewardOffer offer = RewardGranter.BuildConfigOffer(
                run,
                rng,
                gold,
                string.IsNullOrEmpty(passiveGroup) ? System.Array.Empty<string>() : new[] { passiveGroup },
                dishGroup,
                run.LastActionContext);
            if (offer == null)
            {
                return;
            }

            run.EnqueueGenericRewardOffer(BuildAcquireKey(run, item.Id), item.Name, offer);
            OpenGenericRewardForm();
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

    }
}
