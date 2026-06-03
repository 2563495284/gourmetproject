using System;

namespace GourmetProject.Game.Procedure
{
    /// <summary>
    /// 轻量解耦：UI（主菜单）请求开始对局，由菜单流程（持有 FSM owner）监听后切换到对局流程。
    /// 避免在 UI Form 中直接操作流程 FSM。
    /// </summary>
    public static class GameplayLauncher
    {
        public static event Action StartRequested;

        public static void RequestStart()
        {
            StartRequested?.Invoke();
        }
    }
}
