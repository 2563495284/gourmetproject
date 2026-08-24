using System;
using GourmetProject.Game.Run;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>固定六槽、按行优先顺序显示星级评鉴进度。</summary>
    public sealed class StarProgressView : MonoBehaviour
    {
        [SerializeField] private Image[] _stars = new Image[GameRun.MaxRatingStars];
        [SerializeField] private Sprite _starSprite;
        [SerializeField] private Color _earnedColor = Color.white;
        [SerializeField] private Color _lockedColor = new Color(0.42f, 0.39f, 0.35f, 0.28f);

        public int EarnedStars { get; private set; }

        public int SlotCount => _stars?.Length ?? 0;

        public void Bind(int earnedStars)
        {
            EarnedStars = Mathf.Clamp(earnedStars, 0, GameRun.MaxRatingStars);
            if (_stars == null)
            {
                return;
            }

            for (int i = 0; i < _stars.Length; i++)
            {
                Image star = _stars[i];
                if (star == null)
                {
                    continue;
                }

                if (_starSprite != null)
                {
                    star.sprite = _starSprite;
                }

                star.preserveAspect = true;
                star.color = i < EarnedStars ? _earnedColor : _lockedColor;
            }
        }

        public RectTransform GetStarRect(int index)
        {
            return _stars != null && index >= 0 && index < _stars.Length && _stars[index] != null
                ? _stars[index].rectTransform
                : null;
        }

#if UNITY_EDITOR
        internal void EditorConfigure(Image[] stars, Sprite sprite)
        {
            _stars = stars ?? Array.Empty<Image>();
            _starSprite = sprite;
            Bind(0);
        }
#endif
    }
}
