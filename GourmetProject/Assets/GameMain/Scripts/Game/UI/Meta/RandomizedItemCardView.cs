using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Hud;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    public sealed class RandomizedItemCardView : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _descriptionText;

        public RectTransform Rect => transform as RectTransform;

        public void Bind(RandomizedItemResult result)
        {
            EnsureRefs();

            ItemDefinition item = result?.Item;
            if (_background != null)
            {
                _background.color = item != null ? RunItemSlotView.QualityColor(item.Quality) : new Color(0.55f, 0.49f, 0.40f, 1f);
            }

            if (_icon != null)
            {
                Sprite sprite = RunItemSlotView.LoadIcon(item);
                _icon.sprite = sprite;
                _icon.enabled = sprite != null;
                _icon.preserveAspect = true;
            }

            if (_nameText != null)
            {
                _nameText.text = item != null ? item.Name : "折算金币";
            }

            if (_descriptionText != null)
            {
                _descriptionText.text = result != null && result.AcquireResult.Outcome == ItemAcquireOutcome.ConvertedToGold
                    ? $"金币 +{result.AcquireResult.Gold}"
                    : item != null ? item.Desc : string.Empty;
            }
        }

        private void EnsureRefs()
        {
            if (_background == null)
            {
                _background = GetComponent<Image>();
            }

            if (_icon == null)
            {
                Transform icon = transform.Find("Icon");
                _icon = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (_nameText == null)
            {
                Transform name = transform.Find("Name");
                _nameText = name != null ? name.GetComponent<TMP_Text>() : null;
            }

            if (_descriptionText == null)
            {
                Transform desc = transform.Find("Description");
                _descriptionText = desc != null ? desc.GetComponent<TMP_Text>() : null;
            }
        }
    }
}
