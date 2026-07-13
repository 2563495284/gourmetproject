using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
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

        /// <summary>全家福：获得 effectValue 金币 + 一个随机被动道具（随机食物部分留 TODO）。</summary>
        public static void ApplyFamilyPack(GameRun run, ItemDefinition item)
        {
            if (run == null || item == null)
            {
                return;
            }

            run.Gold += System.Math.Max(0, (int)item.EffectValue);
            GrantRandomPassives(run, 1, item.Id);
            // TODO(passive-item): 额外发放一个随机食物（需食物发放服务就绪）。
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
