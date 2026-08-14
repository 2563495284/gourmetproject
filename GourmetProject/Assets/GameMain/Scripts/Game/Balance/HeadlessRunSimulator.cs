using System;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Data;

namespace GourmetProject.Game.Balance
{
    /// <summary>
    /// 一条自动局的可分帧执行句柄。每次 <see cref="Step"/> 只推进有限个正式周循环回调，
    /// 同步入口与分帧入口共享同一个 view 和随机流。
    /// </summary>
    public sealed class HeadlessRunSession
    {
        private readonly HeadlessWeekLoopView _view;
        private bool _failed;

        internal HeadlessRunSession(HeadlessWeekLoopView view, AutoRunTrace trace)
        {
            _view = view;
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public AutoRunTrace Trace { get; }

        public bool IsCompleted => _failed || _view == null || _view.IsCompleted;

        /// <summary>推进有限个流程回调；返回 true 表示本局已经终止。</summary>
        public bool Step(int callbackBudget = 1)
        {
            if (IsCompleted)
            {
                return true;
            }

            try
            {
                return _view.Step(callbackBudget);
            }
            catch (Exception exception)
            {
                MarkInfrastructureFailure(Trace, exception);
                _failed = true;
                return true;
            }
        }

        public void Cancel()
        {
            if (!IsCompleted)
            {
                _view.Cancel();
            }
        }

        internal static void MarkInfrastructureFailure(AutoRunTrace trace, Exception exception)
        {
            trace.Completed = false;
            trace.Termination = AutoRunTerminationKind.InfrastructureError;
            trace.FailureReason = exception.ToString();
            trace.Warnings.Add(new AutoRunWarning
            {
                Kind = AutoRunWarningKind.None,
                Code = "UNHANDLED_EXCEPTION",
                Message = exception.Message,
                Week = trace.Stages.Count > 0
                    ? trace.Stages[trace.Stages.Count - 1].Week
                    : 0,
            });
        }
    }

    /// <summary>
    /// Balance Lab 的同步入口。内部通过无界面 IWeekLoopView 驱动正式 WeekLoopController，
    /// 不写玩家存档、不设置 GameRunContext，也不读取或推进 GameApp 的随机状态。
    /// </summary>
    public sealed class HeadlessRunSimulator
    {
        private readonly cfg.Tables _tables;
        private readonly GameplayDatabase _database;

        public HeadlessRunSimulator(cfg.Tables tables, GameplayDatabase database)
        {
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public AutoRunTrace Run(AutoRunRequest request)
        {
            HeadlessRunSession session = StartSession(request);
            while (!session.Step(256))
            {
            }

            return session.Trace;
        }

        /// <summary>
        /// 创建一条尚未推进的自动局，供编辑器 update 按帧调用 <see cref="HeadlessRunSession.Step"/>。
        /// </summary>
        public HeadlessRunSession StartSession(AutoRunRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var trace = new AutoRunTrace
            {
                Seed = request.Seed,
                CharacterId = request.CharacterId,
                PlayerLevel = request.PlayerLevel,
            };

            try
            {
                string seedText = $"balance-auto-{request.Seed}";
                IRunExecutionEnvironment execution = RunExecutionEnvironment.CreateIsolated(
                    _tables,
                    seedText,
                    profileId: RunExecutionEnvironment.AllUnlockedProfileId);
                var run = new GameRun(
                    _tables,
                    _database,
                    request.CharacterId,
                    seedText,
                    weekIndex: 1,
                    isTutorialRun: false,
                    execution: execution);
                var view = new HeadlessWeekLoopView(run, request, _database, trace);
                view.Begin();
                return new HeadlessRunSession(view, trace);
            }
            catch (Exception exception)
            {
                HeadlessRunSession.MarkInfrastructureFailure(trace, exception);
                return new HeadlessRunSession(view: null, trace);
            }
        }
    }
}
