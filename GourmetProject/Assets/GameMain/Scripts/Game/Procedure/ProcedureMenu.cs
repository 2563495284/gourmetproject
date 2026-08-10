using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
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
            GameAnalyticsService.ApplyStoredConsent();
            // if (GameAnalyticsService.ConsentState == AnalyticsConsentState.Unknown)
            // {
            //     var consent = new ConfirmDialogData
            //     {
            //         Title = "帮助我们改进游戏",
            //         Message = "是否允许发送匿名的游玩统计（如奖励选择、关卡进度和分数），用于平衡与体验优化？不会采集姓名、联系方式或聊天内容；你可以随时在设置中关闭。",
            //         ConfirmText = "允许",
            //         CancelText = "暂不允许",
            //         OnConfirm = () =>
            //         {
            //             GameAnalyticsService.SetConsent(AnalyticsConsentState.Granted);
            //             OpenMenuContent();
            //         },
            //         OnCancel = () =>
            //         {
            //             GameAnalyticsService.SetConsent(AnalyticsConsentState.Denied);
            //             OpenMenuContent();
            //         },
            //     };
            //     GameApp.UI.OpenUIForm(UIForms.ConfirmDialog, UIForms.GroupDialog, consent);
            //     return;
            // }
            GameAnalyticsService.SetConsent(AnalyticsConsentState.Granted);
            OpenMenuContent();
        }

        private static void OpenMenuContent()
        {
            GameSaveData saveData = GameSavePersistence.Load();
            if (OpeningComicProgress.ShouldPlay(saveData.GuideProgress))
            {
                MoveTransitionGroupToFront();
                int serialId = GameApp.UI.OpenUIForm(UIForms.OpeningComic, UIForms.GroupTransition);
                if (serialId <= 0)
                {
                    Log.Error("ProcedureMenu: failed to request opening comic, falling back to main menu.", Tag);
                    GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
                }
                else
                {
                    Log.Info("ProcedureMenu entered: first-run opening comic requested.", Tag);
                }
            }
            else
            {
                GameApp.UI.OpenUIForm(UIForms.MainMenu, UIForms.GroupDefault);
                Log.Info("ProcedureMenu entered: opening comic already completed, main menu opened.", Tag);
            }
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

        private static void MoveTransitionGroupToFront()
        {
            var transitionGroup = GameApp.UI.GetUIGroup(UIForms.GroupTransition);
            if (transitionGroup?.Helper is Component helper)
            {
                helper.transform.SetAsLastSibling();
            }
        }
    }
}
