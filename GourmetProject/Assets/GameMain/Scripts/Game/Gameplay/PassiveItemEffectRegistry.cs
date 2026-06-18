using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Gameplay
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
                    session.FinalFlat += item.EffectValue * state.Level;
                    break;
                case "FinalAddMult":
                    session.FinalMultiplier *= 1f + (item.EffectValue - 1f) * state.Level;
                    break;
            }
        }
    }
}
