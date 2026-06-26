using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 过关领奖界面：展示本周得分与发放的奖励，点「继续」推进到下一周（或通关返回菜单）。
    /// 结构全固定、落在 RewardForm.prefab，脚本只赋文本并按是否最终周切换按钮组。
    /// 奖励经 RewardGranter 按周命名流发放，并在推进时存档以支持「继续游戏」。
    /// </summary>
    public sealed class RewardForm : UGuiForm
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _rewardText;
        [SerializeField] private Text _goldText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _endlessButton;
        [SerializeField] private Button _menuButton;

        private GameRun _run;
        private RewardOffer _offer;
        private RewardChoice _selectedMain;
        private RewardChoice _selectedExtra;
        private GameObject _choiceRoot;
        private int _lastTotal;
        private int _lastTarget;
        private bool _rewardApplied;
        private string _appliedRewardText;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton.onClick.AddListener(OnContinue);
            _endlessButton.onClick.AddListener(OnContinue);
            _menuButton.onClick.AddListener(OnReturnMenu);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            BattleSession session = BattleForm.Active?.Session;
            int total = session != null && session.IsSettled ? session.LastResult.Total : 0;
            int target = session?.RequiredScore ?? _run.RequiredScore;
            ActionExecutionContext actionContext = BattleForm.Active?.CurrentBattleActionContext;
            string rewardKey = GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);

            _offer = _run.GetPendingRewardOffer(rewardKey);
            if (_offer == null)
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Reward, rewardKey);
                _offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(rewardKey, _offer);
                RunPersistence.Save(_run);
            }

            _selectedMain = FirstOrDefault(_offer.MainChoices);
            _selectedExtra = FirstOrDefault(_offer.ExtraChoices);
            _rewardApplied = false;
            _appliedRewardText = string.Empty;
            _lastTotal = total;
            _lastTarget = target;

            RefreshOffer();
        }

        private void RefreshOffer()
        {
            _titleText.text = "美食达成！";
            _scoreText.text = $"得分 {_lastTotal} / 目标 {_lastTarget}";
            _rewardText.text = _rewardApplied
                ? _appliedRewardText
                : $"固定奖励：金币 +{_offer.BaseGold}\n请选择主奖励{(_offer.HasExtraChoices ? "和额外奖励" : string.Empty)}。";
            _goldText.text = _rewardApplied ? $"当前金币 {_run.Gold}" : $"当前金币 {_run.Gold}（领取后 +{_offer.BaseGold}）";

            // 行动轴模型下发奖不再推进周；只保留「领取并继续」单按钮。
            _continueButton.gameObject.SetActive(true);
            _endlessButton.gameObject.SetActive(false);
            _menuButton.gameObject.SetActive(false);

            SetButtonLabel(_continueButton, _rewardApplied ? "继续" : "领取奖励");

            RebuildChoiceButtons();
        }

        private void OnContinue()
        {
            ApplySelectedRewardIfNeeded();
            RunPersistence.Save(_run);
            Close();
            BattleForm.Active?.OnRewardConfirmed();
        }

        private void OnReturnMenu()
        {
            ApplySelectedRewardIfNeeded();
            RunPersistence.Save(_run);
            Close();
            BattleForm.Active?.OnRewardConfirmed();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void ApplySelectedRewardIfNeeded()
        {
            if (_rewardApplied)
            {
                return;
            }

            _selectedMain ??= FirstOrDefault(_offer.MainChoices);
            _selectedExtra ??= FirstOrDefault(_offer.ExtraChoices);
            _appliedRewardText = RewardGranter.Apply(_run, _offer, _selectedMain, _selectedExtra);
            _run.ClearPendingRewardOffer();
            RunPersistence.Save(_run);
            _rewardApplied = true;
            RefreshOffer();
        }

        private void RebuildChoiceButtons()
        {
            if (_choiceRoot != null)
            {
                Destroy(_choiceRoot);
                _choiceRoot = null;
            }

            if (_rewardApplied || _offer == null || (!_offer.HasMainChoices && !_offer.HasExtraChoices))
            {
                return;
            }

            Transform parent = _rewardText.transform.parent;
            _choiceRoot = new GameObject("RewardChoices", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _choiceRoot.transform.SetParent(parent, false);
            var rootRect = (RectTransform)_choiceRoot.transform;
            rootRect.anchorMin = new Vector2(0.1f, 0.05f);
            rootRect.anchorMax = new Vector2(0.9f, 0.45f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var layout = _choiceRoot.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            AddChoiceSection("主奖励", _offer.MainChoices, _selectedMain, choice =>
            {
                _selectedMain = choice;
                RebuildChoiceButtons();
            });

            AddChoiceSection("额外奖励", _offer.ExtraChoices, _selectedExtra, choice =>
            {
                _selectedExtra = choice;
                RebuildChoiceButtons();
            });
        }

        private void AddChoiceSection(
            string title,
            System.Collections.Generic.IReadOnlyList<RewardChoice> choices,
            RewardChoice selected,
            System.Action<RewardChoice> onSelected)
        {
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            CreateLabel(title);
            for (int i = 0; i < choices.Count; i++)
            {
                RewardChoice choice = choices[i];
                string prefix = choice == selected ? "✓ " : string.Empty;
                CreateChoiceButton(prefix + choice.DisplayText, () => onSelected(choice));
            }
        }

        private void CreateLabel(string text)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_choiceRoot.transform, false);
            var label = go.GetComponent<Text>();
            label.font = _rewardText.font;
            label.fontSize = _rewardText.fontSize;
            label.color = _rewardText.color;
            label.alignment = TextAnchor.MiddleLeft;
            label.text = text;
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, 26f);
        }

        private void CreateChoiceButton(string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("ChoiceButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_choiceRoot.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, 44f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.18f, 0.13f, 0.09f, 0.9f);

            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 2f);
            rect.offsetMax = new Vector2(-12f, -2f);

            var label = textGo.GetComponent<Text>();
            label.font = _rewardText.font;
            label.fontSize = _rewardText.fontSize;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.text = text;
        }

        private static RewardChoice FirstOrDefault(System.Collections.Generic.IReadOnlyList<RewardChoice> choices)
        {
            return choices != null && choices.Count > 0 ? choices[0] : null;
        }

        private static void SetButtonLabel(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
