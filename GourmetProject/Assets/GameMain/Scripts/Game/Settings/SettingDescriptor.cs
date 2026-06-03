using System;
using System.Collections.Generic;

namespace GourmetProject.Game.Settings
{
    /// <summary>
    /// 单个设置项的数据驱动描述符。SettingsForm 仅依赖本描述符渲染与回写，
    /// 不关心具体是什么设置。新增一项设置（如倍速）只需在 <see cref="SettingsCatalog"/>
    /// 里追加一个描述符并复用对应控件，无需改 UI 代码。
    /// </summary>
    public sealed class SettingDescriptor
    {
        /// <summary>稳定标识，便于调试与定位。</summary>
        public string Id;

        /// <summary>展示用标题文本。</summary>
        public string Label;

        /// <summary>使用的控件类型。</summary>
        public SettingControlType ControlType;

        // —— Dropdown 用 ——
        public Func<List<string>> GetOptions;
        public Func<int> GetSelectedIndex;
        public Action<int> SetSelectedIndex;

        // —— Slider 用 ——
        public float SliderMin;
        public float SliderMax = 1f;
        public bool SliderWholeNumbers;
        public Func<float> GetSliderValue;
        public Action<float> SetSliderValue;

        /// <summary>把滑条数值格式化为展示文本（如 "80%"、"1.5x"）。可空，默认用原值。</summary>
        public Func<float, string> FormatSliderValue;

        // —— Toggle 用 ——
        public Func<bool> GetToggleValue;
        public Action<bool> SetToggleValue;

        /// <summary>
        /// 点击“应用”时统一触发的落地副作用（如把分辨率推给引擎）。可空。
        /// 仅写入设置存储而无需即时生效的项可以不填。
        /// </summary>
        public Action Apply;
    }
}
