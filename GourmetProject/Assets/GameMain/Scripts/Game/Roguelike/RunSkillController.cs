using System;
using System.Collections.Generic;
using GourmetProject.Core.Diagnostics;
using GourmetProject.Core.Rng;
using GourmetProject.Game.UI;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 单局技能流程协调器：持有图鉴与已选状态，在检查点触发 3 选 1 抽取、打开界面、
    /// 应用选择并写入存档；并支持从存档重放已选技能（继续游戏）。
    /// </summary>
    public sealed class RunSkillController
    {
        private const string Tag = "RunSkill";

        /// <summary>技能抽取使用的命名随机流（与关卡流 "level" 隔离，互不污染消耗顺序）。</summary>
        public const string DraftStream = "skill_offer";

        private readonly SkillCatalog _catalog;
        private readonly RunSkillState _state = new RunSkillState();
        private readonly SkillRuntimeState _runtime;
        private int _activatedCheckpointCount;

        public RunSkillController()
        {
            _catalog = SkillCatalog.FromConfig();
            _runtime = new SkillRuntimeState(_state);
        }

        /// <summary>供玩法系统查询当前 build 的只读视图。</summary>
        public SkillRuntimeState Runtime => _runtime;

        /// <summary>已激活的普通检查点数量。</summary>
        public int ActivatedCheckpointCount => _activatedCheckpointCount;

        /// <summary>从存档重放已选技能与进度（继续游戏；不重新抽取，不消耗抽取流）。</summary>
        public void RestoreFrom(RunSaveData data)
        {
            if (data == null) return;

            if (data.PickedSkillIds != null)
            {
                for (int i = 0; i < data.PickedSkillIds.Count; i++)
                {
                    SkillDef def = _catalog.Get(data.PickedSkillIds[i]);
                    if (def != null) _state.Apply(def);
                }
            }

            _activatedCheckpointCount = data.ActivatedCheckpointCount;
            Log.Info($"Restored run: {_state.PickedOrder.Count} skills, cp={_activatedCheckpointCount}.", Tag);
        }

        /// <summary>
        /// 到达一个普通检查点：按当前段位抽取候选并打开选择界面；玩家选定（或无候选）后存档，
        /// 并回调 <paramref name="onResolved"/> 通知调用方恢复游戏。
        /// </summary>
        public void BeginCheckpointDraft(Action onResolved)
        {
            int tier = SkillTierMap.TierForCheckpoint(_activatedCheckpointCount);
            IReadOnlyList<SkillDef> pool = _catalog.PoolOf(tier);

            // 局外成长：默认锁定且未解锁的进阶/终极技不进入抽取池（设计文档 13.10）。
            var available = new List<SkillDef>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (MetaProfile.Current.IsSkillAvailable(pool[i].Id)) available.Add(pool[i]);
            }

            IRandomStream rng = GameApp.Random.Stream(DraftStream);
            List<SkillDef> options = SkillDraftService.Draft(available, _state, rng, 3);

            _activatedCheckpointCount++;

            if (options.Count == 0)
            {
                // 无合格候选（极端情况）：直接存档放行，不卡住玩家。
                Log.Warning($"No eligible skill at tier {tier}; skipping draft.", Tag);
                Save();
                onResolved?.Invoke();
                return;
            }

            var data = new SkillPickData
            {
                Tier = tier,
                Options = options,
                OnPicked = picked =>
                {
                    _state.Apply(picked);
                    Log.Info($"Picked skill '{picked.Id}' (T{tier}).", Tag);
                    Save();
                    onResolved?.Invoke();
                },
            };

            GameApp.UI.OpenUIForm(UIForms.SkillPick, UIForms.GroupDialog, data);
        }

        /// <summary>把当前进度写入主存档槽。</summary>
        public void Save()
        {
            var data = new RunSaveData
            {
                SeedText = GameApp.Random.SeedText,
                Rng = GameApp.Random.Capture(),
                PickedSkillIds = new List<string>(_state.PickedOrder),
                ActivatedCheckpointCount = _activatedCheckpointCount,
            };
            GameApp.Save.Save(UIForms.GameSaveSlot, data);
        }
    }
}
