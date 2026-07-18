using System;
using GourmetProject.Game.Meta;

namespace GourmetProject.Game.UI.Meta
{
    internal enum RecipeWorkspaceMode
    {
        Edit,
        ReadonlyBook,
        ActiveItemTarget,
        EventDeleteDish,
    }

    internal readonly struct RecipeWorkspaceRequest
    {
        private RecipeWorkspaceRequest(
            RecipeWorkspaceMode mode,
            Action onExit,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged,
            ItemDefinition item,
            string title,
            int bookIndex)
        {
            Mode = mode;
            OnExit = onExit;
            OnCancel = onCancel;
            OnTargetConfirmed = onTargetConfirmed;
            OnChanged = onChanged;
            Item = item;
            Title = title;
            BookIndex = bookIndex;
        }

        public RecipeWorkspaceMode Mode { get; }

        public Action OnExit { get; }

        public Action OnCancel { get; }

        public Action<ActiveTarget> OnTargetConfirmed { get; }

        public Action OnChanged { get; }

        public ItemDefinition Item { get; }

        public string Title { get; }

        public int BookIndex { get; }

        public static RecipeWorkspaceRequest Edit(Action onExit, Action onChanged)
        {
            return new RecipeWorkspaceRequest(
                RecipeWorkspaceMode.Edit,
                onExit,
                null,
                null,
                onChanged,
                null,
                null,
                -1);
        }

        public static RecipeWorkspaceRequest ReadonlyBook(int bookIndex, Action onExit, Action onChanged)
        {
            return new RecipeWorkspaceRequest(
                RecipeWorkspaceMode.ReadonlyBook,
                onExit,
                null,
                null,
                onChanged,
                null,
                null,
                bookIndex);
        }

        public static RecipeWorkspaceRequest ActiveItemTarget(
            ItemDefinition item,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            return new RecipeWorkspaceRequest(
                RecipeWorkspaceMode.ActiveItemTarget,
                null,
                onCancel,
                onTargetConfirmed,
                onChanged,
                item,
                null,
                -1);
        }

        public static RecipeWorkspaceRequest EventDeleteDish(
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            return new RecipeWorkspaceRequest(
                RecipeWorkspaceMode.EventDeleteDish,
                null,
                onCancel,
                onTargetConfirmed,
                onChanged,
                null,
                title,
                -1);
        }
    }
}
