using System.Collections.Generic;
using GourmetProject.Game.Presentation.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 临时桌是实心 Overlay，世界棋子会被框盖住。
    /// 这里把临时桌里的菜再画一层 Overlay Image，叠在绿底上面。
    /// </summary>
    public sealed class TemporaryAreaDishOverlay : MonoBehaviour
    {
        private readonly List<Image> _images = new List<Image>();
        private readonly List<Material> _flavorMaterials = new List<Material>();
        private readonly HashSet<DishPieceView> _hiddenBodies = new HashSet<DishPieceView>();
        private readonly HashSet<DishPieceView> _keepHidden = new HashSet<DishPieceView>();
        private readonly List<DishPieceView> _visiblePieces = new List<DishPieceView>();
        private readonly List<DishPieceView> _restoreBodies = new List<DishPieceView>();

        private BattleWorldController _world;
        private RectTransform _root;
        private Canvas _canvas;

        public void Bind(BattleWorldController world)
        {
            _world = world;
            _root = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
        }

        private void LateUpdate()
        {
            if (_world == null || _world.WorldCamera == null || _root == null)
            {
                RestoreHiddenBodies(null);
                HideUnused(0);
                return;
            }

            _world.CopyTemporaryAreaOverlayPieces(_visiblePieces);
            DishPieceView dragging = _world.ActiveDragPiece;
            int shown = 0;
            _keepHidden.Clear();
            for (int i = 0; i < _visiblePieces.Count; i++)
            {
                DishPieceView piece = _visiblePieces[i];
                if (piece == null || piece == dragging)
                {
                    continue;
                }

                if (!piece.TryCaptureGrabVisual(_world.WorldCamera, out DishGrabVisualSnapshot visual)
                    || visual.Sprite == null)
                {
                    continue;
                }

                Image image = EnsureImage(shown);
                ApplyVisual(image, visual);
                piece.ApplyFlavorVisualToGraphic(image, EnsureFlavorMaterial(shown));
                image.gameObject.SetActive(true);
                piece.SetBodyRenderersEnabled(false);
                _hiddenBodies.Add(piece);
                _keepHidden.Add(piece);
                shown++;
            }

            RestoreHiddenBodies(_keepHidden);
            HideUnused(shown);
        }

        private void OnDisable()
        {
            RestoreHiddenBodies(null);
            HideUnused(0);
        }

        private Image EnsureImage(int index)
        {
            while (_images.Count <= index)
            {
                var go = new GameObject(
                    $"TemporaryAreaDish_{_images.Count}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                go.layer = gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(_root, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                Image image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                _images.Add(image);
            }

            return _images[index];
        }

        private Material EnsureFlavorMaterial(int index)
        {
            while (_flavorMaterials.Count <= index)
            {
                Material source = SpriteRenderStyle.SpriteFlavorOrganicMaterial;
                _flavorMaterials.Add(source != null
                    ? new Material(source)
                    {
                        name = $"TemporaryAreaDishFlavor_{_flavorMaterials.Count}",
                        hideFlags = HideFlags.DontSave,
                    }
                    : null);
            }

            return _flavorMaterials[index];
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _flavorMaterials.Count; i++)
            {
                if (_flavorMaterials[i] != null)
                {
                    Destroy(_flavorMaterials[i]);
                }
            }

            _flavorMaterials.Clear();
        }

        private void ApplyVisual(Image image, DishGrabVisualSnapshot visual)
        {
            RectTransform rect = image.rectTransform;
            Vector2 target = ScreenToLocal(visual.ScreenCenter);
            Vector2 half = visual.ScreenSize * 0.5f;
            Vector2 localMin = ScreenToLocal(visual.ScreenCenter - half);
            Vector2 localMax = ScreenToLocal(visual.ScreenCenter + half);
            rect.sizeDelta = new Vector2(
                Mathf.Max(8f, Mathf.Abs(localMax.x - localMin.x)),
                Mathf.Max(8f, Mathf.Abs(localMax.y - localMin.y)));
            rect.anchoredPosition = target;
            rect.localRotation = Quaternion.Euler(0f, 0f, visual.ScreenRotationDegrees);
            rect.localScale = new Vector3(
                visual.FlipX ? -1f : 1f,
                visual.FlipY ? -1f : 1f,
                1f);
            image.sprite = visual.Sprite;
            image.color = visual.Color;
            image.enabled = true;
        }

        private void HideUnused(int shown)
        {
            for (int i = shown; i < _images.Count; i++)
            {
                if (_images[i] != null)
                {
                    _images[i].gameObject.SetActive(false);
                }
            }
        }

        private void RestoreHiddenBodies(HashSet<DishPieceView> keepHidden)
        {
            if (_hiddenBodies.Count == 0)
            {
                return;
            }

            DishPieceView dragging = _world != null ? _world.ActiveDragPiece : null;
            _restoreBodies.Clear();
            foreach (DishPieceView piece in _hiddenBodies)
            {
                if (piece == null || piece == dragging)
                {
                    continue;
                }

                if (keepHidden != null && keepHidden.Contains(piece))
                {
                    continue;
                }

                _restoreBodies.Add(piece);
            }

            for (int i = 0; i < _restoreBodies.Count; i++)
            {
                _restoreBodies[i].SetBodyRenderersEnabled(true);
                _hiddenBodies.Remove(_restoreBodies[i]);
            }

            _hiddenBodies.RemoveWhere(piece => piece == null);
        }

        private Vector2 ScreenToLocal(Vector2 screenPoint)
        {
            Camera uiCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPoint, uiCamera, out Vector2 local);
            return local;
        }
    }
}
