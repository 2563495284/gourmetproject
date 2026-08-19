namespace GourmetProject.Game.Run
{
    public enum RunContentAcquisitionKind
    {
        Item,
        DishFlavor,
    }

    /// <summary>奖励、商店与事件共用的真实获得内容通知。</summary>
    public sealed class RunContentAcquisition
    {
        public RunContentAcquisitionKind Kind { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public cfg.ItemKind ItemKind { get; set; }
        public cfg.ActiveItemCategory ActiveItemCategory { get; set; }
        public string ItemEffectType { get; set; } = string.Empty;
        public string DishId { get; set; } = string.Empty;
        public string FlavorId { get; set; } = string.Empty;
        public string FragmentId { get; set; } = string.Empty;
    }
}
