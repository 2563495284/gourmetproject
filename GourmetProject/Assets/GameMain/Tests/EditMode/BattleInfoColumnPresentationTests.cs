using System;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleInfoColumnPresentationTests
    {
        private const string BattlePrefabPath = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";

        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void Refresh_UsesPlaceholdersOutsideFoodAndBattleValuesInsideFood()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                BattleInfoColumn column = instance.transform
                    .Find("HudFrame/LeftColumn")
                    .GetComponent<BattleInfoColumn>();
                GameRun run = CreateRun();
                BattleSession session = CreateSession(requiredScore: 120);
                session.ConfigureFoodDiscardLimit(3);

                column.Refresh(run, session, GameplayView.ActionSelect, null);

                Assert.That(TextReference(column, "_weekText").text, Is.EqualTo("第一周"));
                Assert.That(TextReference(column, "_goldText").text, Is.EqualTo(run.Gold.ToString()));
                Assert.That(TextReference(column, "_scoreCurrentText").text, Is.EqualTo("-"));
                Assert.That(TextReference(column, "_scoreRequiredText").text, Is.EqualTo("-"));
                Assert.That(TextReference(column, "_discardCountText").text, Is.EqualTo("--"));

                column.Refresh(run, session, GameplayView.Food, null);

                Assert.That(TextReference(column, "_scoreCurrentText").text, Is.EqualTo("0"));
                Assert.That(TextReference(column, "_scoreRequiredText").text, Is.EqualTo("120"));
                Assert.That(TextReference(column, "_discardCountText").text, Is.EqualTo("03"));
                Assert.That(TextReference(column, "_viewTableCountText").text, Is.EqualTo("12"));
                Assert.That(TextReference(column, "_viewRecipeCountText").text,
                    Is.EqualTo(run.RecipeEntries.Count.ToString()));

                column.Refresh(run, session, GameplayView.TableView, null);

                Assert.That(TextReference(column, "_viewTableLabelText").text, Is.EqualTo("返回"));
                Assert.That(TextReference(column, "_viewTableCountText").gameObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RefreshHearts_MatchesCapacityAndSwapsActiveEmptySprites()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                BattleInfoColumn column = instance.transform
                    .Find("HudFrame/LeftColumn")
                    .GetComponent<BattleInfoColumn>();
                GameRun run = CreateRun();
                BattleSession session = CreateSession(requiredScore: 120);
                run.TryLoseHeart(out _, out _);

                column.Refresh(run, session, GameplayView.Food, null);

                var serialized = new SerializedObject(column);
                RectTransform container = serialized.FindProperty("_heartContainer").objectReferenceValue as RectTransform;
                Sprite active = serialized.FindProperty("_heartActiveSprite").objectReferenceValue as Sprite;
                Sprite empty = serialized.FindProperty("_heartEmptySprite").objectReferenceValue as Sprite;
                Assert.That(container.childCount, Is.EqualTo(3));
                Assert.That(container.GetChild(0).GetComponent<Image>().sprite, Is.EqualTo(active));
                Assert.That(container.GetChild(1).GetComponent<Image>().sprite, Is.EqualTo(active));
                Assert.That(container.GetChild(2).GetComponent<Image>().sprite, Is.EqualTo(empty));

                run.AdjustHeartCapacity(2);
                column.Refresh(run, session, GameplayView.Food, null);

                Assert.That(container.childCount, Is.EqualTo(5));
                Assert.That(container.GetChild(4).GetComponent<Image>().sprite, Is.EqualTo(empty));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "battle-info-column-presentation-tests");
        }

        private BattleSession CreateSession(int requiredScore)
        {
            return new BattleSession(
                new DiningTable(3, 4),
                _database,
                new Xoshiro256SS(1UL),
                Array.Empty<RecipeSlot>(),
                requiredScore);
        }

        private static Text TextReference(BattleInfoColumn column, string propertyName)
        {
            return new SerializedObject(column).FindProperty(propertyName).objectReferenceValue as Text;
        }
    }
}
