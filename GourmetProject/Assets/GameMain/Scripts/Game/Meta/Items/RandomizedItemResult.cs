namespace GourmetProject.Game.Meta
{
    public sealed class RandomizedItemResult
    {
        public RandomizedItemResult(ItemDefinition item, ItemAcquireResult acquireResult)
        {
            Item = item;
            AcquireResult = acquireResult;
        }

        public ItemDefinition Item { get; }

        public ItemAcquireResult AcquireResult { get; }

        public bool Acquired => Item != null && AcquireResult.Outcome != ItemAcquireOutcome.ConvertedToGold;
    }
}
