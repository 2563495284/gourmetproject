using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Data;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// 运行存档读写：把 <see cref="GameRun"/> 与存档槽位、随机种子初始化串起来。
    /// 存档槽位沿用 <see cref="UIForms.GameSaveSlot"/>，与经营方向选择界面的存档入口判定一致。
    /// </summary>
    public static class RunPersistence
    {
        private const string Tag = "RunSave";
        private static int _saveSuppressionDepth;

        public static bool HasSave => GameApp.Save.Has(UIForms.GameSaveSlot);

        public static void Save(GameRun run)
        {
            if (run == null || _saveSuppressionDepth > 0)
            {
                return;
            }

            RunSaveData data = run.ToSaveData();
            data.RandomSnapshot = GameApp.Random.Capture();
            GameApp.Save.Save(UIForms.GameSaveSlot, data);
            Log.Info($"Run saved. week={run.WeekIndex}, gold={run.Gold}.", Tag);
        }

        public static System.IDisposable SuppressSave()
        {
            _saveSuppressionDepth++;
            return new SaveSuppressionScope();
        }

        public static void Delete()
        {
            GameApp.Save.Delete(UIForms.GameSaveSlot);
        }

        /// <summary>读取存档并重建运行（同时按种子初始化随机）。失败返回 null。</summary>
        public static GameRun TryLoad()
        {
            if (!GameApp.Save.TryLoad(UIForms.GameSaveSlot, out RunSaveData data) || data == null)
            {
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
