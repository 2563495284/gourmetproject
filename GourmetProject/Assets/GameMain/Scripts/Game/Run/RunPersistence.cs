using GourmetProject.Core.Save;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Save;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 运行存档读写：把 <see cref="GameRun"/> 与存档槽位、随机种子初始化串起来。
    /// 单局数据是玩家总档中的一个可清空分区。
    /// </summary>
    public static class RunPersistence
    {
        private const string Tag = "RunSave";
        public const int CurrentActionRandomRuleVersion = 1;
        private static int _saveSuppressionDepth;

        public static bool HasSave
        {
            get
            {
                RunSaveData data = GameSavePersistence.Load().Run;
                return data != null
                    && IsActionRandomRuleVersionCompatible(data.ActionRandomRuleVersion);
            }
        }

        public static void Save(GameRun run)
        {
            if (run == null || _saveSuppressionDepth > 0)
            {
                return;
            }

            RunSaveData data = run.ToSaveData();
            data.ActionRandomRuleVersion = CurrentActionRandomRuleVersion;
            data.RandomSnapshot = GameApp.Random.Capture();
            GameSaveData root = GameSavePersistence.Load();
            root.Run = data;
            GameSavePersistence.Save(root);
            Log.Info($"Run saved. week={run.WeekIndex}, gold={run.Gold}.", Tag);
        }

        public static System.IDisposable SuppressSave()
        {
            _saveSuppressionDepth++;
            return new SaveSuppressionScope();
        }

        public static void Delete()
        {
            Delete(GameApp.Save);
        }

        internal static void Delete(ISaveService save)
        {
            if (save == null)
            {
                return;
            }

            GameSaveData root = GameSavePersistence.Load(save);
            root.Run = null;
            GameSavePersistence.Save(save, root);
        }

        /// <summary>读取存档并重建运行（同时按种子初始化随机）。失败返回 null。</summary>
        public static GameRun TryLoad()
        {
            RunSaveData data = GameSavePersistence.Load().Run;
            if (data == null)
            {
                return null;
            }

            if (!IsActionRandomRuleVersionCompatible(data.ActionRandomRuleVersion))
            {
                Log.Warning(
                    $"Run save uses incompatible action random rules "
                    + $"(save={data.ActionRandomRuleVersion}, current={CurrentActionRandomRuleVersion}); start a new run.",
                    Tag);
                return null;
            }

            cfg.Tables tables = GameApp.Config.Tables;
            GameplayDatabase db = GameplayContentBuilder.BuildDatabase(tables);
            if (data.RandomSnapshot != null)
            {
                GameApp.Random.Restore(data.RandomSnapshot);
            }
            else
            {
                GameApp.Random.Init(data.SeedText);
            }

            GameRun run = GameRun.FromSaveData(tables, db, data);
            Log.Info($"Run loaded. character={run.CharacterId}, week={run.WeekIndex}.", Tag);
            return run;
        }

        internal static bool IsActionRandomRuleVersionCompatible(int version)
        {
            return version == CurrentActionRandomRuleVersion;
        }

        private sealed class SaveSuppressionScope : System.IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _saveSuppressionDepth = System.Math.Max(0, _saveSuppressionDepth - 1);
            }
        }
    }
}
