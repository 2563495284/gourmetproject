using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
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
        private bool _isFinalWeek;

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

            IRandomStream rng = GameApp.Random.Stream($"reward_w{_run.WeekIndex}");
            string reward = RewardGranter.Grant(_run, _run.CurrentWeek, rng);
            _isFinalWeek = !_run.HasNextWeek;

            Refresh(total, target, reward);
        }

        private void Refresh(int total, int target, string reward)
        {
            _titleText.text = _isFinalWeek ? "通关！" : $"第 {_run.WeekIndex} 周 · 过关！";
            _scoreText.text = $"得分 {total} / 目标 {target}";
            _rewardText.text = reward;
            _goldText.text = $"当前金币 {_run.Gold}";

            // 配置周打完后进入（或继续）无尽模式或返回菜单；否则只给「进入下一周」。
            _continueButton.gameObject.SetActive(!_isFinalWeek);
            _endlessButton.gameObject.SetActive(_isFinalWeek);
            _menuButton.gameObject.SetActive(_isFinalWeek);

            if (_isFinalWeek)
            {
                Text endlessLabel = _endlessButton.GetComponentInChildren<Text>();
                if (endlessLabel != null)
                {
                    endlessLabel.text = _run.IsEndless ? "继续挑战" : "进入无尽模式";
                }
            }
        }

        private void OnContinue()
        {
            _run.WeekIndex++;
            _run.RequiredScoreOverride = -1;
            RunPersistence.Save(_run);
            Close();
            BattleForm.Active?.BeginWeek();
        }

        private void OnReturnMenu()
        {
            // 保留存档，便于之后从主菜单「继续游戏」回到无尽进度。
            RunPersistence.Save(_run);
            Close();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
