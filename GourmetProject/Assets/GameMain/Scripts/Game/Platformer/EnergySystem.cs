using UnityEngine;

namespace GourmetProject.Game.Platformer
{
    /// <summary>死亡原因（影响是否忽略无敌、反馈表现）。</summary>
    public enum DeathCause
    {
        Fall,
        Spike,
        Shadow,
        Monster,   // 其它暗处怪物接触致死（伏击蛛 / 石瞳 / 光影虫引爆）
        Suicide,
    }

    /// <summary>
    /// 打火机能量（设计文档 4.1）：上限 100，开启 17%/s 消耗（约 5.9s 耗尽），耗尽自动熄灭；
    /// 触碰检查点立即回满。打火机实际光半径随能量按 sqrt 曲线衰减。
    /// </summary>
    public sealed class EnergySystem
    {
        private float _max = GameConst.EnergyMax;

        public float Current { get; private set; } = GameConst.EnergyMax;
        public float Max => _max;
        public float Fraction => Mathf.Clamp01(Current / Max);

        /// <summary>设置由局外成长提供的能量上限加成（设计文档 13.10），并立即回满。</summary>
        public void SetMaxBonus(float bonus)
        {
            _max = GameConst.EnergyMax + Mathf.Max(0f, bonus);
            Current = _max;
        }

        /// <summary>扣减能量（光食虫附着 / 雾灵接触等），按百分点。下限 0。</summary>
        public void Drain(float percent)
        {
            if (percent <= 0f) return;
            Current = Mathf.Max(0f, Current - percent);
            if (Current <= 0f) LighterOn = false;
        }

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
