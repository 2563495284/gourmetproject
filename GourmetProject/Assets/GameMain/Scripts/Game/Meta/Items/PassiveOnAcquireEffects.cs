using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 被动道具「获得时(OnAcquire)」一次性效果派发器：道具首次加入持有列表后立即结算一次。
    /// 仅实现纯数值 / 资源类效果；需要玩家选目标 / UI / 尚未就绪子系统的效果统一留 TODO 占位。
    /// </summary>
    public static class PassiveOnAcquireEffects
    {
        private const string Tag = "Item";
        private const string NegativeTag = "Negative";

        public static void Apply(GameRun run, cfg.Item item)
        {
            if (run == null || item == null)
            {
                return;
            }

            // 需要随机的效果按 SeedDomains.Item 派生确定性流；未初始化随机系统（如 EditMode 单测）时为 null，逐效果兜底。
            IRandomStream rng = GameApp.Random?.DomainStream(SeedDomains.Item, $"onacq_{item.Id}");

            switch (item.EffectType)
            {
                case ItemEffectTypes.GoldNow:
                    ApplyGoldNow(run, item, rng);
                    break;
                case ItemEffectTypes.Loan:
                    ApplyLoan(run, item);
                    break;
                case ItemEffectTypes.DiscardNegative:
                    DiscardNegatives(run, System.Math.Max(0, (int)item.EffectValue), goldPer: 0);
                    break;
                case ItemEffectTypes.DiscardNegativeForGold:
                    DiscardNegatives(run, int.MaxValue, goldPer: System.Math.Max(0, (int)item.EffectValue));
                    break;
                case ItemEffectTypes.GrantRandomPassive:
                    GrantRandomPassives(run, System.Math.Max(0, (int)item.EffectValue), rng);
                    break;
                case ItemEffectTypes.FamilyPack:
                    ApplyFamilyPack(run, item, rng);
                    break;
                case ItemEffectTypes.GoldMealBonus:
                {
                    int meals = 0;
                    ParseToken(item.EffectParam, "meals", ref meals);
                    run.AddMealBonusMeals(meals);
                    break;
                }
                case ItemEffectTypes.RequiredScoreToOne:
                {
                    int meals = 0;
                    ParseToken(item.EffectParam, "meals", ref meals);
                    run.AddScoreToOneMeals(meals);
                    break;
                }

                // —— 以下留 TODO：需玩家选目标 / UI / 子系统（时间轴、菜谱、风味、标签、主动道具）尚未就绪 ——
                case ItemEffectTypes.ChooseOnePassive:
                case ItemEffectTypes.ChooseOneActive:
                case ItemEffectTypes.ChooseOneFood:
                case ItemEffectTypes.ChooseOneFragment:
                case ItemEffectTypes.GrantRandomActive:
                case ItemEffectTypes.RandomizeItems:
                case ItemEffectTypes.GrantRecipe:
                case ItemEffectTypes.CopyFood:
                case ItemEffectTypes.RerollAction:
                case ItemEffectTypes.FoodConvert:
                case ItemEffectTypes.FlavorEnhance:
                case ItemEffectTypes.FlavorRemoveForGold:
                case ItemEffectTypes.FlavorRemoveCopySkill:
                case ItemEffectTypes.FlavorRemoveDoubleScore:
                case ItemEffectTypes.FlavorContagion:
                case ItemEffectTypes.CellTagEnhance:
                case ItemEffectTypes.CellTagContagion:
                case ItemEffectTypes.TimelineRandomize:
                case ItemEffectTypes.TimelineExtraDay:
                case ItemEffectTypes.TimelineWeekMinus:
                case ItemEffectTypes.TimelineAddRewardNode:
                case ItemEffectTypes.TimelineAddInterestNode:
                    // TODO(passive-item): 需选目标 / UI / 对应子系统就绪后接入（见 docs/design/道具.md）。
                    Log.Info($"OnAcquire 效果 {item.EffectType}({item.Id}) 尚未实装，已忽略。", Tag);
                    break;
                default:
                    Log.Warning($"未识别的 OnAcquire effectType={item.EffectType}({item.Id})。", Tag);
                    break;
            }
        }

        /// <summary>随机金币：effectParam="range:min,max"，闭区间随机；无随机流时取区间中值兜底。</summary>
        private static void ApplyGoldNow(GameRun run, cfg.Item item, IRandomStream rng)
        {
            int min = 1;
            int max = 1;
            ParseRange(item.EffectParam, ref min, ref max);
            if (max < min)
            {
                max = min;
            }

            int gold = rng != null ? rng.Range(min, max + 1) : (min + max) / 2;
            run.Gold += System.Math.Max(0, gold);
        }

        /// <summary>高利贷：立即获得 effectValue 金币；effectParam="repay:N" 登记下一周应扣的债务。</summary>
        private static void ApplyLoan(GameRun run, cfg.Item item)
        {
            run.Gold += System.Math.Max(0, (int)item.EffectValue);
            int repay = 0;
            ParseToken(item.EffectParam, "repay", ref repay);
            run.RegisterLoanDebt(System.Math.Max(0, repay));
        }

        /// <summary>丢弃负面道具：最多丢 maxCount 个带 Negative 标签的道具；goldPer>0 时每丢一个给钱。</summary>
        private static void DiscardNegatives(GameRun run, int maxCount, int goldPer)
        {
            if (maxCount <= 0)
            {
                return;
            }

            var negatives = new List<string>();
            foreach (RunItemState state in run.Items)
            {
                cfg.Item def = run.Tables.TbItem.GetOrDefault(state.ItemId);
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

        private static void GrantRandomPassives(GameRun run, int count, IRandomStream rng)
        {
            if (count <= 0)
            {
                return;
            }

            if (rng == null)
            {
                // TODO(passive-item): 无随机流（如未初始化随机系统）时跳过随机发放。
                Log.Info("GrantRandomPassive 缺少随机流，已跳过。", Tag);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                ItemPoolService.GrantRandom(run.Tables, run, cfg.ItemKind.Passive, rng, fallbackGold: 0);
            }
        }

        /// <summary>全家福：获得 effectValue 金币 + 一个随机被动道具（随机食物部分留 TODO）。</summary>
        private static void ApplyFamilyPack(GameRun run, cfg.Item item, IRandomStream rng)
        {
            run.Gold += System.Math.Max(0, (int)item.EffectValue);
            GrantRandomPassives(run, 1, rng);
            // TODO(passive-item): 额外发放一个随机食物（需食物发放服务就绪）。
        }

        private static bool HasNegativeTag(cfg.Item item)
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

        /// <summary>解析形如 "key:value" 的 token（分隔符 ; , |）。</summary>
        private static void ParseToken(string param, string key, ref int value)
        {
            if (string.IsNullOrEmpty(param))
            {
                return;
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
                    value = v;
                    return;
                }
            }
        }
    }
}
