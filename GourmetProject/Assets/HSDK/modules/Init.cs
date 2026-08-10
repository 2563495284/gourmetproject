namespace Hortor
{
    public sealed class InitOption
    {
        public string gameId;
        public ENV env;
        public string gameVersion;
    }

    internal static class InitHandler
    {
        internal const string HsdkVersion = "0.0.1";

        public static void Init(InitOption option)
        {
            GameInfo.Init(option);
            Network.Init(option.env);
            ClientSystemInfo.Init();
        }
    }
}
