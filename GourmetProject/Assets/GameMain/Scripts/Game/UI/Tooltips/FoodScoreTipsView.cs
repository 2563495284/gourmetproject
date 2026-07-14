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
        [SerializeField] private Text _deliciousnessText;

        public void Bind(FoodScoreTipsData data)
        {
            EnsureStructure();
            data ??= FoodScoreTipsData.Empty;
            _scoreText.text = $"分数 {FoodTipUiUtility.FormatNumber(data.Score)}";
            _multiplierText.text = $"倍率 x{FoodTipUiUtility.FormatNumber(data.Multiplier)}";
            _deliciousnessText.text = $"美味度 {FoodTipUiUtility.FormatNumber(data.Deliciousness)}";
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
            EnsureStructure();
        }

        private void Reset()
        {
            EnsureStructure();
        }

        private void EnsureStructure()
        {
            RectTransform root = FoodTipUiUtility.EnsureRect(gameObject);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 150f);
            FoodTipUiUtility.EnsurePanelImage(gameObject, new Color(1f, 0.86f, 0.58f, 0.98f));
            _canvasGroup ??= gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            var layout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.padding = new RectOffset(10, 10, 6, 8);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 顶到底：分数、倍率、美味度，因此视觉从下往上读就是需求里的顺序。
            _scoreText = FoodTipUiUtility.EnsureTextChild(root, _scoreText, "Score", 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            _multiplierText = FoodTipUiUtility.EnsureTextChild(root, _multiplierText, "Multiplier", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
            _deliciousnessText = FoodTipUiUtility.EnsureTextChild(root, _deliciousnessText, "Deliciousness", 22, FontStyle.Bold, TextAnchor.MiddleCenter);
        }
    }
}
