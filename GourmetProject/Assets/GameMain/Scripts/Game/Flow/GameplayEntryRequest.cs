namespace GourmetProject.Game.Flow
{
    /// <summary>
    /// 从菜单（经营方向选择/继续游戏）向流程层传递「开始局内」请求的轻量信箱。
    /// 界面不直接切换 GameFramework 流程，改为登记请求，由当前的 ProcedureMenu 轮询后切到 ProcedureGameplay。
    /// </summary>
    public static class GameplayEntryRequest
    {
        public enum Mode
        {
            NewRun,
            Continue,
        }

        public static bool Pending { get; private set; }

        public static Mode RequestedMode { get; private set; }

        public static string CharacterId { get; private set; }

        /// <summary>请求以指定经营方向开新运行。</summary>
        public static void RequestNewRun(string characterId)
        {
            RequestedMode = Mode.NewRun;
            CharacterId = characterId;
            Pending = true;
        }

        /// <summary>请求继续已有运行（读档后）。</summary>
        public static void RequestContinue()
        {
            RequestedMode = Mode.Continue;
            CharacterId = null;
            Pending = true;
        }

        /// <summary>消费请求并清空。</summary>
        public static void Consume()
        {
            Pending = false;
        }
    }
}
