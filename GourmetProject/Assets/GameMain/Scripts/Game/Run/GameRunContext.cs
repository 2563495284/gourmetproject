using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using UnityEngine;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 当前肉鸽运行的全局持有点，供局内/局外各界面共享同一个 <see cref="GameRun"/>。
    /// 单机单运行，简单静态持有即可；进入新运行时覆盖，回主菜单时清空。
    /// </summary>
    public static class GameRunContext
    {
        public static GameRun Current { get; private set; }

        public static bool HasRun => Current != null;

        public static void Set(GameRun run) => Current = run;

        public static void Clear() => Current = null;

        /// <summary>
        /// 关闭 Domain Reload 的编辑器会跨 Play Session 保留静态字段。
        /// 每次启动都清空旧运行，避免新建的 RandomService 与上一次的 GameRun 脱节。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }
    }
}
