using System;
using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Gameplay;
using GourmetProject.Game.UI;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using UnityGameFramework.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Procedure
{
    /// <summary>
    /// 玩法流程：从菜单切入后，建立（或继续）一次肉鸽运行并打开局内战斗界面。
    /// 局外周循环（领奖、事件、商店）后续在此流程内通过 UI 切换推进。
    /// </summary>
    public sealed class ProcedureGameplay : ProcedureBase
    {
        private const string Tag = "Gameplay";

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            GameplayEntryRequest.Mode mode = GameplayEntryRequest.RequestedMode;
            string characterId = GameplayEntryRequest.CharacterId;
            GameplayEntryRequest.Consume();

            CloseMenuForms();

            if (mode == GameplayEntryRequest.Mode.NewRun)
            {
                StartNewRun(characterId);
            }
            else if (!GameRunContext.HasRun)
            {
                GameRun loaded = RunPersistence.TryLoad();
                if (loaded != null)
                {
                    GameRunContext.Set(loaded);
                }
                else
                {
                    Log.Warning("ProcedureGameplay: continue requested but no save found.", Tag);
                }
            }

            if (!GameRunContext.HasRun)
            {
                Log.Error("ProcedureGameplay: no active run, returning to menu.", Tag);
                ChangeState<ProcedureMenu>(procedureOwner);
                return;
            }

            GameplayFlowSignal.Consume();
            GameApp.UI.OpenUIForm(UIForms.Battle, UIForms.GroupDefault);
            Log.Info($"ProcedureGameplay entered. character={GameRunContext.Current.CharacterId}, week={GameRunContext.Current.WeekIndex}.", Tag);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (GameplayFlowSignal.ReturnToMenuRequested)
            {
                GameplayFlowSignal.Consume();
                CloseGameplayForms();
                GameRunContext.Clear();
                Log.Info("ProcedureGameplay: returning to menu.", Tag);
                ChangeState<ProcedureMenu>(procedureOwner);
            }
        }

        private static void CloseGameplayForms()
        {
            CloseIfOpen(UIForms.Battle);
            CloseIfOpen(UIForms.Reward);
            CloseIfOpen(UIForms.WeekMap);
            CloseIfOpen(UIForms.Shop);
            CloseIfOpen(UIForms.DishDetail);
        }

        private static void StartNewRun(string characterId)
        {
            cfg.Tables tables = GameApp.Config.Tables;
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(tables);

            string seed = $"{characterId}-{DateTime.UtcNow.Ticks:x}";
            GameApp.Random.Init(seed);

            var run = new GameRun(tables, db, characterId, seed, weekIndex: 1);
            GameRunContext.Set(run);
            Log.Info($"New run started. character={characterId}, seed={seed}.", Tag);
        }

        private static void CloseMenuForms()
        {
            CloseIfOpen(UIForms.MainMenu);
            CloseIfOpen(UIForms.CharacterSelect);
            CloseIfOpen(UIForms.Settings);
        }

        private static void CloseIfOpen(string assetName)
        {
            if (GameApp.UI.HasUIForm(assetName))
            {
                UIForm form = GameApp.UI.GetUIForm(assetName);
                if (form != null)
                {
                    GameApp.UI.CloseUIForm(form);
                }
            }
        }
    }
}
