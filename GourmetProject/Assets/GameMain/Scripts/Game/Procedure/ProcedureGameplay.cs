using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Platformer;
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
            if (_worldGo != null)
            {
                Object.Destroy(_worldGo);
                _worldGo = null;
            }

            base.OnLeave(procedureOwner, isShutdown);
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
