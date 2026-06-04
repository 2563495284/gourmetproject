using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 技能抽取核心（无 Unity 依赖，可独立单测）：从指定 Tier 池按连锁规则过滤候选，
    /// 施加 Tag 软引导加权，再用确定性随机流不放回地抽出 N 个候选供玩家 3 选 1。
    ///
    /// 连锁规则（设计文档 13.5/13.7/13.9）：
    ///   - 互斥：拥有任一 <see cref="SkillDef.Mutex"/> 列出的技能则本技能不出现。
    ///   - 叠加上限：达到 <see cref="SkillDef.MaxStacks"/> 不再出现（0=无限，经济系）。
    ///   - 移动技不重复：<see cref="SkillDef.IsMovement"/> 的技能被选后全局不再出现。
    ///   - 前置：<see cref="SkillDef.PrereqAny"/> 非空时，需已拥有其中任一才解锁。
    /// </summary>
    public static class SkillDraftService
    {
        /// <summary>每拥有一个同 tag 技能，候选权重提升的比例（设计文档 13.5「+30%」）。</summary>
        public const float TagBoostPerPick = 0.3f;

        /// <summary>
        /// 从 <paramref name="tierPool"/> 抽取最多 <paramref name="count"/> 个不重复候选。
        /// 若合格候选不足，则有几个给几个（降级，不抛异常）。
        /// </summary>
        public static List<SkillDef> Draft(
            IReadOnlyList<SkillDef> tierPool,
            RunSkillState state,
            IRandomStream rng,
            int count = 3)
        {
            if (tierPool == null) throw new ArgumentNullException(nameof(tierPool));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            // 1. 过滤合格候选。
            var candidates = new List<SkillDef>(tierPool.Count);
            for (int i = 0; i < tierPool.Count; i++)
            {
                SkillDef def = tierPool[i];
                if (IsEligible(def, state)) candidates.Add(def);
            }

            // 2. Tag 软引导加权。
            var weights = new List<float>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                weights.Add(WeightFor(candidates[i], state));
            }

            // 3. 不放回加权抽取。
            int take = Math.Min(count, candidates.Count);
            var result = new List<SkillDef>(take);
            for (int n = 0; n < take; n++)
            {
                int idx = rng.WeightedPickIndex(weights);
                result.Add(candidates[idx]);
                candidates.RemoveAt(idx);
                weights.RemoveAt(idx);
            }

            return result;
        }

        /// <summary>该技能在当前局面下是否可作为候选出现。</summary>
        public static bool IsEligible(SkillDef def, RunSkillState state)
        {
            if (def == null) return false;

            // 互斥：拥有任一互斥技能则移除。
            if (def.Mutex != null)
            {
                for (int i = 0; i < def.Mutex.Count; i++)
                {
                    if (state.Has(def.Mutex[i])) return false;
                }
            }

            // 移动技不重复（等价 maxStacks=1 已达；显式判一遍更稳妥）。
            if (def.IsMovement && state.StacksOf(def.Id) >= 1) return false;

            // 叠加上限：>0 时受限，0 表示无限。
            if (def.MaxStacks > 0 && state.StacksOf(def.Id) >= def.MaxStacks) return false;

            // 前置：任一满足即可。
            if (def.PrereqAny != null && def.PrereqAny.Count > 0)
            {
                bool ok = false;
                for (int i = 0; i < def.PrereqAny.Count; i++)
                {
                    if (state.Has(def.PrereqAny[i])) { ok = true; break; }
                }
                if (!ok) return false;
            }

            return true;
        }

        /// <summary>含 Tag 软引导加权后的候选权重（保证为正）。</summary>
        public static float WeightFor(SkillDef def, RunSkillState state)
        {
            float baseWeight = def.Weight > 0f ? def.Weight : 1f;
            float multiplier = 1f + TagBoostPerPick * state.TagCount(def.Tag);
            return baseWeight * multiplier;
        }
    }
}
