using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 食物 hover Tips 组合器：只负责组合/定位 1、2、3 等独立模块。
    /// </summary>
    public sealed class FoodTipsView : MonoBehaviour
    {
        private const string CountAsTermId = "term_food_count_as";
        private const float ShowDuration = 0.12f;
        private const float HideDuration = 0.08f;
        private const float MinimumTermCardWidth = 180f;

        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private FoodScoreTipsView _scoreView;
        [SerializeField] private FoodSummaryTipsView _summaryView;
        [SerializeField] private RectTransform _flavorDetailsRoot;
        [SerializeField] private RectTransform _specialTagsRoot;
        [FormerlySerializedAs("_transferredSubSkillsRoot")]
        [SerializeField] private RectTransform _externalSkillsRoot;
        [SerializeField] private FoodTipCardView _infoCardPrefab;
        [SerializeField] private FoodFlavorDetailView _flavorDetailPrefab;

        private Tween _visibilityTween;

        [Header("Layout")]
        [SerializeField] private float _targetGap = 18f;
        [SerializeField] private float _screenPadding = 16f;
        [SerializeField] private float _detailGap = 10f;

        public FoodScoreTipsView ScoreView
        {
            get
            {
                ValidateReferences();
                return _scoreView;
            }
        }

        public FoodSummaryTipsView SummaryView
        {
            get
            {
                ValidateReferences();
                return _summaryView;
            }
        }

        public void Bind(DishInstance dish, DiningTable table, GameplayDatabase db, ScoreResult scoreResult = null)
        {
            Bind(FoodTipsDataFactory.Build(dish, table, db, scoreResult));
        }

        public void Bind(FoodTipsData data)
        {
            if (!ValidateReferences())
            {
                return;
            }

            data ??= new FoodTipsData(null, null, null, null, null);

            _scoreView.Bind(data.Score);
            _summaryView.Bind(data.Summary);
            BuildFlavorDetails(data.FlavorDetails);
            BuildInfoCards(
                _specialTagsRoot,
                BuildSpecialTagsWithCountAs(data.SpecialTags, data.Summary.CountAs),
                "SpecialTag",
                MinimumTermCardWidth);
            BuildInfoCards(_externalSkillsRoot, data.ExternalSkills, "ExternalSkill");
        }

        public void Show()
        {
            ValidateReferences();
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);
            _summaryView?.RefreshLayoutAfterActivation();
            Canvas.ForceUpdateCanvases();
            if (_canvasGroup != null)
            {
                KillVisibilityTween();
                if (!wasActive)
                {
                    _canvasGroup.alpha = 0f;
                }

                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
                _visibilityTween = _canvasGroup
                    .DOFade(1f, ShowDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() => _visibilityTween = null);
            }
        }

        public void Hide()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            if (_canvasGroup == null || !Application.isPlaying)
            {
                gameObject.SetActive(false);
                return;
            }

            KillVisibilityTween();
            _visibilityTween = _canvasGroup
                .DOFade(0f, HideDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _visibilityTween = null;
                    gameObject.SetActive(false);
                });
        }

        public void PlaceAroundWorldBounds(Bounds worldBounds, Camera worldCamera, Canvas canvas)
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                return;
            }

            canvas ??= GetComponentInParent<Canvas>();
            RectTransform parent = transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RectTransform canvasRect = parent.parent as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Rect targetRect = WorldBoundsToLocalRect(worldBounds, worldCamera, canvasRect, uiCamera);
            Canvas.ForceUpdateCanvases();

            PlaceAbove(_scoreView.transform as RectTransform, targetRect, canvasRect.rect);
            SeparateScoreFromTarget(canvasRect, targetRect);
            PlaceSummaryGroup(targetRect, canvasRect);
            SeparateSummaryGroupFromPrimaryModules(canvasRect, targetRect);
        }

        public void PlaceAroundRectTransform(RectTransform target, Canvas canvas)
        {
            if (target == null)
            {
                return;
            }

            canvas ??= GetComponentInParent<Canvas>();
            RectTransform parent = transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RectTransform canvasRect = parent.parent as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Rect targetRect = RectTransformToLocalRect(target, canvasRect);
            Canvas.ForceUpdateCanvases();

            PlaceAbove(_scoreView.transform as RectTransform, targetRect, canvasRect.rect);
            SeparateScoreFromTarget(canvasRect, targetRect);
            PlaceSummaryGroup(targetRect, canvasRect);
            SeparateSummaryGroupFromPrimaryModules(canvasRect, targetRect);
        }

        private void PlaceSummaryGroup(Rect targetRect, RectTransform canvasRect)
        {
            RectTransform summaryRect = _summaryView.transform as RectTransform;
            bool placeOnRight = ShouldPlaceSummaryGroupOnRight(
                targetRect,
                canvasRect.rect,
                summaryRect);
            PlaceSummaryTopSide(summaryRect, targetRect, placeOnRight);

            // Detail lists belong to the summary card. Keep their intended relative positions
            // first. Align their target-facing edges instead of their centers so a wide detail
            // card grows away from the food and cannot drag the narrower summary card with it.
            PlaceBelow(_flavorDetailsRoot, summaryRect);
            PlaceAbove(_specialTagsRoot, summaryRect);
            AlignToTargetSide(_flavorDetailsRoot, targetRect, placeOnRight);
            AlignToTargetSide(_specialTagsRoot, targetRect, placeOnRight);
            if (placeOnRight)
            {
                PlaceRight(_externalSkillsRoot, summaryRect);
            }
            else
            {
                PlaceLeft(_externalSkillsRoot, summaryRect);
            }

            Canvas.ForceUpdateCanvases();
            ClampSummaryGroupToBounds(canvasRect, canvasRect.rect, summaryRect);
        }

        private void Awake()
        {
            ValidateReferences();
            Hide();
        }

        private void OnDisable()
        {
            KillVisibilityTween();
        }

        private void KillVisibilityTween()
        {
            if (_visibilityTween == null)
            {
                return;
            }

            _visibilityTween.Kill();
            _visibilityTween = null;
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_canvasGroup, nameof(_canvasGroup));
            valid &= ReportMissing(_scoreView, nameof(_scoreView));
            valid &= ReportMissing(_summaryView, nameof(_summaryView));
            valid &= ReportMissing(_flavorDetailsRoot, nameof(_flavorDetailsRoot));
            valid &= ReportMissing(_specialTagsRoot, nameof(_specialTagsRoot));
            valid &= ReportMissing(_externalSkillsRoot, nameof(_externalSkillsRoot));
            valid &= ReportMissing(_infoCardPrefab, nameof(_infoCardPrefab));
            valid &= ReportMissing(_flavorDetailPrefab, nameof(_flavorDetailPrefab));
            return valid;
        }

        private void BuildFlavorDetails(IReadOnlyList<FoodInfoEntry> entries)
        {
            FoodTipUiUtility.ClearChildren(_flavorDetailsRoot);
            int count = entries != null ? entries.Count : 0;
            _flavorDetailsRoot.gameObject.SetActive(count > 0);
            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry entry = entries[i];
                FoodFlavorDetailView card = Instantiate(_flavorDetailPrefab, _flavorDetailsRoot, false);
                card.name = $"FlavorDetail_{i}";
                card.Bind(entry.Title, entry.Desc);
            }
        }

        private void BuildInfoCards(
            RectTransform root,
            IReadOnlyList<FoodInfoEntry> entries,
            string prefix,
            float minimumWidth = 0f)
        {
            FoodTipUiUtility.ClearChildren(root);
            int count = entries != null ? entries.Count : 0;
            root.gameObject.SetActive(count > 0);
            float preferredWidth = minimumWidth;
            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry entry = entries[i];
                FoodTipCardView card = Instantiate(_infoCardPrefab, root, false);
                card.name = $"{prefix}_{i}";
                card.Bind(entry.Title, entry.Desc);
                if (minimumWidth > 0f)
                {
                    preferredWidth = Mathf.Max(preferredWidth, card.PreferredSingleLineWidth);
                }
            }

            if (minimumWidth > 0f)
            {
                root.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    Mathf.Ceil(preferredWidth));
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            }
        }

        private static IReadOnlyList<FoodInfoEntry> BuildSpecialTagsWithCountAs(
            IReadOnlyList<FoodInfoEntry> entries,
            int countAs)
        {
            if (countAs <= 1)
            {
                return entries;
            }

            int sourceCount = entries?.Count ?? 0;
            var result = new List<FoodInfoEntry>(sourceCount + 1);
            for (int i = 0; i < sourceCount; i++)
            {
                result.Add(entries[i]);
            }

            cfg.Term term = GameApp.Config?.Tables?.TbTerm?.GetOrDefault(CountAsTermId);
            string title = !string.IsNullOrEmpty(term?.Name) ? term.Name : "食物";
            string template = !string.IsNullOrEmpty(term?.Desc) ? term.Desc : "视为{x}份食物";
            string desc = template.Replace("{x}", countAs.ToString());
            result.Add(new FoodInfoEntry(title, desc));
            return result;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodTipsView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
        private Rect WorldBoundsToLocalRect(Bounds bounds, Camera worldCamera, RectTransform parent, Camera uiCamera)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] world =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
            };

            bool has = false;
            Vector2 localMin = Vector2.zero;
            Vector2 localMax = Vector2.zero;
            foreach (Vector3 point in world)
            {
                Vector2 screen = worldCamera.WorldToScreenPoint(point);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
                {
                    continue;
                }

                if (!has)
                {
                    localMin = localMax = local;
                    has = true;
                }
                else
                {
                    localMin = Vector2.Min(localMin, local);
                    localMax = Vector2.Max(localMax, local);
                }
            }

            return has
                ? Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y)
                : new Rect(Vector2.zero, Vector2.one);
        }

        private Rect RectTransformToLocalRect(RectTransform target, RectTransform parent)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 localMin = parent.InverseTransformPoint(corners[0]);
            Vector2 localMax = localMin;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 local = parent.InverseTransformPoint(corners[i]);
                localMin = Vector2.Min(localMin, local);
                localMax = Vector2.Max(localMax, local);
            }

            return Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y);
        }

        private void PlaceLeft(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.xMin - _targetGap - size.x * 0.5f, target.center.y);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceAbove(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.center.x, target.yMax + _targetGap + size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceSummaryTopSide(RectTransform rect, Rect target, bool placeOnRight)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            float centerX = placeOnRight
                ? target.xMax + _targetGap + size.x * 0.5f
                : target.xMin - _targetGap - size.x * 0.5f;
            SetCenter(rect, new Vector2(centerX, target.yMax - size.y * 0.5f));
        }

        private bool ShouldPlaceSummaryGroupOnRight(
            Rect target,
            Rect bounds,
            RectTransform summaryRect)
        {
            float groupWidth = SummaryGroupSideWidth(summaryRect);
            float rightSpace = bounds.xMax
                - _screenPadding
                - target.xMax
                - _targetGap;
            float leftSpace = target.xMin
                - _targetGap
                - bounds.xMin
                - _screenPadding;
            bool fitsRight = groupWidth <= rightSpace;
            bool fitsLeft = groupWidth <= leftSpace;
            if (fitsRight != fitsLeft)
            {
                return fitsRight;
            }

            // Preserve the established right-side placement when both sides fit.
            // If neither side fits, use the roomier side to minimize later clamping.
            return fitsRight || rightSpace >= leftSpace;
        }

        private float SummaryGroupSideWidth(RectTransform summaryRect)
        {
            float summaryWidth = ActiveWidth(summaryRect);
            float columnWidth = Mathf.Max(
                summaryWidth,
                Mathf.Max(ActiveWidth(_flavorDetailsRoot), ActiveWidth(_specialTagsRoot)));
            float externalWidth = ActiveWidth(_externalSkillsRoot);
            if (externalWidth > 0f)
            {
                columnWidth = Mathf.Max(
                    columnWidth,
                    summaryWidth + _detailGap + externalWidth);
            }

            return columnWidth;
        }

        private float ActiveWidth(RectTransform rect)
        {
            return rect != null && rect.gameObject.activeSelf
                ? PreferredSize(rect).x
                : 0f;
        }

        private void AlignToTargetSide(RectTransform rect, Rect target, bool placeOnRight)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = RectCenterInParent(rect);
            center.x = placeOnRight
                ? target.xMax + _targetGap + size.x * 0.5f
                : target.xMin - _targetGap - size.x * 0.5f;
            SetCenter(rect, center);
        }

        private void PlaceBelow(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y - anchorSize.y * 0.5f - _detailGap - size.y * 0.5f);
            SetCenter(rect, center);
        }

        private void PlaceAbove(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y + anchorSize.y * 0.5f + _detailGap + size.y * 0.5f);
            SetCenter(rect, center);
        }

        private void PlaceRight(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x + anchorSize.x * 0.5f + _detailGap + size.x * 0.5f, anchorCenter.y);
            SetCenter(rect, center);
        }

        private void PlaceLeft(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x - anchorSize.x * 0.5f - _detailGap - size.x * 0.5f, anchorCenter.y);
            SetCenter(rect, center);
        }

        private void ClampSummaryGroupToBounds(RectTransform canvasRect, Rect bounds, RectTransform summaryRect)
        {
            RectTransform[] group =
            {
                summaryRect,
                _flavorDetailsRoot,
                _specialTagsRoot,
                _externalSkillsRoot,
            };

            bool hasVisibleRect = false;
            Rect visibleRect = default;
            for (int i = 0; i < group.Length; i++)
            {
                RectTransform rect = group[i];
                if (rect == null || !rect.gameObject.activeSelf)
                {
                    continue;
                }

                Rect childRect = RectTransformToLocalRect(rect, canvasRect);
                if (!hasVisibleRect)
                {
                    visibleRect = childRect;
                    hasVisibleRect = true;
                }
                else
                {
                    visibleRect = Rect.MinMaxRect(
                        Mathf.Min(visibleRect.xMin, childRect.xMin),
                        Mathf.Min(visibleRect.yMin, childRect.yMin),
                        Mathf.Max(visibleRect.xMax, childRect.xMax),
                        Mathf.Max(visibleRect.yMax, childRect.yMax));
                }
            }

            if (!hasVisibleRect)
            {
                return;
            }

            float minX = bounds.xMin + _screenPadding;
            float maxX = bounds.xMax - _screenPadding;
            float minY = bounds.yMin + _screenPadding;
            float maxY = bounds.yMax - _screenPadding;
            Vector2 offset = Vector2.zero;

            if (visibleRect.width > maxX - minX)
            {
                offset.x = bounds.center.x - visibleRect.center.x;
            }
            else if (visibleRect.xMax > maxX)
            {
                offset.x = maxX - visibleRect.xMax;
            }
            else if (visibleRect.xMin < minX)
            {
                offset.x = minX - visibleRect.xMin;
            }

            if (visibleRect.height > maxY - minY)
            {
                offset.y = bounds.center.y - visibleRect.center.y;
            }
            else if (visibleRect.yMax > maxY)
            {
                offset.y = maxY - visibleRect.yMax;
            }
            else if (visibleRect.yMin < minY)
            {
                offset.y = minY - visibleRect.yMin;
            }

            for (int i = 0; i < group.Length; i++)
            {
                RectTransform rect = group[i];
                if (rect != null && rect.gameObject.activeSelf)
                {
                    rect.anchoredPosition += offset;
                }
            }
        }

        private void SeparateScoreFromTarget(RectTransform canvasRect, Rect targetRect)
        {
            RectTransform scoreRect = _scoreView.transform as RectTransform;
            if (scoreRect == null || !scoreRect.gameObject.activeSelf)
            {
                return;
            }

            var obstacles = new List<Rect>(1)
            {
                Expand(targetRect, _targetGap),
            };
            Vector2 offset = BestSeparationOffset(
                RectTransformToLocalRect(scoreRect, canvasRect),
                obstacles,
                InsetBounds(canvasRect.rect));
            scoreRect.anchoredPosition += offset;
        }

        private void SeparateSummaryGroupFromPrimaryModules(
            RectTransform canvasRect,
            Rect targetRect)
        {
            RectTransform[] summaryGroup =
            {
                _summaryView.transform as RectTransform,
                _flavorDetailsRoot,
                _specialTagsRoot,
                _externalSkillsRoot,
            };
            if (!TryGroupBounds(summaryGroup, canvasRect, out Rect groupBounds))
            {
                return;
            }

            var obstacles = new List<Rect>(2)
            {
                Expand(targetRect, _targetGap),
            };
            AddVisibleObstacle(obstacles, _scoreView.transform as RectTransform, canvasRect);
            Vector2 bestOffset = BestSeparationOffset(
                groupBounds,
                obstacles,
                InsetBounds(canvasRect.rect));
            if (bestOffset.sqrMagnitude <= 0.01f)
            {
                return;
            }

            for (int i = 0; i < summaryGroup.Length; i++)
            {
                RectTransform rect = summaryGroup[i];
                if (rect != null && rect.gameObject.activeSelf)
                {
                    rect.anchoredPosition += bestOffset;
                }
            }
        }

        private static Vector2 BestSeparationOffset(
            Rect movingBounds,
            IReadOnlyList<Rect> obstacles,
            Rect allowedBounds)
        {
            var candidates = new List<Vector2>(5) { Vector2.zero };
            float moveRight = 0f;
            float moveLeft = 0f;
            float moveUp = 0f;
            float moveDown = 0f;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Rect obstacle = obstacles[i];
                if (!movingBounds.Overlaps(obstacle))
                {
                    continue;
                }

                moveRight = Mathf.Max(moveRight, obstacle.xMax - movingBounds.xMin);
                moveLeft = Mathf.Min(moveLeft, obstacle.xMin - movingBounds.xMax);
                moveUp = Mathf.Max(moveUp, obstacle.yMax - movingBounds.yMin);
                moveDown = Mathf.Min(moveDown, obstacle.yMin - movingBounds.yMax);
            }

            if (moveRight > 0f)
            {
                candidates.Add(new Vector2(moveRight, 0f));
            }

            if (moveLeft < 0f)
            {
                candidates.Add(new Vector2(moveLeft, 0f));
            }

            if (moveUp > 0f)
            {
                candidates.Add(new Vector2(0f, moveUp));
            }

            if (moveDown < 0f)
            {
                candidates.Add(new Vector2(0f, moveDown));
            }

            Vector2 bestOffset = Vector2.zero;
            float bestScore = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 offset = ClampGroupOffset(candidates[i], movingBounds, allowedBounds);
                Rect moved = Offset(movingBounds, offset);
                float overlap = 0f;
                for (int j = 0; j < obstacles.Count; j++)
                {
                    overlap += OverlapArea(moved, obstacles[j]);
                }

                float score = overlap * 1000000f + offset.sqrMagnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                }
            }

            return bestOffset;
        }

        private void AddVisibleObstacle(
            ICollection<Rect> obstacles,
            RectTransform rect,
            RectTransform canvasRect)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            obstacles.Add(Expand(RectTransformToLocalRect(rect, canvasRect), _detailGap));
        }

        private bool TryGroupBounds(
            IReadOnlyList<RectTransform> group,
            RectTransform canvasRect,
            out Rect bounds)
        {
            bool hasBounds = false;
            bounds = default;
            for (int i = 0; i < group.Count; i++)
            {
                RectTransform rect = group[i];
                if (rect == null || !rect.gameObject.activeSelf)
                {
                    continue;
                }

                Rect childBounds = RectTransformToLocalRect(rect, canvasRect);
                bounds = hasBounds
                    ? Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, childBounds.xMin),
                        Mathf.Min(bounds.yMin, childBounds.yMin),
                        Mathf.Max(bounds.xMax, childBounds.xMax),
                        Mathf.Max(bounds.yMax, childBounds.yMax))
                    : childBounds;
                hasBounds = true;
            }

            return hasBounds;
        }

        private static Rect Expand(Rect rect, float padding)
        {
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }

        private Rect InsetBounds(Rect rect)
        {
            return Rect.MinMaxRect(
                rect.xMin + _screenPadding,
                rect.yMin + _screenPadding,
                rect.xMax - _screenPadding,
                rect.yMax - _screenPadding);
        }

        private static Rect Offset(Rect rect, Vector2 offset)
        {
            rect.position += offset;
            return rect;
        }

        private static Vector2 ClampGroupOffset(Vector2 offset, Rect group, Rect bounds)
        {
            if (group.width > bounds.width)
            {
                offset.x = bounds.center.x - group.center.x;
            }
            else
            {
                offset.x = Mathf.Clamp(offset.x, bounds.xMin - group.xMin, bounds.xMax - group.xMax);
            }

            if (group.height > bounds.height)
            {
                offset.y = bounds.center.y - group.center.y;
            }
            else
            {
                offset.y = Mathf.Clamp(offset.y, bounds.yMin - group.yMin, bounds.yMax - group.yMax);
            }

            return offset;
        }

        private static float OverlapArea(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return width * height;
        }

        private Vector2 PreferredSize(RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Vector2 size = rect.rect.size;
            size.y = Mathf.Max(size.y, LayoutUtility.GetPreferredHeight(rect));
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            return size;
        }

        private Vector2 Clamp(Vector2 center, Vector2 size, Rect bounds)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(center.x, bounds.xMin + halfW + _screenPadding, bounds.xMax - halfW - _screenPadding),
                Mathf.Clamp(center.y, bounds.yMin + halfH + _screenPadding, bounds.yMax - halfH - _screenPadding));
        }

        private static void SetCenter(RectTransform rect, Vector2 center)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
        }

        private static Vector2 RectCenterInParent(RectTransform rect)
        {
            RectTransform parent = rect.parent as RectTransform;
            if (parent == null)
            {
                return rect.anchoredPosition;
            }

            Vector3 worldCenter = rect.TransformPoint(rect.rect.center);
            return parent.InverseTransformPoint(worldCenter);
        }
    }
}
