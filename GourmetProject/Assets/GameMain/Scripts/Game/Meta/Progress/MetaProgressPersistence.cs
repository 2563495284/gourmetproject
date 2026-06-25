using GourmetProject.Core.Save;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>跨局进度档读写，独立于单局运行存档。</summary>
    public static class MetaProgressPersistence
    {
        public const string Slot = "meta_progress";

        public static MetaProgressSaveData Load()
        {
            return Load(GameApp.Save);
        }

        public static MetaProgressSaveData Load(ISaveService save)
        {
            if (save == null || !save.TryLoad(Slot, out MetaProgressSaveData data) || data == null)
            {
                return new MetaProgressSaveData();
            }

            data.Normalize();
            return data;
        }

        public static void Save(MetaProgressSaveData data)
        {
            Save(GameApp.Save, data);
        }

        public static void Save(ISaveService save, MetaProgressSaveData data)
        {
            if (save == null)
            {
                return;
            }

            data ??= new MetaProgressSaveData();
            data.Normalize();
            save.Save(Slot, data);
        }

        public static void Delete()
        {
            GameApp.Save?.Delete(Slot);
        }
    }
}
