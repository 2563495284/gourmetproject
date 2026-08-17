using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;

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
            IReadOnlyList<RecipeReadonlyDishEntry> readonlyEntries,
            bool hideExitButton = false)
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
            HideExitButton = hideExitButton;
        }

        public RecipeReadonlyBookMode Mode { get; }

        public Action OnExit { get; }

        public Action OnCancel { get; }

        public Action<ActiveTarget, Action> OnTargetConfirmed { get; }

        public Action OnChanged { get; }

        public ItemDefinition Item { get; }

        public string Title { get; }

        public int BookIndex { get; }

        public IReadOnlyList<RecipeReadonlyDishEntry> ReadonlyEntries { get; }

        public bool HideExitButton { get; }

        public static RecipeReadonlyBookRequest ReadonlyBook(
            int bookIndex,
            Action onExit,
            Action onChanged,
            IReadOnlyList<RecipeReadonlyDishEntry> readonlyEntries = null,
            bool hideExitButton = false)
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
                readonlyEntries,
                hideExitButton);
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

    internal sealed class RecipeReadonlyDishEntry
    {
        public RecipeReadonlyDishEntry(
            RecipeBookSlot slot,
            BattleRecipeEntryStatus? battleStatus = null,
            bool skillsDisabled = false,
            bool excludedFromScore = false,
            bool initiallyHidden = false)
        {
            Slot = slot;
            BattleStatus = battleStatus;
            SkillsDisabled = skillsDisabled;
            ExcludedFromScore = excludedFromScore;
            InitiallyHidden = initiallyHidden;
        }

        public RecipeBookSlot Slot { get; }

        /// <summary>仅经营挑战场景 recipeInfoButton 入口提供；为空时保持局外食谱原有视觉。</summary>
        public BattleRecipeEntryStatus? BattleStatus { get; }

        public bool SkillsDisabled { get; }

        public bool ExcludedFromScore { get; }

        public bool InitiallyHidden { get; }
    }
}
