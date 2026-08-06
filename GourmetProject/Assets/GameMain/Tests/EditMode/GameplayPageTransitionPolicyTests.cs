using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.Pages;
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class GameplayPageTransitionPolicyTests
    {
        [TestCase(GameplayView.ActionSelect, GameplayView.Shop, false)]
        [TestCase(GameplayView.Shop, GameplayView.RecipeInspect, false)]
        [TestCase(GameplayView.ActionSelect, GameplayView.Food, true)]
        [TestCase(GameplayView.Food, GameplayView.ActionSelect, true)]
        [TestCase(GameplayView.Food, GameplayView.TableView, true)]
        [TestCase(GameplayView.TableView, GameplayView.Food, true)]
        [TestCase(GameplayView.TableEdit, GameplayView.Shop, true)]
        public void RequiresWorldCover_UsesCoverExactlyForWorldBoundaries(
            GameplayView current,
            GameplayView next,
            bool expected)
        {
            Assert.That(GameplayPageRouter.RequiresWorldCover(current, next), Is.EqualTo(expected));
        }

        [TestCase(GameplayView.Food, true)]
        [TestCase(GameplayView.TableEdit, true)]
        [TestCase(GameplayView.TableView, true)]
        [TestCase(GameplayView.ActionSelect, false)]
        [TestCase(GameplayView.RewardDishPack, false)]
        public void IsWorldView_ClassifiesOnlyWorldSpacePages(GameplayView view, bool expected)
        {
            Assert.That(GameplayPageRouter.IsWorldView(view), Is.EqualTo(expected));
        }

        [TestCase(GameplayView.Shop, GameplayView.ActionSelect, true, true, false)]
        [TestCase(GameplayView.RewardItemChoice, GameplayView.ActionSelect, false, true, true)]
        [TestCase(GameplayView.ActionSelect, GameplayView.RewardDishPack, true, false, true)]
        [TestCase(GameplayView.RecipeInspect, GameplayView.Shop, true, true, false)]
        [TestCase(GameplayView.RecipeInspect, GameplayView.Shop, false, true, true)]
        [TestCase(GameplayView.Food, GameplayView.ActionSelect, false, true, true)]
        [TestCase(GameplayView.TableView, GameplayView.RecipeInspect, false, true, true)]
        public void RequiresCover_UsesBlackCoverForWorldOrAxisShellChanges(
            GameplayView current,
            GameplayView next,
            bool currentAxisVisible,
            bool nextAxisVisible,
            bool expected)
        {
            Assert.That(
                GameplayPageRouter.RequiresCover(current, next, currentAxisVisible, nextAxisVisible),
                Is.EqualTo(expected));
        }

        [TestCase(GameplayView.ActionSelect, false, true)]
        [TestCase(GameplayView.Shop, false, true)]
        [TestCase(GameplayView.Event, false, true)]
        [TestCase(GameplayView.RecipeInspect, true, true)]
        [TestCase(GameplayView.RecipeInspect, false, false)]
        [TestCase(GameplayView.RewardDishPack, true, false)]
        [TestCase(GameplayView.Food, true, false)]
        public void ShowsActionAxis_MatchesTargetShell(
            GameplayView view,
            bool recipeInspectShowsAxis,
            bool expected)
        {
            Assert.That(GameplayPageRouter.ShowsActionAxis(view, recipeInspectShowsAxis), Is.EqualTo(expected));
        }
    }

    public sealed class InspectionNavigationContextTests
    {
        [Test]
        public void RecipeAndTablePeers_PreserveFirstOriginAndActionSnapshot()
        {
            var context = new InspectionNavigationContext();
            var snapshot = new ActionSelectSnapshot(cardsActive: false);

            context.Capture(GameplayView.ActionSelect, snapshot);
            context.Capture(GameplayView.RecipeInspect, ActionSelectSnapshot.None);
            context.Capture(GameplayView.TableView, ActionSelectSnapshot.None);

            Assert.That(context.HasOrigin, Is.True);
            Assert.That(context.ReturnView, Is.EqualTo(GameplayView.ActionSelect));
            Assert.That(context.ActionSnapshot.HasSnapshot, Is.True);
            Assert.That(context.ActionSnapshot.CardsActive, Is.False);
        }

        [TestCase(GameplayView.Shop)]
        [TestCase(GameplayView.Event)]
        [TestCase(GameplayView.Food)]
        [TestCase(GameplayView.RewardDishPack)]
        [TestCase(GameplayView.RewardItemChoice)]
        [TestCase(GameplayView.RandomizedItems)]
        public void InspectionChain_ReturnsToItsStableOrigin(GameplayView origin)
        {
            var context = new InspectionNavigationContext();

            context.Capture(origin, ActionSelectSnapshot.None);
            context.Capture(GameplayView.RecipeInspect, ActionSelectSnapshot.None);
            context.Capture(GameplayView.TableView, ActionSelectSnapshot.None);

            Assert.That(context.ReturnView, Is.EqualTo(origin));

            context.Clear();
            Assert.That(context.HasOrigin, Is.False);
            Assert.That(context.ReturnView, Is.EqualTo(GameplayView.None));
        }
    }

    public sealed class RewardOperationSessionTests
    {
        [Test]
        public void NestedRandomizedResult_PreservesRootAndDeferredParentCompletion()
        {
            var session = new RewardOperationSession();
            int finishCount = 0;

            session.CaptureRoot(GameplayView.ActionSelect);
            session.CaptureRoot(GameplayView.RewardItemChoice);
            session.BeginRandomized(GameplayView.RewardItemChoice);
            Assert.That(
                session.TryDeferParentCompletion(
                    GameplayView.RewardItemChoice,
                    () => finishCount++),
                Is.True);

            RandomizedRewardResume resume = session.ConsumeRandomized();

            Assert.That(resume.ParentView, Is.EqualTo(GameplayView.RewardItemChoice));
            Assert.That(resume.ParentCompleted, Is.True);
            Assert.That(session.ConsumeRoot(), Is.EqualTo(GameplayView.ActionSelect));

            resume.ParentFinish?.Invoke();
            Assert.That(finishCount, Is.EqualTo(1));
        }

        [Test]
        public void RootReturnPage_IsCapturedOnlyOncePerRewardOperation()
        {
            var session = new RewardOperationSession();

            session.CaptureRoot(GameplayView.Event);
            session.CaptureRoot(GameplayView.Shop);
            session.CaptureRoot(GameplayView.RewardDishPack);

            Assert.That(session.ConsumeRoot(), Is.EqualTo(GameplayView.Event));
            Assert.That(session.ConsumeRoot(), Is.EqualTo(GameplayView.None));
        }
    }
}
