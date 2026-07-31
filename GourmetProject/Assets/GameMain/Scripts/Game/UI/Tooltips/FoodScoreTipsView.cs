using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 模块 2：分数堆叠。视觉上从下到上依次是：美味度、倍率、分数。
    /// </summary>
    public sealed class FoodScoreTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _multiplierText;

        public void Bind(FoodScoreTipsData data)
        {
            if (!ValidateReferences())
            {
                return;
            }

            data ??= FoodScoreTipsData.Empty;
            _scoreText.text = $"分数 {FoodTipUiUtility.FormatNumber(data.Score)}";
            _multiplierText.text = $"倍率 x{FoodTipUiUtility.FormatNumber(data.Multiplier)}";
            Show();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        private void Awake()
        {
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_scoreText, nameof(_scoreText));
            valid &= ReportMissing(_multiplierText, nameof(_multiplierText));
            return valid;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodScoreTipsView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
