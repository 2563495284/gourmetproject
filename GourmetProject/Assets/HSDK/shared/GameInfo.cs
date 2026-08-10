using System;

namespace Hortor
{
    internal static class GameInfo
    {
        public static string GameId { get; private set; } = string.Empty;
        public static string GameVersion { get; private set; } = string.Empty;
        public static ENV Environment { get; private set; } = ENV.Production;

        public static void Init(InitOption option)
        {
            if (option == null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            if (string.IsNullOrWhiteSpace(option.gameId))
            {
                throw new ArgumentException("gameId must not be empty.", nameof(option));
            }

            GameId = option.gameId;
            GameVersion = option.gameVersion ?? string.Empty;
            Environment = option.env;
        }
    }
}
