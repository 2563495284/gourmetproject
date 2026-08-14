#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BreakInfinity;
using GourmetProject.Gameplay.Scoring;
using System.Text;
using GourmetProject.Config;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Balance;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        private BalanceConfigFingerprint _loadedConfigFingerprint;
        private BalanceConfigFingerprint _observedConfigFingerprint;
        private string _configLoadedUtc = string.Empty;
        private string _configLoadError = string.Empty;
        private bool _configStale;
        private double _nextConfigObservationTime;
        private AutoRunReport _lastAutoReport;
        private string _lastAutoStatus = string.Empty;
        private string _lastExportPath = string.Empty;

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
            _autoJob = null;
            if (_scenarioEditor != null) DestroyImmediate(_scenarioEditor);
        }

        private void OnInspectorUpdate()
        {
            bool wasStale = _configStale;
            string previousHash = _observedConfigFingerprint?.ContentHash;
            ObserveConfigOnDisk(false);
            if (wasStale != _configStale
                || !string.Equals(previousHash, _observedConfigFingerprint?.ContentHash, StringComparison.Ordinal))
                Repaint();
        }

        private void OnGUI()
        {
            ObserveConfigOnDisk(false);
            DrawConfigStatus();
            if (_service == null)
            {
                return;
            }

            _tab = GUILayout.Toolbar(_tab, Tabs);
            if (_tab != 0) DrawHeader();
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
            int selectedLevelCount = _playerMode == 2 ? 2 : 1;
            EditorGUILayout.LabelField($"固定解锁档案：{AutoRunReport.AllUnlockedProfileId}");
            EditorGUILayout.LabelField(
                "路线档案",
                _metaAffinity != null ? _metaAffinity.ProfileId : "—（缺失，正式运行已禁用）");
            _showAutoAdvanced = EditorGUILayout.Foldout(_showAutoAdvanced, "高级设置");
            if (_showAutoAdvanced)
            {
                _metaAffinity = (MetaAffinityCatalog)EditorGUILayout.ObjectField("局外路线倾向表", _metaAffinity, typeof(MetaAffinityCatalog), false);
                List<string> affinityErrors = ValidateMetaAffinity();
                foreach (string error in affinityErrors.Take(6))
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                if (affinityErrors.Count > 6)
                    EditorGUILayout.LabelField($"另有 {affinityErrors.Count - 6} 条路线档案错误。", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("选择仓库默认档案", GUILayout.Width(140)))
                    _metaAffinity = BalanceLabDefaultAssets.LoadMetaAffinity();
                if (GUILayout.Button("重建默认资源", GUILayout.Width(120)))
                {
                    BalanceLabDefaultAssets.RebuildDefaultAssets();
                    _metaAffinity = BalanceLabDefaultAssets.LoadMetaAffinity();
                }
                EditorGUILayout.EndHorizontal();
                _autoPolicy.NormalBeamWidth = EditorGUILayout.IntSlider("普通候选上限", _autoPolicy.NormalBeamWidth, 1, 24);
                _autoPolicy.ExpertBeamWidth = EditorGUILayout.IntSlider("高手候选上限", _autoPolicy.ExpertBeamWidth, 1, 64);
                _autoPolicy.PlacementNodeBudget = EditorGUILayout.IntField("单战正式预览预算", _autoPolicy.PlacementNodeBudget);
                _autoPolicy.InterestReserve = EditorGUILayout.IntField("利息保留本金", _autoPolicy.InterestReserve);
                if (GUILayout.Button("恢复推荐默认值", GUILayout.Width(140))) _autoPolicy = new AutoPlayerPolicy();
            }
            bool canStart = CanStartAutoSimulation();
            if (!canStart && _job == null && _autoJob == null && ValidateMetaAffinity().Count > 0)
                EditorGUILayout.HelpBox("默认路线档案缺失或与当前配置不一致；展开高级设置查看详情，或重建默认资源。", MessageType.Error);
            GUI.enabled = canStart;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"快速验证：每档 200 局（共 {200 * selectedLevelCount}）", GUILayout.Height(42)))
                StartAutoJob(characters[_characterIndex], 200);
            if (GUILayout.Button($"正式报告：每档 5000 局（共 {5000 * selectedLevelCount}）", GUILayout.Height(42)))
                StartAutoJob(characters[_characterIndex], 5000);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            if (_configStale)
                EditorGUILayout.HelpBox("磁盘配置已变化。请先在顶部重新加载，避免新旧配置混用。", MessageType.Warning);
            DrawAutoReport();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("复盘与规则验证（可选）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("只有想复现某个实际局面时，才需要下面的旧入口。", MessageType.None);

            GUI.enabled = EditorApplication.isPlaying && GameRunContext.Current != null && _job == null && _autoJob == null;
            if (GUILayout.Button("① 读取当前游戏局面", GUILayout.Height(36))) CaptureCurrentRun();
            GUI.enabled = true;
            if (EditorApplication.isPlaying && GameRunContext.Current == null)
                EditorGUILayout.HelpBox("游戏正在运行，但目前还没有进入一局。", MessageType.None);

            BuildCheckpoint checkpoint = CurrentCheckpoint();
            GUI.enabled = checkpoint != null && _job == null && _autoJob == null && !_configStale;
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
            GUI.enabled = errors.Count == 0 && _job == null && _autoJob == null && !_configStale;
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
            GUI.enabled = _job == null && _autoJob == null && !_configStale;
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
                checkpoint.Items.Add(new BalanceItemEntry
                {
                    ItemId = item.ItemId,
                    Level = item.Level,
                    StateJson = item.StateJson ?? string.Empty,
                });
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
                    PermanentFlat = BigNumberSaveData.ToLegacyFloat(dish.PermanentFlatBonus),
                    PermanentMultiplier = BigNumberSaveData.ToLegacyFloat(dish.PermanentMultBonus),
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
                    PermanentFlat = BigNumberSaveData.ToLegacyFloat(slot.ScoreFlatBonus),
                    PermanentMultiplier = BigNumberSaveData.ToLegacyFloat(slot.ScoreMultiplier),
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
            if (_scenario == null || _service == null || _configStale || _autoJob != null || _job != null) return;
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
                AutoSimulationJob job = _autoJob;
                if (job.CancelRequested)
                {
                    if (job.CurrentSession == null && job.Cursor < job.SampleCount)
                        job.CurrentSession = CreateAutoSession(job);
                    if (job.CurrentSession != null)
                    {
                        job.CurrentSession.Cancel();
                        AddCompletedAutoSession(job);
                    }
                    FinishAutoJob(true);
                    Repaint();
                    return;
                }

                // 每次仅推进一个正式流程回调；50ms 后不再启动新回调，给编辑器 UI 留出时间。
                // 单个回调本身不可抢占，因此以 100ms 作为异常帧诊断硬阈值。
                var frame = Stopwatch.StartNew();
                do
                {
                    if (job.CurrentSession == null)
                        job.CurrentSession = CreateAutoSession(job);
                    if (job.CurrentSession.Step(1))
                        AddCompletedAutoSession(job);

                    if (job.Cursor >= job.SampleCount)
                    {
                        FinishAutoJob(false);
                        break;
                    }

                    if (frame.Elapsed.TotalMilliseconds >= 100d)
                    {
                        Debug.LogWarning($"Balance Lab 单帧自动模拟耗时 {frame.Elapsed.TotalMilliseconds:0.0}ms，超过 100ms 硬阈值。");
                        break;
                    }
                }
                while (frame.Elapsed.TotalMilliseconds < 50d && _autoJob == job);
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
                int total = _autoJob.SampleCount * _autoJob.Levels.Count;
                int completed = _autoTraces.Count;
                float p = completed / (float)Math.Max(1, total);
                Rect autoRect = GUILayoutUtility.GetRect(1, 20, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(autoRect, p, $"自动玩家模拟中 {completed}/{total}（每档 {_autoJob.Cursor}/{_autoJob.SampleCount}）");
                if (GUILayout.Button("取消并保留部分报告")) RequestCancelAutoJob();
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

        private static string ConfigDirectory()
            => Path.Combine(Application.streamingAssetsPath, "Config");

        private void DrawConfigStatus()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("生成配置", EditorStyles.boldLabel);
            bool busy = _autoJob != null || _job != null;
            GUI.enabled = !busy;
            if (GUILayout.Button("重新加载", GUILayout.Width(100))) TryLoadConfig();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_service != null && _loadedConfigFingerprint?.IsValid == true)
            {
                string state = _configStale ? "磁盘内容已变化，等待重新加载" : "已加载且与磁盘一致";
                EditorGUILayout.HelpBox(
                    $"{state}\nSHA-256 {_loadedConfigFingerprint.ShortHash}… · {_loadedConfigFingerprint.FileCount} 个 JSON · 加载于 {_configLoadedUtc}",
                    _configStale ? MessageType.Warning : MessageType.Info);
                if (_lastAutoReport != null
                    && !string.Equals(_lastAutoReport.ConfigContentHash, _loadedConfigFingerprint.ContentHash, StringComparison.OrdinalIgnoreCase))
                    EditorGUILayout.HelpBox("当前展示的自动玩家报告来自另一个配置哈希；导出仍会保留原始哈希。", MessageType.Warning);
            }
            else
            {
                string error = string.IsNullOrEmpty(_configLoadError)
                    ? "配置尚未加载。请先运行 GameConfig/gen.command 生成配置。"
                    : _configLoadError;
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            EditorGUILayout.LabelField("数据源", ConfigDirectory());
            if (busy) EditorGUILayout.LabelField("模拟运行期间不能重新加载配置。", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void ObserveConfigOnDisk(bool force)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now < _nextConfigObservationTime) return;
            _nextConfigObservationTime = now + 1d;
            _observedConfigFingerprint = BalanceConfigFingerprint.Compute(ConfigDirectory());
            _configStale = _loadedConfigFingerprint?.IsValid == true
                           && (!_observedConfigFingerprint.IsValid
                               || !_loadedConfigFingerprint.HasSameContent(_observedConfigFingerprint));
            RefreshLastReportValidity();
        }

        private void TryLoadConfig()
        {
            if (_autoJob != null || _job != null) return;
            try
            {
                BalanceConfigFingerprint before = BalanceConfigFingerprint.Compute(ConfigDirectory());
                if (!before.IsValid) throw new InvalidOperationException(before.Error);
                var config = new ConfigService();
                config.LoadAll();
                BalanceConfigFingerprint after = BalanceConfigFingerprint.Compute(ConfigDirectory());
                if (!after.IsValid) throw new InvalidOperationException(after.Error);
                if (!before.HasSameContent(after))
                    throw new InvalidOperationException("配置在加载过程中发生变化，请重新加载一次。");
                _tables = config.Tables;
                _database = GameplayContentBuilder.BuildDatabase(_tables);
                _service = new BalanceSimulationService(_tables, _database);
                _autoSimulator = new HeadlessRunSimulator(_tables, _database);
                if (_metaAffinity == null)
                    _metaAffinity = BalanceLabDefaultAssets.LoadMetaAffinity();
                _loadedConfigFingerprint = after;
                _observedConfigFingerprint = after;
                _configLoadedUtc = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _configLoadError = string.Empty;
                _configStale = false;
                _nextConfigObservationTime = EditorApplication.timeSinceStartup + 1d;
                RefreshLastReportValidity();
            }
            catch (Exception e)
            {
                _service = null;
                _autoSimulator = null;
                _tables = null;
                _database = null;
                _configLoadError = $"配置加载失败：{e.Message}";
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
            BalanceConfigFingerprint fingerprint = BalanceConfigFingerprint.Compute(ConfigDirectory());
            return fingerprint.IsValid
                ? $"sha256:{fingerprint.ContentHash}:json:{fingerprint.FileCount}:bytes:{fingerprint.TotalBytes}"
                : $"config-error:{fingerprint.Error}";
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

        private bool CanStartAutoSimulation()
        {
            return _service != null
                   && _autoSimulator != null
                   && _loadedConfigFingerprint?.IsValid == true
                   && !_configStale
                   && _autoJob == null
                   && _job == null
                   && ValidateMetaAffinity().Count == 0;
        }

        private void StartAutoJob(cfg.Character character, int samples)
        {
            if (!CanStartAutoSimulation() || character == null || samples <= 0)
            {
                EditorUtility.DisplayDialog("无法开始模拟", "请确认配置已加载、磁盘配置没有变化，并且当前没有其它模拟任务。", "确定");
                return;
            }

            var levels = new List<AutoPlayerLevel>();
            if (_playerMode == 0 || _playerMode == 2) levels.Add(AutoPlayerLevel.Normal);
            if (_playerMode == 1 || _playerMode == 2) levels.Add(AutoPlayerLevel.Expert);
            if (levels.Count == 0) return;

            _autoTraces.Clear();
            _lastAutoReport = null;
            _lastAutoStatus = string.Empty;
            _lastExportPath = string.Empty;
            _autoJob = new AutoSimulationJob
            {
                CharacterId = character.Id,
                CharacterName = character.Name,
                TotalWeeks = Math.Max(1, _tables.TbGameBase.TotalWeeks),
                SampleCount = samples,
                BaseSeed = _baseSeed,
                Levels = levels,
                Policy = ClonePolicy(_autoPolicy),
                MetaAffinity = EffectiveMetaAffinity(),
                MetaAffinityProfileId = ResolveMetaAffinityProfileId(),
                MetaAffinityProfileHash = _metaAffinity.ComputeContentHash(),
                UnlockProfileHash = AutoRunProfileFingerprint.ComputeAllUnlocked(_tables),
                PolicyVersion = AutoRunPolicySeed.Version,
                ConfigContentHash = _loadedConfigFingerprint.ContentHash,
                ConfigFileCount = _loadedConfigFingerprint.FileCount,
                ConfigLoadedUtc = _configLoadedUtc,
                StartedUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        private HeadlessRunSession CreateAutoSession(AutoSimulationJob job)
        {
            AutoPlayerLevel level = job.Levels[Mathf.Clamp(job.LevelIndex, 0, job.Levels.Count - 1)];
            return _autoSimulator.StartSession(new AutoRunRequest
            {
                CharacterId = job.CharacterId,
                PlayerLevel = level,
                Seed = unchecked(job.BaseSeed + job.Cursor),
                Policy = job.Policy,
                MetaAffinity = job.MetaAffinity,
            });
        }

        private void AddCompletedAutoSession(AutoSimulationJob job)
        {
            if (job?.CurrentSession == null) return;
            _autoTraces.Add(job.CurrentSession.Trace);
            job.CurrentSession = null;
            job.LevelIndex++;
            if (job.LevelIndex < job.Levels.Count) return;
            job.LevelIndex = 0;
            job.Cursor++;
        }

        private void RequestCancelAutoJob()
        {
            if (_autoJob == null) return;
            _autoJob.CancelRequested = true;
            _autoJob.CurrentSession?.Cancel();
        }

        private void FinishAutoJob(bool cancelled)
        {
            AutoSimulationJob job = _autoJob;
            if (job == null) return;
            string finished = DateTime.UtcNow.ToString("O");
            _lastAutoReport = AutoRunReportBuilder.Build(new AutoRunReportRequest
            {
                GeneratedUtc = finished,
                StartedUtc = job.StartedUtc,
                FinishedUtc = finished,
                ApplicationVersion = Application.version,
                UnityVersion = Application.unityVersion,
                ConfigContentHash = job.ConfigContentHash,
                ConfigFileCount = job.ConfigFileCount,
                ConfigLoadedUtc = job.ConfigLoadedUtc,
                ConfigStale = _configStale,
                CharacterId = job.CharacterId,
                CharacterName = job.CharacterName,
                TotalWeeks = job.TotalWeeks,
                BaseSeed = job.BaseSeed,
                RequestedRunsPerLevel = job.SampleCount,
                Cancelled = cancelled,
                UnlockProfileId = AutoRunReport.AllUnlockedProfileId,
                UnlockProfileHash = job.UnlockProfileHash,
                MetaAffinityProfileId = job.MetaAffinityProfileId,
                MetaAffinityProfileHash = job.MetaAffinityProfileHash,
                PolicyVersion = job.PolicyVersion,
                Policy = job.Policy,
                Levels = job.Levels,
            }, _autoTraces);
            _lastAutoStatus = cancelled
                ? $"已取消并保留部分报告：{_lastAutoReport.ActualTraceCount}/{_lastAutoReport.RequestedTraceCount} 条 trace。"
                : $"模拟完成：{_lastAutoReport.ActualTraceCount}/{_lastAutoReport.RequestedTraceCount} 条 trace。";
            _autoJob = null;
        }

        private void DrawAutoReport()
        {
            if (_autoJob != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"正在生成报告：{_autoTraces.Count}/{_autoJob.SampleCount * _autoJob.Levels.Count} 条 trace。", EditorStyles.miniLabel);
                return;
            }
            if (_lastAutoReport == null) return;

            AutoRunReport report = _lastAutoReport;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("自动玩家报告", EditorStyles.boldLabel);
            MessageType reportType = report.HasRuntimeErrors || report.HasUnsupportedMechanics || report.ConfigStale
                ? MessageType.Error
                : report.Partial ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(
                $"{_lastAutoStatus}\n配置 {ShortHash(report.ConfigContentHash)}… · " +
                $"解锁档案 {report.UnlockProfileId}@{ShortHash(report.UnlockProfileHash)}… · " +
                $"路线档案 {report.MetaAffinityProfileId}@{ShortHash(report.MetaAffinityProfileHash)}… · " +
                $"策略 {report.PolicyVersion}",
                reportType);
            if (report.HasRuntimeErrors)
                EditorGUILayout.HelpBox("存在运行异常，本报告不可作为正式平衡结论；请先按下方 Seed 复现并修复。", MessageType.Error);
            else if (report.Partial)
                EditorGUILayout.HelpBox("这是部分报告，可以导出用于排查，但不能与完整样本报告等价比较。", MessageType.Warning);
            if (report.HasUnsupportedMechanics)
                EditorGUILayout.HelpBox("存在规则不支持样本，本报告不可用于推荐数值。", MessageType.Error);
            if (report.ConfigStale)
                EditorGUILayout.HelpBox("当前配置已变化，旧报告已标为过期；分位数保留，但推荐要求分已关闭。", MessageType.Error);

            foreach (AutoRunLevelSummary level in report.LevelSummaries)
            {
                EditorGUILayout.BeginVertical("box");
                string levelName = level.PlayerLevel == AutoPlayerLevel.Normal ? "普通玩家" : "高手玩家";
                EditorGUILayout.LabelField(levelName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"局数 {level.ActualRuns}/{level.RequestedRuns}　整局完成 {level.CompletedRuns}（{level.CompletionRate:P1}）　" +
                    $"正常战败 {level.NormalDefeats}　运行异常 {level.RuntimeErrors}　规则不支持 {level.UnsupportedMechanics}　" +
                    $"用户取消 {level.UserCancelled}　平均转向 {level.AverageArchetypeChanges:0.00}");

                foreach (AutoRunWeekSummary week in report.WeekSummaries.Where(summary => summary.PlayerLevel == level.PlayerLevel))
                {
                    EditorGUILayout.Space(3);
                    if (week.BossScores == null)
                    {
                        EditorGUILayout.LabelField(
                            $"W{week.Week} Boss：到达 {week.BossReached}/{week.ActualRuns}（{week.BossReachRate:P1}）　" +
                            "通过 —　P10/P30/P50/P90 —　建议要求 —",
                            EditorStyles.wordWrappedLabel);
                    }
                    else
                    {
                        string suggestion = week.SuggestionValid
                            ? ScoreNumberFormatter.Format(week.BossScores.P30)
                            : "—（样本有效性不足）";
                        EditorGUILayout.LabelField(
                            $"W{week.Week} Boss：到达 {week.BossReached}/{week.ActualRuns}（{week.BossReachRate:P1}）　" +
                            $"通过 {week.BossPassed}/{week.BossReached}（{week.BossPassRate:P1}）　" +
                            $"P10 {ScoreNumberFormatter.Format(week.BossScores.P10)} / P30 {ScoreNumberFormatter.Format(week.BossScores.P30)} / " +
                            $"P50 {ScoreNumberFormatter.Format(week.BossScores.P50)} / P90 {ScoreNumberFormatter.Format(week.BossScores.P90)}　建议要求 {suggestion}",
                            EditorStyles.wordWrappedLabel);
                    }
                    EditorGUILayout.LabelField(
                        $"　到达本周 {week.StageReached}/{week.ActualRuns}（{week.StageReachRate:P1}）　" +
                        $"日常营业 {week.MealPasses}/{week.MealBattles}（{week.MealPassRate:P1}）　金币结余 {week.MeanGoldBalance:0}　" +
                        $"预算截断 {week.SolverTruncatedRate:P1}　候选有界 {week.PlacementCandidateLimitedRate:P1}　无合法方案 {week.NoLegalPlacementRate:P1}",
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(
                        $"　流派：甜蜜 {week.SweetTransferShare:P1} / 数量 {week.CountShare:P1} / 蛋糕 {week.CakeShare:P1}　" +
                        $"路线：正常 {week.NormalRouteShare:P1} / 事件 {week.EventRouteShare:P1} / 利息 {week.InterestRouteShare:P1} / 商店 {week.ShopRouteShare:P1}",
                        EditorStyles.wordWrappedLabel);
                    foreach (string warningCode in week.WarningCodes)
                        EditorGUILayout.HelpBox(WarningText(week, warningCode), WarningType(warningCode));
                }
                EditorGUILayout.EndVertical();
            }

            DrawAutoFailures(report);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导出摘要 CSV")) ExportAutoReport(false);
            if (GUILayout.Button("导出完整 JSON")) ExportAutoReport(true);
            GUI.enabled = !string.IsNullOrEmpty(_lastExportPath);
            if (GUILayout.Button("定位最近导出", GUILayout.Width(110))) EditorUtility.RevealInFinder(_lastExportPath);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_lastExportPath))
                EditorGUILayout.SelectableLabel(_lastExportPath, EditorStyles.miniLabel, GUILayout.Height(18));
        }

        private void DrawAutoFailures(AutoRunReport report)
        {
            if (report.Failures.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("失败与异常 Seed", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("结果区分为通关、正常战败、程序异常、规则不支持和用户取消。完整原因及逐周轨迹保存在 JSON。", MessageType.Info);
            foreach (AutoRunFailureRecord failure in report.Failures.Take(12))
            {
                string type = OutcomeText(failure.Outcome);
                string reason = FirstLine(failure.Reason);
                EditorGUILayout.LabelField(
                    $"[{type}] {failure.PlayerLevel} · Seed {failure.Seed} · 最后 W{failure.LastWeek} · {reason}",
                    EditorStyles.wordWrappedLabel);
            }
            if (report.Failures.Count > 12)
                EditorGUILayout.LabelField($"另有 {report.Failures.Count - 12} 条，详见完整 JSON。", EditorStyles.miniLabel);
            if (GUILayout.Button("复制失败 Seed 与原因", GUILayout.Width(180)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", report.Failures.Select(failure =>
                    $"{failure.Kind}\t{failure.PlayerLevel}\t{failure.Seed}\tW{failure.LastWeek}\t{failure.Reason}"));
            }
        }

        private void ExportAutoReport(bool json)
        {
            if (_lastAutoReport == null) return;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(root, "BalanceReports");
            Directory.CreateDirectory(directory);
            DateTime generated = DateTime.TryParse(
                _lastAutoReport.GeneratedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed.ToLocalTime()
                : DateTime.Now;
            string timestamp = generated.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string stem = $"balance-auto-{SafeFileName(_lastAutoReport.CharacterId)}-{_lastAutoReport.BaseSeed}-{timestamp}";
            string path = Path.Combine(directory, stem + (json ? ".json" : ".csv"));
            string content = json
                ? AutoRunReportBuilder.ToFullJson(_lastAutoReport, true)
                : AutoRunReportBuilder.ToSummaryCsv(_lastAutoReport);
            File.WriteAllText(path, content, json ? new UTF8Encoding(false) : new UTF8Encoding(true));
            _lastExportPath = path;
            ShowNotification(new GUIContent($"已导出 {Path.GetFileName(path)}"));
        }

        private MetaAffinityCatalog EffectiveMetaAffinity()
            => _metaAffinity;

        private string ResolveMetaAffinityProfileId()
        {
            if (_metaAffinity == null) return string.Empty;
            string path = AssetDatabase.GetAssetPath(_metaAffinity);
            string guid = string.IsNullOrEmpty(path) ? "memory" : AssetDatabase.AssetPathToGUID(path);
            return $"{_metaAffinity.ProfileId}:asset:{guid}";
        }

        private List<string> ValidateMetaAffinity()
        {
            if (_metaAffinity == null) return new List<string> { "缺少局外路线倾向表。" };
            return _metaAffinity.Validate(_tables, requireAllConfigured: true);
        }

        private void RefreshLastReportValidity()
        {
            if (_lastAutoReport == null) return;
            bool stale = _loadedConfigFingerprint?.IsValid != true
                         || _observedConfigFingerprint?.IsValid != true
                         || !_loadedConfigFingerprint.HasSameContent(_observedConfigFingerprint)
                         || !string.Equals(
                             _lastAutoReport.ConfigContentHash,
                             _loadedConfigFingerprint.ContentHash,
                             StringComparison.OrdinalIgnoreCase);
            AutoRunReportBuilder.RefreshValidity(_lastAutoReport, stale);
        }

        private static AutoPlayerPolicy ClonePolicy(AutoPlayerPolicy source)
        {
            source ??= new AutoPlayerPolicy();
            return new AutoPlayerPolicy
            {
                SoftmaxTemperature = source.SoftmaxTemperature,
                NormalBeamWidth = source.NormalBeamWidth,
                ExpertBeamWidth = source.ExpertBeamWidth,
                PlacementNodeBudget = source.PlacementNodeBudget,
                DualArchetypeThreshold = source.DualArchetypeThreshold,
                InterestReserve = source.InterestReserve,
                MaxActionsPerWeek = source.MaxActionsPerWeek,
            };
        }

        private static string WarningText(AutoRunWeekSummary week, string code)
        {
            switch (code)
            {
                case "no-stage-samples": return $"W{week.Week} 没有样本到达，本周统计无效。";
                case "no-boss-reached": return $"W{week.Week} 没有样本到达 Boss；分位数和建议要求均不生成。";
                case "boss-survivor-bias": return $"W{week.Week} Boss 到达率仅 {week.BossReachRate:P1}；P30 只代表到达者，请结合到达率解读。";
                case "boss-samples-below-30": return $"W{week.Week} 只有 {week.BossReached} 条样本到达 Boss；至少需要 30 条才生成建议要求。";
                case "low-meal-pass-rate": return $"W{week.Week} 日常营业通过率仅 {week.MealPassRate:P1}，多数样本可能在 Boss 前耗尽红心。";
                case "solver-truncated": return $"W{week.Week} 有 {week.SolverTruncatedRate:P1} 的阶段耗尽摆盘预览预算。";
                case "solver-truncated-over-limit": return $"W{week.Week} 摆盘预算截断率 {week.SolverTruncatedRate:P1} 超过 1%，建议要求不可用。";
                case "placement-candidate-limited": return $"W{week.Week} 有 {week.PlacementCandidateLimitedRate:P1} 的阶段按玩家档位限制了候选评估数；这是预期的有界策略。";
                case "no-legal-placement": return $"W{week.Week} 有 {week.NoLegalPlacementRate:P1} 的阶段没有合法摆盘。";
                case "runtime-errors": return "存在运行异常；本报告不可作为正式平衡结论。";
                case "unsupported-mechanics": return "存在规则不支持样本；推荐要求已禁用。";
                case "config-stale": return "报告配置已过期；推荐要求已禁用。";
                case "sampling-incomplete": return "采样未完整完成；推荐要求已禁用。";
                case "user-cancelled": return "本次任务已被用户取消；部分报告仍可导出。";
                default: return code;
            }
        }

        private static MessageType WarningType(string code)
            => code == "no-boss-reached" || code == "no-stage-samples" || code == "runtime-errors"
               || code == "unsupported-mechanics" || code == "config-stale"
               || code == "solver-truncated-over-limit"
                ? MessageType.Error
                : MessageType.Warning;

        private static string OutcomeText(AutoRunOutcomeKind outcome)
        {
            switch (outcome)
            {
                case AutoRunOutcomeKind.Completed: return "通关";
                case AutoRunOutcomeKind.NormalDefeat: return "正常战败";
                case AutoRunOutcomeKind.UnsupportedMechanic: return "规则不支持";
                case AutoRunOutcomeKind.UserCancelled: return "用户取消";
                default: return "程序异常";
            }
        }

        private static string ShortHash(string value)
            => string.IsNullOrEmpty(value) ? "不可用" : value.Substring(0, Math.Min(12, value.Length));

        private static string FirstLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未提供原因";
            string line = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
            return line.Length <= 180 ? line : line.Substring(0, 180) + "…";
        }

        private static string SafeFileName(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "unknown" : value;
            foreach (char invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '-');
            return text;
        }

        private sealed class AutoSimulationJob
        {
            public string CharacterId;
            public string CharacterName;
            public int TotalWeeks;
            public int SampleCount;
            public int Cursor;
            public int LevelIndex;
            public int BaseSeed;
            public List<AutoPlayerLevel> Levels;
            public AutoPlayerPolicy Policy;
            public MetaAffinityCatalog MetaAffinity;
            public string MetaAffinityProfileId;
            public string MetaAffinityProfileHash;
            public string UnlockProfileHash;
            public string PolicyVersion;
            public string ConfigContentHash;
            public int ConfigFileCount;
            public string ConfigLoadedUtc;
            public string StartedUtc;
            public HeadlessRunSession CurrentSession;
            public bool CancelRequested;
        }
    }
}
#endif
