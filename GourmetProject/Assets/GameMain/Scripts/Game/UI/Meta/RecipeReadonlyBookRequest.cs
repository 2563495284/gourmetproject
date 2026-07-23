using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.UI.Meta
{
    internal enum RecipeReadonlyBookMode
    {
        ReadonlyBook,
        ShopDeleteDish,
        ActiveItemTarget,
        EventDeleteDish,
    }

    internal readonly struct RecipeReadonlyBookRequest
    {
        private RecipeReadonlyBookRequest(
            RecipeReadonlyBookMode mode,
            Action onExit,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onChanged,
            ItemDefinition item,
            string title,
            int bookIndex,
            IReadOnlyList<RecipeBookSlot> readonlyEntries)
        {
            Mode = mode;
            OnExit = onExit;
            OnCancel = onCancel;
            OnTargetConfirmed = onTargetConfirmed;
            OnChanged = onChanged;
            Item = item;
            Title = title;
            BookIndex = bookIndex;
            ReadonlyEntries = readonlyEntries;
        }

        public RecipeReadonlyBookMode Mode { get; }

        public Action OnExit { get; }

        public Action OnCancel { get; }

        public Action<ActiveTarget, Action> OnTargetConfirmed { get; }

        public Action OnChanged { get; }

        public ItemDefinition Item { get; }

        public string Title { get; }

        public int BookIndex { get; }

        public IReadOnlyList<RecipeBookSlot> ReadonlyEntries { get; }

        public static RecipeReadonlyBookRequest ReadonlyBook(
            int bookIndex,
            Action onExit,
            Action onChanged,
            IReadOnlyList<RecipeBookSlot> readonlyEntries = null)
        {
            return new RecipeReadonlyBookRequest(
                RecipeReadonlyBookMode.ReadonlyBook,
                onExit,
                null,
                null,
                onChanged,
                null,
                null,
                bookIndex,
                readonlyEntries);
        }

        public static RecipeReadonlyBookRequest ShopDeleteDish(Action onExit, Action onChanged)
        {
            return new RecipeReadonlyBookRequest(
                RecipeReadonlyBookMode.ShopDeleteDish,
                onExit,
                null,
                null,
                onChanged,
                null,
                null,
                0,
                null);
        }

        public static RecipeReadonlyBookRequest ActiveItemTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget, Action> onTargetConfirmed,
            Action onChanged)
        {
            return new RecipeReadonlyBookRequest(
                RecipeReadonlyBookMode.ActiveItemTarget,
                null,
                onCancel,
                onTargetConfirmed,
                onChanged,
                item,
                null,
                -1,
                null);
        }

        public static RecipeReadonlyBookRequest EventDeleteDish(
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            return new RecipeReadonlyBookRequest(
                RecipeReadonlyBookMode.EventDeleteDish,
                null,
                onCancel,
                onTargetConfirmed != null ? (target, _) => onTargetConfirmed(target) : null,
                onChanged,
                null,
                title,
                -1,
                null);
        }
    }
}
