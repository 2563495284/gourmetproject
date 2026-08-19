using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class RewardFragmentTableRefreshTests
    {
        [Test]
        public void TableInspection_UsesCurrentRunTableAfterBattleHasSettled()
        {
            var battleTable = new DiningTable(1, 1);
            var currentRunTable = new DiningTable(2, 1);
            var database = new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var session = new BattleSession(
                battleTable,
                database,
                new Xoshiro256SS(260817UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 1);

            Assert.That(
                BattleInspectionCoordinator.ResolveTableInspectionSource(
                    GameplayView.Food,
                    session,
                    currentRunTable),
                Is.SameAs(battleTable),
                "经营中查看餐桌仍应展示本场实时盘面。");

            session.RestoreSettledForRewardView(1);

            Assert.That(
                BattleInspectionCoordinator.ResolveTableInspectionSource(
                    GameplayView.Food,
                    session,
                    currentRunTable),
                Is.SameAs(currentRunTable),
                "结算领奖后应展示包含新拼碎片的运行态餐桌。");
        }

        [Test]
        public void FragmentEdit_RefreshesPersistentHudAfterLeavingEditState()
        {
            var host = new FragmentEditHost();
            var coordinator = new BattleTableFragmentEditCoordinator(host);
            host.IsCoordinatorActive = () => coordinator.IsActive;

            Assert.That(
                coordinator.Open(new[] { "fragment_a" }, _ => { }),
                Is.True);

            host.Request.Completed(true);

            Assert.That(host.RefreshCount, Is.EqualTo(1));
            Assert.That(host.CoordinatorWasActiveAtRefresh, Is.False,
                "常驻栏刷新时必须已经退出碎片编辑态，才能读取 GameRun 的最新餐桌。");
            Assert.That(coordinator.IsActive, Is.False);
        }

        private sealed class FragmentEditLayer : IBattleTableFragmentEditLayer
        {
            public bool IsVisible { get; private set; }
            public bool IsConfigured => true;
            public GameObject BoardEditPanel => null;

            public void Initialize() { }
            public void Bind(Action onAction) { }
            public void Show() => IsVisible = true;
            public void HideImmediate() => IsVisible = false;
            public void ApplyActionState(TableFragmentEditActionState state) { }
        }

        private sealed class FragmentEditHost : IBattleTableFragmentEditHost
        {
#pragma warning disable SYSLIB0050
            private readonly GameRun _run = (GameRun)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            private readonly FragmentEditLayer _layer = new FragmentEditLayer();

            public Func<bool> IsCoordinatorActive { get; set; }
            public TableFragmentChoiceRequest Request { get; private set; }
            public int RefreshCount { get; private set; }
            public bool CoordinatorWasActiveAtRefresh { get; private set; }
            public GameRun Run => _run;
            public GameplayView CurrentView => GameplayView.Food;
            public IBattleTableFragmentEditLayer FragmentEditLayer => _layer;
            public bool CanInteract => true;
            public bool CanOpenFragmentEdit => true;
            public IReadOnlyList<int> CandidateRotations => Array.Empty<int>();

            public void ForceCloseInspection() { }
            public void CancelActiveItemUse() { }
            public bool SuspendFragmentEditSource() => true;
            public void RestoreFragmentEditSource() { }
            public void RestoreBattleWorld() { }
            public void BeginTableFragmentChoice(TableFragmentChoiceRequest request) => Request = request;
            public void ConfirmTableEditPlacement() { }
            public void SkipTableEditPack() { }
            public void HideTableEditWorld() { }
            public void SetInspectionNavigationBlocked(bool blocked) { }
            public bool OpenRecipeInspection(Action onClosed) => false;
            public void NotifyPreparingChild() { }
            public void NotifyChildReady() { }
            public void NotifyPreparingReturn() { }
            public void NotifyParentRestored() { }

            public void RefreshPersistent()
            {
                RefreshCount++;
                CoordinatorWasActiveAtRefresh = IsCoordinatorActive?.Invoke() == true;
            }
        }
    }
}
