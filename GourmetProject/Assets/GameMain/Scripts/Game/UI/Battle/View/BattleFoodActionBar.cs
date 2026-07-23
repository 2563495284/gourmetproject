using System;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 战斗态底部食物操作条：总览 / 吃（结算）/ 清空涂鸦 / 切换涂鸦四个按钮。
    /// 组件挂在 FoodActions 节点上，负责显隐与按钮可点态/文案；点击回调由壳注入（结算等编排仍在壳内）。
    /// </summary>
    public sealed class BattleFoodActionBar : MonoBehaviour
    {
        [SerializeField] private Button _eatButton;
        [SerializeField] private Button _doodleClearButton;
        [SerializeField] private Button _doodleToggleButton;
        [SerializeField] private Text _doodleToggleText;

        public void Bind(Action onEat, Action onDoodleClear, Action onDoodleToggle)
        {
            Wire(_eatButton, onEat);
            Wire(_doodleClearButton, onDoodleClear);
            Wire(_doodleToggleButton, onDoodleToggle);
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void Refresh(bool food, BattleSession session, BattleWorldController world)
        {
            if (_eatButton != null)
            {
                _eatButton.interactable = food
                    && session != null
                    && !session.IsSettled
                    && session.PreparedServe == null;
            }

            bool doodleReady = food && world != null;
            if (_doodleClearButton != null)
            {
                _doodleClearButton.interactable = doodleReady;
            }

            if (_doodleToggleButton != null)
            {
                _doodleToggleButton.interactable = doodleReady;
            }

            if (_doodleToggleText != null)
            {
                _doodleToggleText.text = world != null ? world.DoodleToggleLabel : "隐藏涂鸦";
            }
        }

        private static void Wire(Button button, Action callback)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => callback?.Invoke());
        }
    }
}
