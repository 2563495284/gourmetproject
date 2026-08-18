using GourmetProject.Game.Presentation.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// Overlay 拖拽残影：世界棋子永远画在 Overlay HUD 下面，
    /// 出餐口拖拽时用更高 sortingOrder 的 UI Image 跟着指针，盖在出餐口上。
    /// </summary>
    public sealed class DishDragGhostOverlay : MonoBehaviour
    {
        private BattleWorldController _world;
        private RectTransform _root;
        private Canvas _canvas;
        private Image _image;
        private RectTransform _imageRect;
        private Material _flavorMaterial;
        private DishPieceView _hiddenBodyPiece;

        public void Bind(BattleWorldController world)
        {
            _world = world;
            _root = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            EnsureImage();
            HideImage();
        }

        private void LateUpdate()
        {
            DishPieceView piece = _world != null ? _world.ActiveDragPiece : null;
            RestoreHiddenBodyIfChanged(piece);
            if (piece == null || _world == null || _world.WorldCamera == null)
            {
                HideImage();
                return;
            }

            if (!piece.TryCaptureGrabVisual(_world.WorldCamera, out DishGrabVisualSnapshot visual))
            {
                HideImage();
                return;
            }

            piece.SetBodyRenderersEnabled(false);
            _hiddenBodyPiece = piece;
            ApplyVisual(piece, visual);
        }

        private void OnDisable()
        {
            RestoreHiddenBodyIfChanged(null);
            HideImage();
        }

        private void EnsureImage()
        {
            if (_image != null)
            {
                return;
            }

            var go = new GameObject("Ghost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            _imageRect = (RectTransform)go.transform;
            _imageRect.SetParent(_root, false);
            _imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            _imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _imageRect.pivot = new Vector2(0.5f, 0.5f);
            _image = go.GetComponent<Image>();
            _image.raycastTarget = false;
            _image.preserveAspect = true;
        }

        private void ApplyVisual(DishPieceView piece, DishGrabVisualSnapshot visual)
        {
            EnsureImage();
            Vector2 target = ScreenToLocal(visual.ScreenCenter);
            Vector2 half = visual.ScreenSize * 0.5f;
            Vector2 localMin = ScreenToLocal(visual.ScreenCenter - half);
            Vector2 localMax = ScreenToLocal(visual.ScreenCenter + half);
            _imageRect.sizeDelta = new Vector2(
                Mathf.Max(8f, Mathf.Abs(localMax.x - localMin.x)),
                Mathf.Max(8f, Mathf.Abs(localMax.y - localMin.y)));
            _imageRect.anchoredPosition = target;
            _imageRect.localRotation = Quaternion.Euler(0f, 0f, visual.ScreenRotationDegrees);
            _imageRect.localScale = new Vector3(
                visual.FlipX ? -1f : 1f,
                visual.FlipY ? -1f : 1f,
                1f);
            _image.sprite = visual.Sprite;
            _image.color = visual.Color;
            _image.enabled = visual.Sprite != null;
            _imageRect.gameObject.SetActive(visual.Sprite != null);
            EnsureFlavorMaterial();
            piece.ApplyFlavorVisualToGraphic(_image, _flavorMaterial);
        }

        private void EnsureFlavorMaterial()
        {
            if (_flavorMaterial != null || SpriteRenderStyle.SpriteFlavorOrganicMaterial == null)
            {
                return;
            }

            _flavorMaterial = new Material(SpriteRenderStyle.SpriteFlavorOrganicMaterial)
            {
                name = "DragGhostFlavorOrganic",
                hideFlags = HideFlags.DontSave,
            };
        }

        private void OnDestroy()
        {
            if (_flavorMaterial != null)
            {
                Destroy(_flavorMaterial);
                _flavorMaterial = null;
            }
        }

        private void HideImage()
        {
            if (_imageRect != null)
            {
                _imageRect.gameObject.SetActive(false);
            }
        }

        private void RestoreHiddenBodyIfChanged(DishPieceView current)
        {
            if (_hiddenBodyPiece != null && _hiddenBodyPiece != current)
            {
                _hiddenBodyPiece.SetBodyRenderersEnabled(true);
            }

            if (current == null)
            {
                _hiddenBodyPiece = null;
            }
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
