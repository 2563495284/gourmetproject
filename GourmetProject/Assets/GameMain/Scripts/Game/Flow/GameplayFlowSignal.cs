namespace GourmetProject.Game.Flow
{
    /// <summary>
    /// 局内界面向玩法流程发出的流转信号（返回菜单等需要切换 GameFramework 流程的操作）。
    /// 界面登记信号，由 ProcedureGameplay 轮询处理。周内推进（下一周）不需要切流程，界面自行重建会话。
    /// </summary>
    public static class GameplayFlowSignal
    {
        public static bool ReturnToMenuRequested { get; private set; }

        public static void RequestReturnToMenu() => ReturnToMenuRequested = true;

        public static void Consume() => ReturnToMenuRequested = false;
    }
}
