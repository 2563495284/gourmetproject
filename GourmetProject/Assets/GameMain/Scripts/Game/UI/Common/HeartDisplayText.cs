using System.Text;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>统一生成 HUD 与碎心弹层使用的富文本爱心串。</summary>
    public static class HeartDisplayText
    {
        public const string FullColor = "#E92D3A";
        public const string BrokenColor = "#F2A5AA";

        public static string Build(int remaining, int capacity)
        {
            capacity = System.Math.Max(1, capacity);
            remaining = System.Math.Max(0, System.Math.Min(remaining, capacity));

            var result = new StringBuilder(capacity * 30);
            if (remaining > 0)
            {
                result.Append("<color=").Append(FullColor).Append('>');
                result.Append('♥', remaining);
                result.Append("</color>");
            }

            int broken = capacity - remaining;
            if (broken > 0)
            {
                result.Append("<color=").Append(BrokenColor).Append('>');
                result.Append('♥', broken);
                result.Append("</color>");
            }

            return result.ToString();
        }
    }
}
