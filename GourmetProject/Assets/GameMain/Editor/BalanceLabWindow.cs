#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.EditorTools
{
    /// <summary>Build 成长、分布与要求分反推的编辑器数值实验室。</summary>
    public sealed class BalanceLabWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "快速使用", "高级编辑", "单次分析", "批量模拟", "目标美味值反推" };
        private BalanceScenario _scenario;
        private UnityEditor.Editor _scenarioEditor;
        private cfg.Tables _tables;
        private GameplayDatabase _database;
        private BalanceSimulationService _service;
        private int _tab;
        private int _checkpointIndex;
        private int _singleSeed = 1001;
        private int _sampleCount = 10000;
        private int _baseSeed = 1001;
        private Vector2 _scroll;
        private BalanceSampleResult _singleResult;
        private readonly List<BalanceStatistics> _statistics = new List<BalanceStatistics>();
        private readonly List<CompletedRun> _completedRuns = new List<CompletedRun>();
        private SimulationJob _job;
        private HeadlessRunSimulator _autoSimulator;
        private AutoSimulationJob _autoJob;
        private readonly List<AutoRunTrace> _autoTraces = new List<AutoRunTrace>();
        private int _characterIndex;
        private int _playerMode = 2;
        private bool _showAutoAdvanced;
        private AutoPlayerPolicy _autoPolicy = new AutoPlayerPolicy();
        private MetaAffinityCatalog _metaAffinity;

        [MenuItem("Tools/Gourmet/Balance Lab")]
        public static void Open() => GetWindow<BalanceLabWindow>("Balance Lab");

        private void OnEnable()
        {
            EditorApplication.update += TickJob;
            TryLoadConfig();
        }

        private void OnDisable()
        {
            EditorApplication.update -= TickJob;
            _job = null;
            if (_scenarioEditor != null) DestroyImmediate(_scenarioEditor);
        }

        private void OnGUI()
        {
            DrawHeader();
            if (_service == null)
            {
                EditorGUILayout.HelpBox("配置尚未加载。请确认 StreamingAssets/Config 已生成。", MessageType.Error);
                if (GUILayout.Button("重新加载配置")) TryLoadConfig();
                return;
            }

            _tab = GUILayout.Toolbar(_tab, Tabs);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawQuickTab(); break;
                case 1: DrawBuildTab(); break;
                case 2: DrawSingleTab(); break;
                case 3: DrawBatchTab(); break;
                case 4: DrawTargetTab(); break;
            }
            EditorGUILayout.EndScrollView();
            DrawJobStatus();
        }

        private void DrawQuickTab()
        {
            EditorGUILayout.LabelField("从开局直接模拟", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选择经营方向和玩家水平后直接运行。不需要启动游戏，也不需要手工摆 Build。", MessageType.Info);
            List<cfg.Character> characters = _tables.TbCharacter.DataList.Where(v => v != null).ToList();
            if (characters.Count == 0) { EditorGUILayout.HelpBox("经营方向配置为空。", MessageType.Error); return; }
            _characterIndex = EditorGUILayout.Popup("经营方向", Mathf.Clamp(_characterIndex, 0, characters.Count - 1), characters.Select(v => v.Name).ToArray());
            _playerMode = EditorGUILayout.Popup("玩家水平", _playerMode, new[] { "普通", "高手", "普通 + 高手" });
            _baseSeed = EditorGUILayout.IntField("基础种子", _baseSeed);
            _showAutoAdvanced = EditorGUILayout.Foldout(_showAutoAdvanced, "高级设置");
            if (_showAutoAdvanced)
            {
                _metaAffinity = (MetaAffinityCatalog)EditorGUILayout.ObjectField("局外路线倾向表", _metaAffinity, typeof(MetaAffinityCatalog), false);
                _autoPolicy.NormalBeamWidth = EditorGUILayout.IntSlider("普通束宽", _autoPolicy.NormalBeamWidth, 1, 64);
                _autoPolicy.ExpertBeamWidth = EditorGUILayout.IntSlider("高手束宽", _autoPolicy.ExpertBeamWidth, 1, 128);
                _autoPolicy.PlacementNodeBudget = EditorGUILayout.IntField("单战搜索预算", _autoPolicy.PlacementNodeBudget);
                _autoPolicy.InterestReserve = EditorGUILayout.IntField("利息保留本金", _autoPolicy.InterestReserve);
            }
            GUI.enabled = _autoJob == null && _job == null;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("快速模拟 200 局", GUILayout.Height(42))) StartAutoJob(characters[_characterIndex].Id, 200);
            if (GUILayout.Button("正式模拟 5000 局", GUILayout.Height(42))) StartAutoJob(characters[_characterIndex].Id, 5000);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            DrawAutoReport();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("复盘与规则验证（可选）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("只有想复现某个实际局面时，才需要下面的旧入口。", MessageType.None);

            GUI.enabled = EditorApplication.isPlaying && GameRunContext.Current != null && _job == null;
            if (GUILayout.Button("① 读取当前游戏局面", GUILayout.Height(36))) CaptureCurrentRun();
            GUI.enabled = true;
            if (EditorApplication.isPlaying && GameRunContext.Current == null)
                EditorGUILayout.HelpBox("游戏正在运行，但目前还没有进入一局。", MessageType.None);

            BuildCheckpoint checkpoint = CurrentCheckpoint();
            GUI.enabled = checkpoint != null && _job == null;
            if (GUILayout.Button("复制为下一阶段", GUILayout.Height(28))) DuplicateCheckpoint();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("快照快速测试（100次）", GUILayout.Height(30)))
            {
                _sampleCount = 100;
                StartJob(false, false);
            }
            if (GUILayout.Button("快照正式测试（10000次）", GUILayout.Height(30)))
            {
                _sampleCount = 10000;
                StartJob(false, false);
            }
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            if (checkpoint != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"当前阶段：{checkpoint.Name}　W{checkpoint.Week} D{checkpoint.Day:0.0}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"食物 {checkpoint.Dishes.Count}　装饰品和消耗品 {checkpoint.Items.Count}　金币 {checkpoint.Gold}");
                List<string> errors = BuildCheckpointRuntimeFactory.Validate(_tables, _database, checkpoint);
                DrawValidation(errors);
            }
            DrawStatisticsTable(_statistics);
        }

        private void DrawHeader()
        {
            EditorGUI.BeginChangeCheck();
            _scenario = (BalanceScenario)EditorGUILayout.ObjectField("Balance Scenario", _scenario, typeof(BalanceScenario), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (_scenarioEditor != null) DestroyImmediate(_scenarioEditor);
                _scenarioEditor = _scenario != null ? UnityEditor.Editor.CreateEditor(_scenario) : null;
                _checkpointIndex = 0;
                _singleResult = null;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建场景", GUILayout.Width(100))) CreateScenario();
            GUI.enabled = _scenario != null;
            if (GUILayout.Button("定位资源", GUILayout.Width(100))) EditorGUIUtility.PingObject(_scenario);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBuildTab()
        {
            if (_scenarioEditor == null)
            {
                EditorGUILayout.HelpBox("请选择或新建 BalanceScenario。", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("每个 Checkpoint 是一套固定盘面。Retain Probability、替代食物与成长系数仅在启用 Perturbation 时生效。", MessageType.Info);
            _scenarioEditor.OnInspectorGUI();
            if (GUILayout.Button("验证全部 Checkpoint")) ValidateAll(true);
        }

        private void DrawSingleTab()
        {
            BuildCheckpoint checkpoint = DrawCheckpointSelector();
            if (checkpoint == null) return;
            _singleSeed = EditorGUILayout.IntField("随机种子", _singleSeed);
            List<string> errors = BuildCheckpointRuntimeFactory.Validate(_tables, _database, checkpoint);
            DrawValidation(errors);
            GUI.enabled = errors.Count == 0 && _job == null;
            if (GUILayout.Button("运行单次正式结算")) _singleResult = _service.RunSample(checkpoint, _singleSeed);
            GUI.enabled = true;
            if (_singleResult != null) DrawSingleResult(_singleResult);
        }

        private void DrawBatchTab()
        {
            if (_scenario == null || _scenario.Checkpoints.Count == 0)
            {
                EditorGUILayout.HelpBox("场景中没有 Checkpoint。", MessageType.Info);
                return;
            }
            _sampleCount = EditorGUILayout.IntSlider("每阶段样本数", _sampleCount, 1, 10000);
            _baseSeed = EditorGUILayout.IntField("基础种子", _baseSeed);
            EditorGUILayout.HelpBox("预览建议 1,000；正式报告建议 10,000。任务按帧分批运行，可随时取消。", MessageType.Info);
            EditorGUILayout.HelpBox("被勾选组件的边际贡献使用最多 1,000 个同种子样本，控制分析耗时。", MessageType.None);
            GUI.enabled = _job == null;
            if (GUILayout.Button("运行当前 Checkpoint（清空旧结果）")) StartJob(false, false);
            if (GUILayout.Button("运行当前 Build 全阶段（清空旧结果）")) StartJob(true, false);
            if (GUILayout.Button("追加当前 Build 到对比结果")) StartJob(true, true);
            GUI.enabled = true;
            DrawStatisticsTable(BuildPooledStatistics());
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("各 Build 明细", EditorStyles.boldLabel);
            DrawStatisticsTable(_statistics);
        }

        private void DrawTargetTab()
        {
            if (_statistics.Count == 0)
            {
                EditorGUILayout.HelpBox("请先在“批量模拟”页运行样本。", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("要求分反推", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("普通取 P30（约 70% 通过），困难取 P50，挑战取 P75；数值按目标美味值曲线 RoundTo 向上取整。", MessageType.Info);
            DrawStatisticsTable(_statistics);
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导出 CSV")) ExportCsv();
            if (GUILayout.Button("导出 JSON")) ExportJson();
            EditorGUILayout.EndHorizontal();
        }

        private BuildCheckpoint DrawCheckpointSelector()
        {
            if (_scenario == null || _scenario.Checkpoints.Count == 0)
            {
                EditorGUILayout.HelpBox("场景中没有 Checkpoint。", MessageType.Info);
                return null;
            }
            string[] names = _scenario.Checkpoints.Select((c, i) => $"{i + 1}. {c.Name} (W{c.Week} D{c.Day:0.0})").ToArray();
            _checkpointIndex = EditorGUILayout.Popup("Checkpoint", Mathf.Clamp(_checkpointIndex, 0, names.Length - 1), names);
            return _scenario.Checkpoints[_checkpointIndex];
        }

        private BuildCheckpoint CurrentCheckpoint()
        {
            if (_scenario == null || _scenario.Checkpoints == null || _scenario.Checkpoints.Count == 0) return null;
            _checkpointIndex = Mathf.Clamp(_checkpointIndex, 0, _scenario.Checkpoints.Count - 1);
            return _scenario.Checkpoints[_checkpointIndex];
        }

        private void CaptureCurrentRun()
        {
            GameRun run = GameRunContext.Current;
            if (run == null) return;
            if (_scenario == null)
            {
                CreateScenario();
                if (_scenario == null) return;
                _scenario.BuildName = $"{run.CharacterId} Build";
            }

            RunSaveData save = run.ToSaveData();
            var checkpoint = new BuildCheckpoint
            {
                Name = $"第{run.WeekIndex}周 第{run.CurrentDay:0.0}天",
                CharacterId = run.CharacterId,
                Week = run.WeekIndex,
                Day = run.CurrentDay,
                Gold = run.Gold,
                UseConfiguredRequiredScore = true,
                RequiredScore = run.RequiredScore,
                HappyCakeLayers = 0,
            };
            foreach (RunItemSaveData item in save.Items)
                checkpoint.Items.Add(new BalanceItemEntry { ItemId = item.ItemId, Level = item.Level });
            checkpoint.TableFragmentIds.AddRange(save.TableFragmentIds ?? new List<string>());
            foreach (TableFragmentPlacementSaveData p in save.FragmentPlacements)
                checkpoint.FragmentPlacements.Add(new BalanceFragmentPlacement { FragmentId = p.FragmentId, Rotation = p.Rotation, X = p.OriginX, Y = p.OriginY });
            foreach (CellMaterialSaveData m in save.CellMaterialOverrides)
                checkpoint.CellMaterials.Add(new BalanceCellMaterial { X = m.X, Y = m.Y, MaterialId = m.MaterialId });

            BattleForm battle = Resources.FindObjectsOfTypeAll<BattleForm>().FirstOrDefault(v => v != null && v.gameObject.scene.IsValid() && v.Session != null);
            if (battle != null && battle.Session.DiningTable.DishCount > 0)
            {
                checkpoint.UseConfiguredRequiredScore = false;
                checkpoint.RequiredScore = battle.Session.RequiredScore;
                checkpoint.HappyCakeLayers = battle.Session.HappyCakeLayers;
                CapturePlacedDishes(checkpoint, battle.Session.DiningTable);
            }
            else
                AutoPlaceRecipe(checkpoint, run);

            Undo.RecordObject(_scenario, "Capture Balance Checkpoint");
            _scenario.Checkpoints.Add(checkpoint);
            _checkpointIndex = _scenario.Checkpoints.Count - 1;
            EditorUtility.SetDirty(_scenario);
            AssetDatabase.SaveAssets();
            _singleResult = null;
            Repaint();
        }

        private static void CapturePlacedDishes(BuildCheckpoint checkpoint, DiningTable board)
        {
            foreach (DishInstance dish in board.Dishes)
            {
                var extraSkills = dish.SkillIds.Where(id => !dish.Def.SkillIds.Contains(id)).ToList();
                var flavors = new List<string>(dish.FlavorIds);
                if (!string.IsNullOrEmpty(dish.Def.FlavorId)) flavors.Remove(dish.Def.FlavorId);
                checkpoint.Dishes.Add(new BuildReplayStep
                {
                    DishId = dish.Def.Id,
                    X = dish.Placement.Origin.X,
                    Y = dish.Placement.Origin.Y,
                    Rotation = dish.Placement.RotationIndex,
                    ExtraSkillIds = extraSkills,
                    ExtraFlavorIds = flavors,
                    PermanentFlat = dish.PermanentFlatBonus,
                    PermanentMultiplier = dish.PermanentMultBonus,
                });
            }
        }

        private static void AutoPlaceRecipe(BuildCheckpoint checkpoint, GameRun run)
        {
            DiningTable board = run.BuildTablePreviewFromFragments();
            int instanceId = 1;
            for (int i = 0; i < run.RecipeEntries.Count; i++)
            {
                RecipeBookSlot slot = run.RecipeEntries[i];
                DishDef def = run.Database.GetDish(slot.DishId);
                if (def == null) continue;
                List<Placement> placements = board.FindValidPlacements(def);
                if (placements.Count == 0) continue;
                Placement placement = placements[0];
                var flavors = new List<string>();
                if (!string.IsNullOrEmpty(def.FlavorId)) flavors.Add(def.FlavorId);
                flavors.AddRange(slot.ExtraFlavorIds);
                var skills = def.SkillIds.Concat(slot.ExtraSkillIds).Distinct().ToList();
                var instance = new DishInstance(instanceId++, def, placement, skills, flavors);
                instance.AddPermanentFlat(slot.ScoreFlatBonus);
                instance.MultiplyPermanentMult(slot.ScoreMultiplier);
                board.Place(instance);
                checkpoint.Dishes.Add(new BuildReplayStep
                {
                    DishId = def.Id,
                    X = placement.Origin.X,
                    Y = placement.Origin.Y,
                    Rotation = placement.RotationIndex,
                    ExtraSkillIds = new List<string>(slot.ExtraSkillIds),
                    ExtraFlavorIds = new List<string>(slot.ExtraFlavorIds),
                    PermanentFlat = slot.ScoreFlatBonus,
                    PermanentMultiplier = slot.ScoreMultiplier,
                });
            }
        }

        private void DuplicateCheckpoint()
        {
            BuildCheckpoint source = CurrentCheckpoint();
            if (source == null) return;
            string json = JsonUtility.ToJson(source);
            var copy = JsonUtility.FromJson<BuildCheckpoint>(json);
            copy.Name = source.Name + " - 下一阶段";
            Undo.RecordObject(_scenario, "Duplicate Balance Checkpoint");
            _scenario.Checkpoints.Add(copy);
            _checkpointIndex = _scenario.Checkpoints.Count - 1;
            EditorUtility.SetDirty(_scenario);
            AssetDatabase.SaveAssets();
        }

        private void DrawSingleResult(BalanceSampleResult sample)
        {
            if (!sample.IsValid)
            {
                EditorGUILayout.HelpBox(sample.FailureReason, MessageType.Error);
                return;
            }
            EditorGUILayout.LabelField($"总分：{sample.TotalScore}    金币变化：{sample.GoldDelta:0.##}", EditorStyles.boldLabel);
            foreach (BalanceDishContribution dish in sample.Dishes)
                EditorGUILayout.LabelField($"{dish.DishId}: {dish.Score:0.##}");
            if (sample.ScoreResult != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("正式 ScoreLines", EditorStyles.boldLabel);
                foreach (var line in sample.ScoreResult.ScoreLines)
                    EditorGUILayout.LabelField($"[{line.Phase}/{line.Kind}] {line.Message}  {line.Before:0.##} → {line.After:0.##}", EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawStatisticsTable(IEnumerable<BalanceStatistics> stats)
        {
            foreach (BalanceStatistics s in stats)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"{s.ScenarioName} / {s.CheckpointName}  W{s.Week} D{s.Day:0.0}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"有效 {s.ValidCount}/{s.SampleCount} ({s.ValidRate:P1})   均值 {s.Mean:0.##}   SD {s.StandardDeviation:0.##}   CV {s.CoefficientOfVariation:0.###}");
                EditorGUILayout.LabelField($"P10 {s.P10:0}  P25 {s.P25:0}  P50 {s.P50:0}  P75 {s.P75:0}  P90 {s.P90:0}  Max {s.Max:0}");
                EditorGUILayout.LabelField($"当前要求 {s.CurrentRequiredScore} / 通过率 {s.CurrentPassRate:P1}    建议：普通 {s.SuggestedNormal}  困难 {s.SuggestedHard}  挑战 {s.SuggestedChallenge}");
                foreach (BalanceComponentContribution c in s.ComponentContributions)
                {
                    MessageType type = c.MeanRatio > 0.25f ? MessageType.Warning : MessageType.None;
                    EditorGUILayout.HelpBox($"{c.ComponentId}: 平均边际 {c.MeanDelta:0.##}（{c.MeanRatio:P1}）", type);
                }
                foreach (string warning in s.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
        }

        private void StartJob(bool all, bool append)
        {
            if (_scenario == null) return;
            List<int> indices = all ? Enumerable.Range(0, _scenario.Checkpoints.Count).ToList() : new List<int> { Mathf.Clamp(_checkpointIndex, 0, _scenario.Checkpoints.Count - 1) };
            foreach (int index in indices)
            {
                List<string> errors = BuildCheckpointRuntimeFactory.Validate(_tables, _database, _scenario.Checkpoints[index]);
                if (errors.Count > 0)
                {
                    EditorUtility.DisplayDialog("无法开始模拟", $"{_scenario.Checkpoints[index].Name}\n{string.Join("\n", errors)}", "确定");
                    return;
                }
            }
            if (!append)
            {
                _statistics.Clear();
                _completedRuns.Clear();
            }
            _job = new SimulationJob { Scenario = _scenario, CheckpointIndices = indices, SampleCount = _sampleCount, BaseSeed = _baseSeed };
        }

        private void TickJob()
        {
            if (_autoJob != null)
            {
                int batch = _autoJob.SampleCount >= 5000 ? 2 : 5;
                for (int i = 0; i < batch && _autoJob.Cursor < _autoJob.SampleCount; i++, _autoJob.Cursor++)
                {
                    foreach (AutoPlayerLevel level in _autoJob.Levels)
                    {
                        _autoTraces.Add(_autoSimulator.Run(new AutoRunRequest
                        {
                            CharacterId = _autoJob.CharacterId,
                            PlayerLevel = level,
                            Seed = unchecked(_autoJob.BaseSeed + _autoJob.Cursor),
                            Policy = _autoPolicy,
                            MetaAffinity = _metaAffinity,
                        }));
                    }
                }
                if (_autoJob.Cursor >= _autoJob.SampleCount) _autoJob = null;
                Repaint();
                return;
            }
            if (_job == null || _job.Scenario == null || _service == null) return;
            const int batchSize = 25;
            int checkpointIndex = _job.CheckpointIndices[_job.CheckpointCursor];
            BuildCheckpoint checkpoint = _job.Scenario.Checkpoints[checkpointIndex];
            for (int i = 0; i < batchSize && _job.SampleCursor < _job.SampleCount; i++)
            {
                _job.Results.Add(_service.RunSample(checkpoint, unchecked(_job.BaseSeed + _job.SampleCursor)));
                _job.SampleCursor++;
            }
            if (_job.SampleCursor >= _job.SampleCount)
            {
                int roundTo = ResolveRoundTo(checkpoint.Week);
                BalanceStatistics stats = BalanceStatisticsCalculator.Calculate(_job.Scenario.BuildName, checkpoint, _job.Results, roundTo);
                stats.ComponentContributions = _service.CalculateComponentContributions(checkpoint, _job.Results);
                foreach (BalanceComponentContribution c in stats.ComponentContributions.Where(c => c.MeanRatio > 0.25f))
                    stats.Warnings.Add($"{c.ComponentId} 平均贡献率 {c.MeanRatio:P1}，超过 25%。");
                _statistics.Add(stats);
                _completedRuns.Add(new CompletedRun { ScenarioName = _job.Scenario.BuildName, Checkpoint = checkpoint, Samples = _job.Results });
                _job.CheckpointCursor++;
                _job.SampleCursor = 0;
                _job.Results = new List<BalanceSampleResult>(_job.SampleCount);
                if (_job.CheckpointCursor >= _job.CheckpointIndices.Count)
                {
                    AddCrossCheckpointWarnings();
                    _job = null;
                }
            }
            Repaint();
        }

        private void DrawJobStatus()
        {
            if (_autoJob != null)
            {
                float p = (float)_autoJob.Cursor / Math.Max(1, _autoJob.SampleCount);
                Rect autoRect = GUILayoutUtility.GetRect(1, 20, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(autoRect, p, $"自动玩家模拟中 {_autoJob.Cursor}/{_autoJob.SampleCount}");
                if (GUILayout.Button("取消自动模拟")) _autoJob = null;
                return;
            }
            if (_job == null) return;
            float overall = (_job.CheckpointCursor + (float)_job.SampleCursor / _job.SampleCount) / _job.CheckpointIndices.Count;
            Rect rect = GUILayoutUtility.GetRect(1, 20, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, overall, $"模拟中 {overall:P0}");
            if (GUILayout.Button("取消模拟")) _job = null;
        }

        private void AddCrossCheckpointWarnings()
        {
            foreach (IGrouping<string, BalanceStatistics> build in _statistics.GroupBy(v => v.ScenarioName))
            {
                List<BalanceStatistics> ordered = build.OrderBy(v => v.Week).ThenBy(v => v.Day).ToList();
                for (int i = 1; i < ordered.Count; i++)
                    if (ordered[i].P50 < ordered[i - 1].P50) ordered[i].Warnings.Add($"P50 低于上一阶段 {ordered[i - 1].CheckpointName}。");
            }
            foreach (IGrouping<string, BalanceStatistics> stage in _statistics.GroupBy(v => $"{v.Week}:{v.Day:0.###}"))
            {
                float center = stage.Average(v => v.P50);
                if (center <= 0f) continue;
                foreach (BalanceStatistics value in stage)
                {
                    float deviation = (value.P50 - center) / center;
                    if (Math.Abs(deviation) > 0.15f) value.Warnings.Add($"同阶段 Build 中位数偏差 {deviation:P1}，超过 ±15%。");
                }
            }
        }

        private List<BalanceStatistics> BuildPooledStatistics()
        {
            var pooled = new List<BalanceStatistics>();
            foreach (IGrouping<string, CompletedRun> group in _completedRuns.GroupBy(v => $"{v.Checkpoint.Week}:{v.Checkpoint.Day:0.###}"))
            {
                CompletedRun first = group.First();
                var checkpoint = new BuildCheckpoint
                {
                    Name = $"综合 W{first.Checkpoint.Week} D{first.Checkpoint.Day:0.0}",
                    Week = first.Checkpoint.Week,
                    Day = first.Checkpoint.Day,
                    RequiredScore = first.Checkpoint.RequiredScore,
                };
                pooled.Add(BalanceStatisticsCalculator.Calculate("全部 Build 合并", checkpoint, group.SelectMany(v => v.Samples).ToList(), ResolveRoundTo(checkpoint.Week)));
            }
            return pooled.OrderBy(v => v.Week).ThenBy(v => v.Day).ToList();
        }

        private void ValidateAll(bool showSuccess)
        {
            var errors = new List<string>();
            if (_scenario != null)
                foreach (BuildCheckpoint checkpoint in _scenario.Checkpoints)
                    errors.AddRange(BuildCheckpointRuntimeFactory.Validate(_tables, _database, checkpoint).Select(e => $"{checkpoint.Name}: {e}"));
            if (errors.Count > 0) EditorUtility.DisplayDialog("验证失败", string.Join("\n", errors), "确定");
            else if (showSuccess) EditorUtility.DisplayDialog("验证完成", "全部 Checkpoint 均合法。", "确定");
        }

        private static void DrawValidation(List<string> errors)
        {
            foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private int ResolveRoundTo(int week)
        {
            cfg.HiddenScoreCurve fallback = null;
            foreach (cfg.HiddenScoreCurve curve in _tables.TbHiddenScoreCurve.DataList)
            {
                if (curve.Purpose != cfg.HiddenScorePurpose.TargetScore) continue;
                fallback ??= curve;
                if (curve.WeekList.Count == 0 || curve.WeekList.Contains(week)) return Math.Max(1, curve.RoundTo);
            }
            return Math.Max(1, fallback?.RoundTo ?? 1);
        }

        private void TryLoadConfig()
        {
            try
            {
                var config = new ConfigService();
                config.LoadAll();
                _tables = config.Tables;
                _database = GameplayContentBuilder.BuildDatabase(_tables);
                _service = new BalanceSimulationService(_tables, _database);
                _autoSimulator = new HeadlessRunSimulator(_tables, _database);
            }
            catch (Exception e)
            {
                _service = null;
                Debug.LogException(e);
            }
        }

        private void CreateScenario()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建 Balance Scenario", "BalanceScenario", "asset", "选择保存位置");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<BalanceScenario>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _scenario = asset;
            _scenarioEditor = UnityEditor.Editor.CreateEditor(asset);
            Selection.activeObject = asset;
        }

        private void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("导出平衡报告", string.Empty, "balance-report.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder("configSummary,baseSeed,lastSeed,scenario,checkpoint,week,day,samples,validRate,mean,sd,cv,p10,p25,p50,p75,p90,max,currentRequired,currentPassRate,normal,hard,challenge\n");
            foreach (BalanceStatistics s in BuildPooledStatistics().Concat(_statistics))
            {
                sb.Append(Csv(ConfigSummary())).Append(',').Append(_baseSeed).Append(',').Append(unchecked(_baseSeed + Math.Max(0, s.SampleCount - 1))).Append(',')
                    .Append(Csv(s.ScenarioName)).Append(',').Append(Csv(s.CheckpointName)).Append(',').Append(s.Week).Append(',').Append(F(s.Day)).Append(',').Append(s.SampleCount).Append(',')
                    .Append(F(s.ValidRate)).Append(',').Append(F(s.Mean)).Append(',').Append(F(s.StandardDeviation)).Append(',').Append(F(s.CoefficientOfVariation)).Append(',')
                    .Append(F(s.P10)).Append(',').Append(F(s.P25)).Append(',').Append(F(s.P50)).Append(',').Append(F(s.P75)).Append(',').Append(F(s.P90)).Append(',').Append(F(s.Max)).Append(',')
                    .Append(s.CurrentRequiredScore).Append(',').Append(F(s.CurrentPassRate)).Append(',').Append(s.SuggestedNormal).Append(',').Append(s.SuggestedHard).Append(',').Append(s.SuggestedChallenge).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("导出平衡报告", string.Empty, "balance-report.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            var wrapper = new StatisticsList { GeneratedUtc = DateTime.UtcNow.ToString("O"), ConfigSummary = ConfigSummary(), Scenario = _scenario != null ? _scenario.name : string.Empty, BaseSeed = _baseSeed, Results = BuildPooledStatistics().Concat(_statistics).ToList() };
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true), new UTF8Encoding(false));
        }

        private static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string ConfigSummary()
        {
            string root = Path.Combine(Application.streamingAssetsPath, "Config");
            if (!Directory.Exists(root)) return "config-missing";
            FileInfo[] files = new DirectoryInfo(root).GetFiles("*.json").OrderBy(v => v.Name, StringComparer.Ordinal).ToArray();
            long length = files.Sum(v => v.Length);
            long latest = files.Length > 0 ? files.Max(v => v.LastWriteTimeUtc.Ticks) : 0L;
            return $"json:{files.Length}:bytes:{length}:utcTicks:{latest}";
        }

        [Serializable]
        private sealed class StatisticsList
        {
            public string GeneratedUtc;
            public string ConfigSummary;
            public string Scenario;
            public int BaseSeed;
            public List<BalanceStatistics> Results;
        }

        private sealed class SimulationJob
        {
            public BalanceScenario Scenario;
            public List<int> CheckpointIndices;
            public int CheckpointCursor;
            public int SampleCount;
            public int SampleCursor;
            public int BaseSeed;
            public List<BalanceSampleResult> Results = new List<BalanceSampleResult>();
        }

        private sealed class CompletedRun
        {
            public string ScenarioName;
            public BuildCheckpoint Checkpoint;
            public List<BalanceSampleResult> Samples;
        }

        private void StartAutoJob(string characterId, int samples)
        {
            _autoTraces.Clear();
            var levels = new List<AutoPlayerLevel>();
            if (_playerMode == 0 || _playerMode == 2) levels.Add(AutoPlayerLevel.Normal);
            if (_playerMode == 1 || _playerMode == 2) levels.Add(AutoPlayerLevel.Expert);
            _autoJob = new AutoSimulationJob { CharacterId = characterId, SampleCount = samples, BaseSeed = _baseSeed, Levels = levels };
        }

        private void DrawAutoReport()
        {
            if (_autoTraces.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("自动玩家报告", EditorStyles.boldLabel);
            foreach (IGrouping<AutoPlayerLevel, AutoRunTrace> group in _autoTraces.GroupBy(v => v.PlayerLevel))
            {
                List<AutoRunTrace> runs = group.ToList();
                List<AutoRunStageTrace> stages = runs.SelectMany(v => v.Stages).ToList();
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(group.Key == AutoPlayerLevel.Normal ? "普通玩家" : "高手玩家", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"局数 {runs.Count}　完成率 {runs.Count(v => v.Completed) / (float)runs.Count:P1}　平均转向 {runs.Average(v => v.ArchetypeChanges):0.00}");
                if (stages.Count > 0)
                {
                    EditorGUILayout.LabelField($"流派：甜蜜 {stages.Count(v => v.Archetype == BuildArchetype.SweetTransfer)/(float)stages.Count:P1}　数量 {stages.Count(v => v.Archetype == BuildArchetype.Count)/(float)stages.Count:P1}　蛋糕 {stages.Count(v => v.Archetype == BuildArchetype.Cake)/(float)stages.Count:P1}");
                    EditorGUILayout.LabelField($"摆盘截断 {stages.Count(v => v.SolverTruncated)/(float)stages.Count:P1}　无合法方案 {stages.Count(v => v.NoLegalPlacement)/(float)stages.Count:P1}");
                    foreach (IGrouping<int, AutoRunStageTrace> week in stages.GroupBy(v => v.Week).OrderBy(v => v.Key))
                    {
                        List<AutoRunStageTrace> bossRuns = week.Where(v => v.BossReached).ToList();
                        int[] scores = (bossRuns.Count > 0 ? bossRuns.Select(v => v.BossScore) : week.Select(v => v.Score)).OrderBy(v => v).ToArray();
                        int p10 = scores[Mathf.Clamp(Mathf.FloorToInt((scores.Length - 1) * .1f), 0, scores.Length - 1)];
                        int p50 = scores[(scores.Length - 1) / 2];
                        int p90 = scores[Mathf.Clamp(Mathf.CeilToInt((scores.Length - 1) * .9f), 0, scores.Length - 1)];
                        int suggested = scores[Mathf.Clamp(Mathf.FloorToInt((scores.Length - 1) * .3f), 0, scores.Length - 1)];
                        int mealCount = week.Sum(v => v.MealBattles);
                        int mealPasses = week.Sum(v => v.MealPasses);
                        float mealRate = mealCount > 0 ? mealPasses / (float)mealCount : 0f;
                        float bossReach = bossRuns.Count / (float)week.Count();
                        float bossPass = bossRuns.Count > 0 ? bossRuns.Count(v => v.BossPassed) / (float)bossRuns.Count : 0f;
                        EditorGUILayout.LabelField($"W{week.Key} Boss：到达 {bossReach:P1}　通过 {bossPass:P1}　P10 {p10} / P50 {p50} / P90 {p90}　建议要求 {suggested}");
                        EditorGUILayout.LabelField($"　日常营业：{mealPasses}/{mealCount}（{mealRate:P1}）　金币结余 {week.Average(v => v.GoldBalance):0}");
                        if (bossReach < 0.01f)
                            EditorGUILayout.HelpBox($"W{week.Key} 没有样本到达 Boss；本周星级评鉴 分位数和建议要求分无效。请先检查日常营业目标曲线或成长供给。", MessageType.Error);
                        else if (bossReach < 0.5f)
                            EditorGUILayout.HelpBox($"W{week.Key} 只有 {bossReach:P1} 到达 Boss，Boss 样本存在明显幸存者偏差。", MessageType.Warning);
                        if (mealCount > 0 && mealRate < 0.3f)
                            EditorGUILayout.HelpBox($"W{week.Key} 日常营业通过率仅 {mealRate:P1}，多数样本会在见到 Boss 前耗尽红心。", MessageType.Warning);
                    }
                    int activeUses = stages.Sum(v => v.ActiveItemsUsed.Count);
                    float activePerRun = runs.Count > 0 ? activeUses / (float)runs.Count : 0f;
                    EditorGUILayout.LabelField($"消耗品使用 {activeUses} 次（每局 {activePerRun:0.00}）");
                    EditorGUILayout.LabelField($"局外路线：正常 {stages.Count(v => v.MetaRoute == MetaRoute.Normal)/(float)stages.Count:P1}　事件 {stages.Count(v => v.MetaRoute == MetaRoute.Event)/(float)stages.Count:P1}　利息 {stages.Count(v => v.MetaRoute == MetaRoute.Interest)/(float)stages.Count:P1}　商店 {stages.Count(v => v.MetaRoute == MetaRoute.Shop)/(float)stages.Count:P1}");
                }
                EditorGUILayout.EndVertical();
            }
        }

        private sealed class AutoSimulationJob
        {
            public string CharacterId;
            public int SampleCount;
            public int Cursor;
            public int BaseSeed;
            public List<AutoPlayerLevel> Levels;
        }
    }
}
#endif
