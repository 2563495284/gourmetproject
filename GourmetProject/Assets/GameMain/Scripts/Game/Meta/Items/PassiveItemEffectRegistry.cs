using GourmetProject.Gameplay.Battle;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>被动道具效果派发入口。新增效果时优先在这里注册，避免 UI/流程层直接 switch。</summary>
    public static class PassiveItemEffectRegistry
    {
        public static void ApplyToBattle(BattleSession session, cfg.Item item, RunItemState state)
        {
            if (session == null || item == null || state == null)
            {
                return;
            }

            switch (item.EffectType)
            {
                case "FinalAddFlat":
                    session.FinalFlat += item.EffectValue;
                    break;
                case "FinalAddMult":
                    session.FinalMultiplier *= item.EffectValue;
                    break;
            }
        }
    }
}
