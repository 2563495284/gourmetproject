namespace GourmetProject.Game.Roguelike
{
    /// <summary>
    /// 技能 id 常量（与 GameConfig/Datas/skill.json 一一对应）。供效果接入层按语义查询
    /// <see cref="RunSkillState"/> / <see cref="SkillRuntimeState"/>，避免散落字符串字面量。
    /// </summary>
    public static class SkillIds
    {
        // —— T1：核心移动 ——
        public const string DoubleJump = "ms_double_jump";
        public const string WallCling = "ms_wall_cling";
        public const string Glide = "ms_glide";
        public const string MoveSpeed = "mv_move_speed";

        // —— T1：光源 / 经济 / 怪物 ——
        public const string Aura10 = "lt_aura_10";
        public const string Cost10 = "lt_cost_10";
        public const string Pulse = "lt_pulse";
        public const string EnergyCap = "ec_energy_cap";
        public const string MosquitoRepel = "mo_mosquito_repel";

        // —— T2：核心移动 ——
        public const string AirDash = "ms_air_dash";
        public const string WallJump = "ms_wall_jump";
        public const string DashKeepJump = "ms_dash_keep_jump";
        public const string DashCancel = "ms_dash_cancel";
        public const string GlidePlus = "ms_glide_plus";

        // —— T2：光源 / 经济 / 生存 / 怪物 ——
        public const string PulseCdHalf = "lt_pulse_cd_half";
        public const string Regen = "ec_regen";
        public const string SpikeWarn = "sv_spike_warn";
        public const string ShadowSense = "mo_shadow_sense";

        // —— T3：核心移动 ——
        public const string DashInvuln = "ms_dash_invuln";

        // —— T3：光源 / 生存 / 怪物 ——
        public const string Aura30 = "lt_aura_30";
        public const string Cost30 = "lt_cost_30";
        public const string PulseCd3 = "lt_pulse_cd_3";
        public const string Revive = "sv_revive";
        public const string InvulnExt = "sv_invuln_ext";
        public const string LightBurst = "mo_light_burst";
        public const string ShadowRepelAura = "mo_shadow_repel_aura";
    }
}
