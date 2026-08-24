using System.IO;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class StarRatingTests
    {
        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/StarRating/";
        private const string BattlePrefabPath = "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string AwardPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/StarAwardForm.prefab";
        private const string BubblePrefabPath = "Assets/GameMain/Content/Resources/Prefabs/UI/Hud/TimelineNodeBubbleView.prefab";
        private const string ThemePath = "Assets/GameMain/Content/Resources/Sprites/UI/TimelineFresh/TimelineAxisTheme.asset";

        private static cfg.Tables _tables;
        private static GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadData()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void Award_IsIdempotentCappedAndRoundTripsPendingData()
        {
            GameRun run = CreateRun(1);
            Assert.That(run.RatingStarsEarned, Is.Zero);
            Assert.That(run.TryAwardRatingStar("boss-a"), Is.True);
            Assert.That(run.TryAwardRatingStar("boss-a"), Is.False);
            Assert.That(run.RatingStarsEarned, Is.EqualTo(1));
            Assert.That(run.GetPendingStarAward().AfterStars, Is.EqualTo(1));

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(
                _tables,
                _database,
                save,
                RunExecutionEnvironment.CreateIsolated(_tables, "star-test-restore"));
            Assert.That(restored.RatingStarsEarned, Is.EqualTo(1));
            Assert.That(restored.HasPendingStarAward, Is.True);
            Assert.That(restored.TryAwardRatingStar("boss-a"), Is.False);

            restored.ClearPendingStarAward();
            for (int i = 1; i < GameRun.MaxRatingStars; i++)
            {
                Assert.That(restored.TryAwardRatingStar("boss-" + i), Is.True);
                restored.ClearPendingStarAward();
            }

            Assert.That(restored.RatingStarsEarned, Is.EqualTo(GameRun.MaxRatingStars));
            Assert.That(restored.TryAwardRatingStar("boss-overflow"), Is.False);
            Assert.That(restored.HasPendingStarAward, Is.False);
        }

        [Test]
        public void LegacySave_MigratesFullWeeksAndSettledCurrentBossWithoutReplay()
        {
            GameRun run = CreateRun(3);
            RollTimeline(run);
            cfg.TimelineNode settledBoss = TimelineService.GetNodes(run)
                .First(node => TimelineService.IsRatingEvaluationNode(run, node));
            run.MarkNodeTriggered(settledBoss.Id);

            RunSaveData legacy = run.ToSaveData();
            legacy.StarProgressInitialized = false;
            legacy.RatingStarsEarned = 0;
            legacy.AwardedRatingStarBattleKeys.Clear();
            legacy.PendingStarAward = null;

            GameRun restored = GameRun.FromSaveData(
                _tables,
                _database,
                legacy,
                RunExecutionEnvironment.CreateIsolated(_tables, "star-test-legacy"));
            Assert.That(restored.RatingStarsEarned, Is.EqualTo(5));
            Assert.That(restored.HasPendingStarAward, Is.False);
            Assert.That(restored.ToSaveData().StarProgressInitialized, Is.True);
        }

        [Test]
        public void TimelineCandidatesAndDomainMutations_RejectRatingEvaluationNodes()
        {
            GameRun run = CreateRun(1);
            RollTimeline(run);
            cfg.TimelineNode boss = TimelineService.GetNodes(run)
                .First(node => TimelineService.IsRatingEvaluationNode(run, node));
            cfg.TimelineNode normal = TimelineService.GetNodes(run)
                .First(node => !TimelineService.IsRatingEvaluationNode(run, node));

            Assert.That(TimelineService.GetCloneableNodes(run).Any(node => node.Id == boss.Id), Is.False);
            Assert.That(TimelineService.GetDeletableUnsettledNodes(run).Any(node => node.Id == boss.Id), Is.False);
            Assert.That(TimelineService.GetCloneableNodes(run).Any(node => node.Id == normal.Id), Is.True);
            Assert.That(TimelineService.GetDeletableUnsettledNodes(run).Any(node => node.Id == normal.Id), Is.True);
            Assert.That(run.CloneRuntimeTimelineNodeToCurrentOrNextIntegerDay(boss.Id, "test"), Is.Empty);
            Assert.That(run.RemoveRuntimeTimelineNode(boss.Id), Is.False);
            Assert.That(run.CloneRuntimeTimelineNodeToCurrentOrNextIntegerDay(normal.Id, "test"), Is.Not.Empty);
            Assert.That(run.RemoveRuntimeTimelineNode(normal.Id), Is.True);
        }

        [Test]
        public void SixStarViewsAndPrefabs_HaveExpectedStatesAndReferences()
        {
            GameObject battle = AssetDatabase.LoadAssetAtPath<GameObject>(BattlePrefabPath);
            Assert.That(battle, Is.Not.Null);
            Assert.That(FindByName(battle.transform, "WeekText"), Is.Null);
            Transform starCard = FindByName(battle.transform, "StarCard");
            Assert.That(starCard, Is.Not.Null);
            StarProgressView hud = starCard.GetComponent<StarProgressView>();
            AssertProgressStates(hud);
            BattleInfoColumn info = battle.GetComponentInChildren<BattleInfoColumn>(true);
            Assert.That(new SerializedObject(info).FindProperty("_starProgress").objectReferenceValue, Is.SameAs(hud));

            GameObject award = AssetDatabase.LoadAssetAtPath<GameObject>(AwardPrefabPath);
            Assert.That(award, Is.Not.Null);
            StarAwardForm form = award.GetComponent<StarAwardForm>();
            StarProgressView awardProgress = award.GetComponentInChildren<StarProgressView>(true);
            Assert.That(form, Is.Not.Null);
            AssertProgressStates(awardProgress);
            var serialized = new SerializedObject(form);
            foreach (string field in new[]
                     {
                         "_transitionGroup", "_transitionPanel", "_titleText", "_messageText",
                         "_largeStar", "_glow", "_progress", "_continueButton",
                     })
            {
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            }
        }

        [Test]
        public void StarSprites_UsePolicyAndHaveTransparentAntialiasedEdges()
        {
            foreach (string name in new[] { "star_rating_medal.png", "timeline_node_star_bubble.png" })
            {
                string path = SpriteRoot + name;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, name);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), name);
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), name);
                Assert.That(importer.alphaIsTransparency, Is.True, name);
                Assert.That(importer.mipmapEnabled, Is.False, name);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), name);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), name);
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f), name);

                byte[] bytes = File.ReadAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert.That(ImageConversion.LoadImage(texture, bytes, false), Is.True, name);
                    Color32[] pixels = texture.GetPixels32();
                    Assert.That(pixels[0].a, Is.Zero, name + " corner");
                    Assert.That(pixels.Any(pixel => pixel.a > 0 && pixel.a < 255), Is.True, name + " antialias");
                    Assert.That(pixels.Any(pixel => pixel.a == 255), Is.True, name + " opaque center");
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        [Test]
        public void BossBubble_UsesStarShellAndKeepsItAfterSelectionStateClears()
        {
            TimelineAxisTheme theme = AssetDatabase.LoadAssetAtPath<TimelineAxisTheme>(ThemePath);
            TimelineNodeBubbleView prefab = AssetDatabase.LoadAssetAtPath<TimelineNodeBubbleView>(BubblePrefabPath);
            Assert.That(theme.BossNodeBubble, Is.Not.Null);
            TimelineNodeBubbleView instance = Object.Instantiate(prefab);
            try
            {
                instance.Initialize(theme);
                instance.Bind(null, completed: false, executing: false, boss: true, preview: false);
                Image shell = (Image)new SerializedObject(instance).FindProperty("_shell").objectReferenceValue;
                Assert.That(shell.sprite, Is.SameAs(theme.BossNodeBubble));
                instance.SetNodeTargetState(eligible: false, emphasized: false, destructive: true);
                instance.ClearNodeTargetState();
                Assert.That(shell.sprite, Is.SameAs(theme.BossNodeBubble));

                instance.Bind(null, completed: false, executing: false, boss: false, preview: false);
                Assert.That(shell.sprite, Is.SameAs(theme.NodeBubble));
            }
            finally
            {
                Object.DestroyImmediate(instance.gameObject);
            }
        }

        [Test]
        public void HeadlessSimulation_ConsumesStarAwardCallbackWithoutInfrastructureFailure()
        {
            string characterId = _tables.TbCharacter.DataList[0].Id;
            var simulator = new HeadlessRunSimulator(_tables, _database);
            AutoRunTrace trace = simulator.Run(new AutoRunRequest
            {
                CharacterId = characterId,
                PlayerLevel = AutoPlayerLevel.Normal,
                Seed = 260824,
            });

            Assert.That(trace.Termination, Is.Not.EqualTo(AutoRunTerminationKind.InfrastructureError), trace.FailureReason);
            Assert.That(trace.Stages.SelectMany(stage => stage.Battles).Any(), Is.True);
        }

        private static GameRun CreateRun(int week)
        {
            string characterId = _tables.TbCharacter.DataList[0].Id;
            return new GameRun(
                _tables,
                _database,
                characterId,
                "star-test-" + week,
                week,
                false,
                RunExecutionEnvironment.CreateIsolated(_tables, "star-test-" + week));
        }

        private static void RollTimeline(GameRun run)
        {
            IRandomStream rng = run.Random.DomainStream(SeedDomains.Map, "star-rating-test");
            TimelineService.RollWeekTimeline(run, rng);
        }

        private static void AssertProgressStates(StarProgressView progress)
        {
            Assert.That(progress, Is.Not.Null);
            Assert.That(progress.SlotCount, Is.EqualTo(GameRun.MaxRatingStars));
            progress.Bind(0);
            Assert.That(progress.GetStarRect(0).GetComponent<Image>().color.a, Is.LessThan(0.5f));
            progress.Bind(1);
            Assert.That(progress.GetStarRect(0).GetComponent<Image>().color.a, Is.EqualTo(1f));
            Assert.That(progress.GetStarRect(1).GetComponent<Image>().color.a, Is.LessThan(0.5f));
            progress.Bind(6);
            Assert.That(Enumerable.Range(0, 6).All(i =>
                progress.GetStarRect(i).GetComponent<Image>().color.a == 1f), Is.True);
        }

        private static Transform FindByName(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
        }
    }
}
