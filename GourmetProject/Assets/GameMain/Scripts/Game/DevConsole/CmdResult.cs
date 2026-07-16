namespace GourmetProject.Game.DevConsole
{
    /// <summary>
    /// 控制台命令执行结果（参考 STS2 <c>CmdResult</c>，去掉联机用的 Task）。
    /// 单机同步执行，只携带成功标记与展示文本。
    /// </summary>
    public readonly struct CmdResult
    {
        public readonly bool Success;
        public readonly string Message;

        public CmdResult(bool success, string message = null)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        public static CmdResult Ok(string message = null) => new CmdResult(true, message);

        public static CmdResult Fail(string message = null) => new CmdResult(false, message);
    }
}
