using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>死亡原因（影响是否忽略无敌、反馈表现）。</summary>
    public enum DeathCause
    {
        Fall,
        Spike,
        Shadow,
        Suicide,
    }

    /// <summary>
    /// 打火机能量（设计文档 4.1）：上限 100，开启 17%/s 消耗（约 5.9s 耗尽），耗尽自动熄灭；
    /// 触碰检查点立即回满。打火机实际光半径随能量按 sqrt 曲线衰减。
    /// </summary>
    public sealed class EnergySystem
    {
        public float Current { get; private set; } = GameConst.EnergyMax;
        public float Max => GameConst.EnergyMax;
        public float Fraction => Mathf.Clamp01(Current / Max);

        /// <summary>本帧打火机是否真正点亮（按住 R 且仍有能量）。</summary>
        public bool LighterOn { get; private set; }

        public void Tick(float dt, bool wantLighter)
        {
            LighterOn = wantLighter && Current > 0f;
            if (LighterOn)
            {
                Current -= GameConst.LighterDrainPerSec * dt;
                if (Current <= 0f)
                {
                    Current = 0f;
                    LighterOn = false;
                }
            }
        }

        public void Refill()
        {
            Current = Max;
        }

        /// <summary>打火机当前光半径（世界单位）：base × sqrt(energy/100)。</summary>
        public float LighterRadius => GameConst.LighterBaseRadius * Mathf.Sqrt(Fraction);
    }
}
