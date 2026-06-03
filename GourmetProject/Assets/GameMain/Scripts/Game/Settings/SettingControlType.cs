namespace GourmetProject.Game.Settings
{
    /// <summary>
    /// 设置项使用的 UI 控件类型。新增控件类型时，SettingsForm 需补充对应的行预制体与绑定逻辑。
    /// </summary>
    public enum SettingControlType
    {
        Toggle,
        Slider,
        Dropdown,
    }
}
