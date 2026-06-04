namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 跨流程传递「本局开局意图」的轻量桥：菜单决定新局/继续后写入这里，
    /// <c>ProcedureGameplay</c> 据此初始化随机系统，<c>GameWorld</c> 据此恢复技能与检查点。
    /// 非 null 表示继续游戏；null 表示开新局。
    /// </summary>
    public static class RunSession
    {
        /// <summary>待加载的存档数据；null 表示开新局。</summary>
        public static RunSaveData PendingLoad;

        public static bool HasPendingLoad => PendingLoad != null;

        public static void Clear() => PendingLoad = null;
    }
}
