using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Runtime;
using UnityEngine;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Procedure
{
    /// <summary>
    /// 菜单流程（玩法前端）：注册 UI 界面组并打开主菜单。由基础层 ProcedureMain 数据驱动切入。
    /// </summary>
    public sealed class ProcedureMenu : ProcedureBase
    {
        private const string Tag = "Menu";

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            EnsureUIGroups();
            GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
            Log.Info("ProcedureMenu entered: main menu opened.", Tag);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // 菜单界面登记了「开始局内」请求时，切入玩法流程。
            if (GameplayEntryRequest.Pending)
            {
                Log.Info("ProcedureMenu: gameplay entry requested, switching to gameplay procedure.", Tag);
                ChangeState<ProcedureGameplay>(procedureOwner);
            }
        }

        private static void EnsureUIGroups()
        {
            if (!GameApp.UI.HasUIGroup(UIForms.GroupDefault))
            {
                GameApp.UI.AddUIGroup(UIForms.GroupDefault, 0);
            }

            if (!GameApp.UI.HasUIGroup(UIForms.GroupDialog))
            {
                GameApp.UI.AddUIGroup(UIForms.GroupDialog, 1);
            }

            if (!GameApp.UI.HasUIGroup(UIForms.GroupTransition))
            {
                GameApp.UI.AddUIGroup(UIForms.GroupTransition, 2);
            }

            // GameFramework 的界面组容器由框架运行时创建，只有普通 Transform，
            // 子界面用 stretch 锚点会塌缩到 Canvas 原点。这里把组容器升级为撑满 Canvas 的 RectTransform。
            StretchGroupHelper(UIForms.GroupDefault);
            StretchGroupHelper(UIForms.GroupDialog);
            StretchGroupHelper(UIForms.GroupTransition);
        }

        private static void StretchGroupHelper(string groupName)
        {
            var group = GameApp.UI.GetUIGroup(groupName);
            if (!(group?.Helper is Component helper))
            {
                return;
            }

            var go = helper.gameObject;
            var rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = go.AddComponent<RectTransform>();
            }

            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
