using System;
using System.Collections.Generic;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Meta
{
    /// <summary>战斗情境下的主动道具使用上下文：能力落到 <see cref="BattleSession"/>。</summary>
    public sealed class BattleUseContext : IActiveUseContext
    {
        private readonly BattleSession _session;

        public BattleUseContext(BattleSession session, GameRun run)
        {
            _session = session;
            Run = run;
        }

        public ActiveUseContextKind ContextKind => ActiveUseContextKind.Battle;

        public GameRun Run { get; }

        public IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind)
        {
            // 目标选择 UI 落地时在此枚举棋盘/菜谱目标；当前尚无需选目标的主动道具，返回空。
            return Array.Empty<ActiveTarget>();
        }

        public bool ClearBoard()
        {
            if (_session == null || _session.IsSettled || _session.Board.DishCount <= 0)
            {
                return false;
            }

            _session.ClearBoard();
            return true;
        }

        public bool ExtraServe()
        {
            if (_session == null || _session.IsSettled)
            {
                return false;
            }

            for (int i = 0; i < _session.Slots.Count; i++)
            {
                if (_session.Serve(i).Success)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
