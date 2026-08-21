using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TipHoverTriggerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void PositionAroundTarget_UsesCompleteVisibleHierarchy_WhenChoosingSide()
        {
            RectTransform parent = CreateRect("Parent", null, new Vector2(1000f, 600f), Vector2.zero);
            RectTransform target = CreateRect("Target", parent, new Vector2(100f, 100f), new Vector2(50f, 0f));
            RectTransform tipRect = CreateRect("Tip", parent, new Vector2(200f, 100f), Vector2.zero);
            RectTransform externalCard = CreateRect(
                "ExternalCard",
                tipRect,
                new Vector2(200f, 100f),
                Vector2.zero);

            var tip = tipRect.gameObject.AddComponent<TipPlacementAwareTestView>();
            tip.Configure(externalCard);
            var trigger = target.gameObject.AddComponent<TipHoverTrigger>();
            trigger.SetTip(
                tip,
                () => tip.gameObject.SetActive(true),
                () => tip.gameObject.SetActive(false));

            trigger.BeginForcedShow(target);

            Assert.That(tip.WasPlacedLeft, Is.True);
            Assert.That(tipRect.anchoredPosition.x, Is.LessThan(target.anchoredPosition.x));
            Rect visible = VisibleRect(parent, tipRect);
            Assert.That(visible.xMin, Is.GreaterThanOrEqualTo(parent.rect.xMin + 11.99f));
            Assert.That(visible.xMax, Is.LessThanOrEqualTo(parent.rect.xMax - 11.99f));
        }

        private RectTransform CreateRect(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 position)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            if (_root == null)
            {
                _root = gameObject;
            }

            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static Rect VisibleRect(RectTransform parent, RectTransform root)
        {
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            var corners = new Vector3[4];
            foreach (Graphic graphic in graphics)
            {
                ((RectTransform)graphic.transform).GetWorldCorners(corners);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 point = parent.InverseTransformPoint(corners[i]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }

    public sealed class TipPlacementAwareTestView : MonoBehaviour, ITooltipPlacementAware
    {
        private const float Gap = 18f;
        private RectTransform _externalCard;

        public bool WasPlacedLeft { get; private set; }

        public void Configure(RectTransform externalCard)
        {
            _externalCard = externalCard;
        }

        public void OnPlacedAroundTarget(bool placedLeftOfTarget)
        {
            WasPlacedLeft = placedLeftOfTarget;
            _externalCard.anchorMin = new Vector2(placedLeftOfTarget ? 0f : 1f, 0.5f);
            _externalCard.anchorMax = _externalCard.anchorMin;
            _externalCard.pivot = new Vector2(placedLeftOfTarget ? 1f : 0f, 0.5f);
            _externalCard.anchoredPosition = new Vector2(placedLeftOfTarget ? -Gap : Gap, 0f);
        }
    }
}
