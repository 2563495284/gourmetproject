namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 单局已选技能的运行时只读视图：供玩法系统（如 <c>PlayerController</c>、光照、怪物）
    /// 按语义查询当前 build。本次只接入「二段跳」作为「选取 → 生效」示范，
    /// 其余便捷属性供后续效果接入逐步使用。
    /// </summary>
    public sealed class SkillRuntimeState
    {
        private readonly RunSkillState _state;

        public SkillRuntimeState(RunSkillState state)
        {
            _state = state ?? new RunSkillState();
        }

        /// <summary>底层已选技能状态。</summary>
        public RunSkillState State => _state;

        /// <summary>是否拥有某技能。</summary>
        public bool Has(string id) => _state.Has(id);

        /// <summary>某技能叠加层数。</summary>
        public int StacksOf(string id) => _state.StacksOf(id);

        // —— 核心移动技语义查询（效果接入用）——

        /// <summary>二段跳：空中可再跳一次（本次已接入 PlayerController）。</summary>
        public bool HasDoubleJump => _state.Has(SkillIds.DoubleJump);

        public bool HasWallCling => _state.Has(SkillIds.WallCling);
        public bool HasGlide => _state.Has(SkillIds.Glide);
        public bool HasAirDash => _state.Has(SkillIds.AirDash);
        public bool HasWallJump => _state.Has(SkillIds.WallJump);
        public bool HasDashKeepJump => _state.Has(SkillIds.DashKeepJump);
        public bool HasDashCancel => _state.Has(SkillIds.DashCancel);
        public bool HasDashInvuln => _state.Has(SkillIds.DashInvuln);
    }
}
