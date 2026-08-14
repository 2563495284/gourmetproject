#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.EditorTools
{
    /// <summary>仓库内固定的 Balance Lab 默认资源，以及可重复执行的生成入口。</summary>
    internal static class BalanceLabDefaultAssets
    {
        public const string DirectoryPath = "Assets/GameMain/Content/Balance";
        public const string MetaAffinityPath = DirectoryPath + "/DefaultMetaAffinityCatalog.asset";
        public const string ExampleScenarioPath = DirectoryPath + "/ExampleBalanceScenario.asset";

        public static MetaAffinityCatalog LoadMetaAffinity()
            => AssetDatabase.LoadAssetAtPath<MetaAffinityCatalog>(MetaAffinityPath);

        public static BalanceScenario LoadExampleScenario()
            => AssetDatabase.LoadAssetAtPath<BalanceScenario>(ExampleScenarioPath);

        [MenuItem("Tools/Gourmet/Balance Lab/重建默认资源")]
        public static void RebuildDefaultAssets()
        {
            try
            {
                var config = new ConfigService();
                config.LoadAll();
                cfg.Tables tables = config.Tables;
                GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);
                EnsureDirectory();

                MetaAffinityCatalog affinity = LoadMetaAffinity();
                if (affinity == null)
                {
                    affinity = ScriptableObject.CreateInstance<MetaAffinityCatalog>();
                    AssetDatabase.CreateAsset(affinity, MetaAffinityPath);
                }
                affinity.RebuildRecommended(tables);
                EditorUtility.SetDirty(affinity);

                BalanceScenario scenario = LoadExampleScenario();
                if (scenario == null)
                {
                    scenario = ScriptableObject.CreateInstance<BalanceScenario>();
                    AssetDatabase.CreateAsset(scenario, ExampleScenarioPath);
                }
                PopulateExampleScenario(scenario, tables, database);
                EditorUtility.SetDirty(scenario);

                List<string> affinityErrors = affinity.Validate(tables, requireAllConfigured: true);
                List<string> scenarioErrors = scenario.Checkpoints
                    .SelectMany(checkpoint => BuildCheckpointRuntimeFactory.Validate(tables, database, checkpoint))
                    .ToList();
                if (affinityErrors.Count > 0 || scenarioErrors.Count > 0)
                    throw new InvalidOperationException(string.Join("\n", affinityErrors.Concat(scenarioErrors)));

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = affinity;
                Debug.Log($"Balance Lab 默认资源已重建：{MetaAffinityPath}；{ExampleScenarioPath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("默认资源生成失败", exception.Message, "确定");
            }
        }

        private static void PopulateExampleScenario(
            BalanceScenario scenario,
            cfg.Tables tables,
            GameplayDatabase database)
        {
            cfg.Character character = tables.TbCharacter.DataList.FirstOrDefault(value => value != null)
                                      ?? throw new InvalidOperationException("没有可用于示例的经营方向。");
            var checkpoint = new BuildCheckpoint
            {
                Name = "W1 合法摆盘示例",
                CharacterId = character.Id,
                Week = 1,
                Day = 0f,
                Gold = 100,
                UseConfiguredRequiredScore = false,
                RequiredScore = 100,
            };

            BalanceRuntime runtime = BuildCheckpointRuntimeFactory.Create(
                tables,
                database,
                checkpoint,
                seed: 1001,
                applyPerturbation: false);
            int instanceId = 1;
            foreach (DishDef dish in database.AllDishes
                         .OrderBy(value => value.SortOrder)
                         .ThenBy(value => value.Id, StringComparer.Ordinal))
            {
                List<Placement> placements = runtime.Board.FindValidPlacements(dish);
                if (placements.Count == 0) continue;
                Placement placement = placements[0];
                checkpoint.Dishes.Add(new BuildReplayStep
                {
                    DishId = dish.Id,
                    X = placement.Origin.X,
                    Y = placement.Origin.Y,
                    Rotation = placement.RotationIndex,
                });
                IReadOnlyList<string> flavors = string.IsNullOrEmpty(dish.FlavorId)
                    ? Array.Empty<string>()
                    : new[] { dish.FlavorId };
                runtime.Board.Place(new DishInstance(instanceId++, dish, placement, dish.SkillIds, flavors));
                if (checkpoint.Dishes.Count >= 3) break;
            }

            if (checkpoint.Dishes.Count == 0)
                throw new InvalidOperationException("初始餐桌上没有可放置的示例食物。");
            scenario.BuildName = "GM 数值平衡示例";
            scenario.Checkpoints = new List<BuildCheckpoint> { checkpoint };
        }

        private static void EnsureDirectory()
        {
            if (AssetDatabase.IsValidFolder(DirectoryPath)) return;
            const string parent = "Assets/GameMain/Content";
            AssetDatabase.CreateFolder(parent, "Balance");
        }
    }
}
#endif
