using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;

namespace GourmetProject.Game.UI.Battle.Pages
{
    internal readonly struct EventPageRequest
    {
        public EventPageRequest(
            string title,
            string description,
            string resultButtonText,
            string backgroundSprite,
            IReadOnlyList<string> options,
            IReadOnlyList<bool> optionEnabled,
            Action<int> onPick,
            Action onEnd)
        {
            Title = title;
            Description = description;
            ResultButtonText = resultButtonText;
            BackgroundSprite = backgroundSprite;
            Options = options;
            OptionEnabled = optionEnabled;
            OnPick = onPick;
            OnEnd = onEnd;
        }

        public string Title { get; }

        public string Description { get; }

        public string ResultButtonText { get; }

        public string BackgroundSprite { get; }

        public IReadOnlyList<string> Options { get; }

        public IReadOnlyList<bool> OptionEnabled { get; }

        public Action<int> OnPick { get; }

        public Action OnEnd { get; }
    }

    internal interface IEventPageHost
    {
        EventPagePanel EventPagePanel { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void SetCenterTitle(string text);

        void OpenEventRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged);
    }

    internal sealed class EventPageCoordinator
    {
        private readonly IEventPageHost _host;

        public EventPageCoordinator(IEventPageHost host)
        {
            _host = host;
        }

        public void Show(EventPageRequest request)
        {
            _host.SwitchTo(GameplayView.Event, () =>
            {
                _host.SetCenterTitle(string.Empty);
                if (_host.EventPagePanel != null)
                {
                    _host.EventPagePanel.Open(
                        request.Title,
                        request.Description,
                        request.ResultButtonText,
                        request.BackgroundSprite,
                        request.Options,
                        request.OptionEnabled,
                        request.OnPick,
                        request.OnEnd);
                }
                else if (!string.IsNullOrWhiteSpace(request.ResultButtonText)
                    || request.Options == null
                    || request.Options.Count == 0)
                {
                    request.OnEnd?.Invoke();
                }
                else
                {
                    request.OnPick?.Invoke(0);
                }
            });
        }

        public void OpenRecipeDishDelete(
            GameRun run,
            string title,
            Action onCancel,
            Action<ActiveTarget> onTargetConfirmed,
            Action onChanged)
        {
            _host.OpenEventRecipeDishDelete(run, title, onCancel, onTargetConfirmed, onChanged);
        }
    }
}
