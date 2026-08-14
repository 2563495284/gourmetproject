using System;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Tutorial;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Procedure
{
    /// <summary>
    /// 玩法流程：从菜单切入后，建立（或继续）一次肉鸽运行，加载独立经营挑战场景并打开局内经营挑战界面。
    /// 局外周循环（领奖、事件、商店）在此流程内通过 UI 切换推进；经营挑战全程停留在 Battle.unity。
    /// </summary>
    public sealed class ProcedureGameplay : ProcedureBase
    {
        private const string Tag = "Gameplay";
        private const string BattleSceneName = "Battle";

        // 进入经营挑战时缓存的菜单相机（Launch 场景），经营挑战期间禁用、返回时恢复。
        private Camera _menuCamera;
        private bool _battleSceneRequested;
        private bool _sceneEventsSubscribed;

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

            // Domain Reload 被禁用时，项目静态上下文可能来自上一轮 Play Session，
            // 而 Runtime 层的 RandomService 已在 SubsystemRegistration 中重新创建。
            // 正常读档会 Restore 完整随机快照；这里作为流程边界的防御性兜底，
            // 保证任何有效 GameRun 都不会带着未初始化的随机服务进入 Battle。
            EnsureRandomInitialized();

            GameplayFlowSignal.Consume();
            LoadBattleScene();
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (GameplayFlowSignal.ReturnToMenuRequested)
            {
                GameplayFlowSignal.Consume();
                CloseGameplayForms();
                GameRunContext.Clear();
                UnloadBattleScene();
                Log.Info("ProcedureGameplay: returning to menu.", Tag);
                ChangeState<ProcedureMenu>(procedureOwner);
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            UnsubscribeSceneEvents();
            base.OnLeave(procedureOwner, isShutdown);
        }

        // —— 经营挑战场景加载 / 卸载 ——

        private void LoadBattleScene()
        {
            _menuCamera = Camera.main;
            _battleSceneRequested = true;
            SubscribeSceneEvents();

            if (GameApp.Scenes.IsLoaded(SceneNames.Battle))
            {
                ActivateBattleScene();
                return;
            }

            GameApp.Scenes.Load(SceneNames.Battle);
            Log.Info("ProcedureGameplay: loading battle scene...", Tag);
        }

        private void ActivateBattleScene()
        {
            UnityEngine.SceneManagement.Scene battle = SceneManager.GetSceneByName(BattleSceneName);
            if (battle.IsValid() && battle.isLoaded)
            {
                SceneManager.SetActiveScene(battle);
            }

            // 单场景观感：经营挑战相机就绪后再禁用菜单相机，避免出现「无相机渲染」帧。
            if (_menuCamera != null)
            {
                _menuCamera.gameObject.SetActive(false);
            }

            GameApp.UI.OpenUIForm(UIForms.Battle, UIForms.GroupDefault);
            Log.Info($"ProcedureGameplay entered. character={GameRunContext.Current.CharacterId}, week={GameRunContext.Current.WeekIndex}.", Tag);
        }

        private void UnloadBattleScene()
        {
            UnsubscribeSceneEvents();

            if (_menuCamera != null)
            {
                _menuCamera.gameObject.SetActive(true);
                _menuCamera = null;
            }

            if (_battleSceneRequested && GameApp.Scenes.IsLoaded(SceneNames.Battle))
            {
                GameApp.Scenes.Unload(SceneNames.Battle);
            }

            _battleSceneRequested = false;
        }

        private void SubscribeSceneEvents()
        {
            if (_sceneEventsSubscribed)
            {
                return;
            }

            GameApp.Event.Subscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameApp.Event.Subscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            _sceneEventsSubscribed = true;
        }

        private void UnsubscribeSceneEvents()
        {
            if (!_sceneEventsSubscribed || GameApp.Event == null)
            {
                return;
            }

            GameApp.Event.Unsubscribe(LoadSceneSuccessEventArgs.EventId, OnLoadSceneSuccess);
            GameApp.Event.Unsubscribe(LoadSceneFailureEventArgs.EventId, OnLoadSceneFailure);
            _sceneEventsSubscribed = false;
        }

        private void OnLoadSceneSuccess(object sender, GameEventArgs e)
        {
            if (e is LoadSceneSuccessEventArgs args && args.SceneAssetName == SceneNames.Battle)
            {
                ActivateBattleScene();
            }
        }

        private void OnLoadSceneFailure(object sender, GameEventArgs e)
        {
            if (e is LoadSceneFailureEventArgs args && args.SceneAssetName == SceneNames.Battle)
            {
                Log.Error($"ProcedureGameplay: failed to load battle scene: {args.ErrorMessage}", Tag);
            }
        }

        private static void StartNewRun(string characterId)
        {
            cfg.Tables tables = GameApp.Config.Tables;
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(tables);

            string seed = $"{characterId}-{DateTime.UtcNow.Ticks:x}";
            GameApp.Random.Init(seed);

            bool isTutorialRun = TutorialProgressService.ConsumeTutorialRun();
            var run = new GameRun(
                tables,
                db,
                characterId,
                seed,
                weekIndex: 1,
                isTutorialRun: isTutorialRun,
                execution: RunExecutionEnvironment.CreateLive(
                    GameApp.Random,
                    MetaProgressPersistence.Load()));
            GameRunContext.Set(run);
            RunPersistence.Save(run);
            GameAnalyticsService.TrackRunStarted(run);
            GameAnalyticsService.TrackRunCheckpoint(run, 1, 0);
            Log.Info(
                $"New run started. character={characterId}, seed={seed}, tutorial={isTutorialRun}.",
                Tag);
        }

        private static void EnsureRandomInitialized()
        {
            if (GameApp.Random.IsInitialized)
            {
                return;
            }

            GameApp.Random.Init(GameRunContext.Current.SeedText);
            Log.Warning(
                "ProcedureGameplay: restored RandomService from the active run seed.",
                Tag);
        }

        private static void CloseMenuForms()
        {
            CloseIfOpen(UIForms.MainMenu);
            CloseIfOpen(UIForms.CharacterSelect);
            CloseIfOpen(UIForms.Settings);
        }

        private static void CloseGameplayForms()
        {
            CloseIfOpen(UIForms.Battle);
            CloseIfOpen(UIForms.Reward);
            CloseIfOpen(UIForms.Result);
            CloseIfOpen(UIForms.Settings);
            CloseIfOpen(UIForms.ConfirmDialog);
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
