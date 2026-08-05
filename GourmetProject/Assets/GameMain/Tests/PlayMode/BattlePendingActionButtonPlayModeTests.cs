using System.Collections;
using System.Linq;
using System.Reflection;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class BattlePendingActionButtonPlayModeTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);

            var random = new RandomService();
            random.Init("pending-action-button-tests");
            typeof(GameApp)
                .GetProperty(nameof(GameApp.Random))
                ?.GetSetMethod(nonPublic: true)
                ?.Invoke(null, new object[] { random });
        }

        [UnityTest]
        public IEnumerator BattleScene_WiresReusableComButtonPrefab()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;

            BattleWorldController world = Object.FindFirstObjectByType<BattleWorldController>(
                FindObjectsInactive.Include);
            Assert.That(world, Is.Not.Null);
            FieldInfo field = typeof(BattleWorldController).GetField(
                "_comButtonPrefab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var prefab = field.GetValue(world) as Button;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.gameObject.name, Is.EqualTo("ComButton"));

            Button instance = Object.Instantiate(prefab);
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.GetComponentInChildren<Button>(true), Is.Not.Null);
            Object.Destroy(instance.gameObject);

            AsyncOperation unload = SceneManager.UnloadSceneAsync("Battle");
            Assert.That(unload, Is.Not.Null);
            yield return unload;
        }

        [UnityTest]
        public IEnumerator PreplacedOutletDish_ShowsServeComButtonAndClickServes()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;

            BattleWorldController world = Object.FindFirstObjectByType<BattleWorldController>(
                FindObjectsInactive.Include);
            Assert.That(world, Is.Not.Null);
            string characterId = _tables.TbCharacter.DataList.First().Id;
            var run = new GameRun(_tables, _database, characterId, "pending-action-button-tests");
            BattleSession session = run.BuildBattleSession(0, string.Empty, "button-flow");

            world.Initialize(run, session, _ => { }, _ => { }, () => { }, null, null);
            Assert.That(session.PreparedServe, Is.Not.Null);
            session.ConfigureFoodDiscardLimit(1);
            int discardedOutletDishId = session.PreparedServe.Dish.Id;
            Assert.That(session.TryDiscardPreparedServe(), Is.True);
            Assert.That(world.EnsureNextDishPrepared(), Is.True);
            Assert.That(session.PreparedServe.Dish.Id, Is.Not.EqualTo(discardedOutletDishId));
            PreparedServeDish prepared = session.PreparedServe;
            Assert.That(session.PreplacePreparedServe(prepared.Placements[0]).Success, Is.True);
            world.SyncTableFromSession();
            yield return null;

            Button actionButton = world.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button.gameObject.name.StartsWith("PendingDishAction_"));
            Assert.That(actionButton, Is.Not.Null);
            Component label = actionButton.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(label, Is.Not.Null);
            Assert.That(
                label.GetType().GetProperty("text")?.GetValue(label) as string,
                Is.EqualTo("上菜"));
            DishPieceView pendingPiece = world.GetComponentsInChildren<DishPieceView>(true)
                .First(piece => piece.Instance?.Id == prepared.Dish.Id);
            Assert.That(
                actionButton.transform.position.y,
                Is.LessThan(pendingPiece.WorldBounds.min.y),
                "ComButton 应位于预摆食物下方。");

            actionButton.onClick.Invoke();
            yield return null;

            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.PreparedServe, Is.Not.Null, "确认后应立即自动补下一道菜。");

            var servedDish = session.DiningTable.Dishes.First();
            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(servedDish.Id, 1), Is.True);
            Assert.That(
                session.PreplaceTemporaryAreaDish(
                    servedDish.Id,
                    session.FindTemporaryAreaDishPlacements(servedDish.Id).First()),
                Is.True);
            world.SyncTableFromSession();
            yield return null;

            actionButton = world.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button.gameObject.name.StartsWith("PendingDishAction_"));
            Assert.That(actionButton, Is.Not.Null);
            label = actionButton.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(component => component.GetType().FullName == "TMPro.TextMeshProUGUI");
            Assert.That(
                label?.GetType().GetProperty("text")?.GetValue(label) as string,
                Is.EqualTo("确认"));

            actionButton.onClick.Invoke();
            yield return null;
            Assert.That(session.ServesUsed, Is.EqualTo(1), "临时桌菜确认不能增加上菜次数。");

            world.ClearBattleTable();
            AsyncOperation unload = SceneManager.UnloadSceneAsync("Battle");
            Assert.That(unload, Is.Not.Null);
            yield return unload;
        }
    }
}
