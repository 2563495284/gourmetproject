using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Platformer;
using GourmetProject.Game.Roguelike;
using GourmetProject.Game.UI;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Procedure
{
    /// <summary>
    /// 对局流程：代码方式构建《光影》游戏世界（GameWorld 在 Awake 内自建关卡/玩家/光照/怪物/HUD）。
    /// 按 Esc 返回主菜单。退出时销毁世界对象，恢复被禁用的相机。
    /// </summary>
    public sealed class ProcedureGameplay : ProcedureBase
    {
        private const string Tag = "Gameplay";
        private GameObject _worldGo;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            // 关闭主菜单等界面（菜单流程在 Default 组打开的界面）。
            CloseMenuForms();

            // 在构建世界（关卡随机生成依赖随机系统）之前，按新局/继续初始化随机种子。
            PrepareRun();

            _worldGo = new GameObject("GameWorld");
            _worldGo.AddComponent<GameWorld>();
            Log.Info("ProcedureGameplay entered: world built.", Tag);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ChangeState<ProcedureMenu>(procedureOwner);
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            // 局外成长结算：按本局最高进度 / 是否通关结算光之碎片（设计文档 13.10）。
            GameWorld world = GameWorld.Current;
            if (world != null && !isShutdown)
            {
                MetaProfile.Current.OnRunEnded(world.MaxProgress, world.IsVictory);
            }

            if (_worldGo != null)
            {
                Object.Destroy(_worldGo);
                _worldGo = null;
            }

            base.OnLeave(procedureOwner, isShutdown);
        }

        private static void PrepareRun()
        {
            if (RunSession.HasPendingLoad)
            {
                // 继续游戏：用存档种子复现同一张地图（已选技能由 GameWorld 重放）。
                GameApp.Random.Init(RunSession.PendingLoad.SeedText);
                Log.Info($"Continue run with seed '{RunSession.PendingLoad.SeedText}'.", Tag);
            }
            else
            {
                // 新局：清掉旧档并生成全新随机种子。
                GameApp.Save.Delete(UIForms.GameSaveSlot);
                GameApp.Random.Init(string.Empty);
                Log.Info($"New run with seed '{GameApp.Random.SeedText}'.", Tag);
            }
        }

        private static void CloseMenuForms()
        {
            if (GameApp.UI.HasUIForm(UIForms.MainMenu))
            {
                var form = GameApp.UI.GetUIForm(UIForms.MainMenu);
                if (form != null) GameApp.UI.CloseUIForm(form);
            }
        }
    }
}
