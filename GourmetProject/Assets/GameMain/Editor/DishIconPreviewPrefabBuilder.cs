using System;
using System.IO;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Model;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Editor
{
    internal static class DishIconPreviewPrefabBuilder
    {
        private const string RewardPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardDishPanel.prefab";
        private const string ShopFoodPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Shop/ShopFoodBuyItemView.prefab";
        private const string ShopBuyCardPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Shop/ShopBuyCardView.prefab";
        private const string RecipeEditDishPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Recipes/RecipeEditDishView.prefab";
        private const string RewardFormPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardForm.prefab";
        private const string ServingOutletPrefabPath = "Assets/GameMain/Content/Prefabs/UI/Hud/ServingOutlet.prefab";
        private const string DishPiecePrefabPath = "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishPiece.prefab";
        private const string CellPrefabPath = "Assets/GameMain/Content/Prefabs/Battle/Board/DiningTableCell.prefab";
        private const string BadgePrefabPath = "Assets/GameMain/Content/Prefabs/Battle/Dishes/DishValueBadge.prefab";
        [MenuItem("GourmetProject/UI/Rebuild Dish Icon Previews")]
        private static void RebuildDishIconPreviews()
        {
            GameObject cellRoot = AssetDatabase.LoadAssetAtPath<GameObject>(CellPrefabPath);
            GameObject badgeRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BadgePrefabPath);
            SpriteRenderer cellPrefab = cellRoot != null
                ? cellRoot.GetComponent<SpriteRenderer>()
                : null;
            Component badgePrefab =
                FindComponentByTypeName(badgeRoot, "DishValueBadgeView");
            if (cellPrefab == null || badgePrefab == null)
            {
                Debug.LogError(
                    "Dish icon preview builder could not find the DiningTableCell SpriteRenderer or DishValueBadgeView.");
                return;
            }

            PatchRewardPrefab(cellPrefab, badgePrefab);
            PatchShopFoodPrefab(cellPrefab, badgePrefab);
            PatchShopBuyCardPrefab(cellPrefab, badgePrefab);
            PatchRecipeEditDishPrefab(cellPrefab, badgePrefab);
            PatchRewardFormPrefab(cellPrefab, badgePrefab);
            PatchServingOutletPrefab(cellPrefab, badgePrefab);
            PatchDishPiecePrefab(badgePrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("All dish icon RenderTexture previews rebuilt.");
        }

        [MenuItem("GourmetProject/UI/Capture Dish Icon Preview Sample")]
        private static void CaptureDishIconPreviewSample()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopFoodPrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Texture2D readable = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                DishIconRenderTexturePreview preview = instance.GetComponentInChildren<DishIconRenderTexturePreview>(true);
                Sprite sprite = Resources.Load<Sprite>("Sprites/Dishes/donut");
                var dish = new DishDef(
                    "preview_sample_donut",
                    "Preview Sample Donut",
                    100000,
                    DishShape.FromRows(new[] { "X" }),
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty,
                    baseId: "donut");

                preview.gameObject.SetActive(true);
                preview.Bind(dish, sprite, dish.Deliciousness);
                RenderTexture texture = preview.CurrentTexture;
                if (texture == null)
                {
                    throw new InvalidOperationException("Dish icon preview did not create a RenderTexture.");
                }

                RenderTexture.active = texture;
                readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, false);
                readable.Apply(false, false);

                string outputPath = Path.Combine(Path.GetTempPath(), "DishIconPreviewSample.png");
                File.WriteAllBytes(outputPath, ImageConversion.EncodeToPNG(readable));
                Debug.Log($"Dish icon preview sample saved to: {outputPath}");
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null)
                {
                    UnityEngine.Object.DestroyImmediate(readable);
                }

                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void PatchRewardPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(RewardPrefabPath, root =>
            {
                Transform card = root.transform.Find("ChoiceContainer/RewardDishChoiceCardTemplate");
                Transform iconFrame = card?.Find("IconFrame");
                if (card == null || iconFrame == null)
                {
                    Debug.LogError("RewardDishPanel prefab is missing its choice card or IconFrame.");
                    return;
                }

                RectTransform container = EnsureContainer(iconFrame, "DishRenderTexture");
                Stretch(container, 6f);
                DishIconRenderTexturePreview preview = EnsureOutput(container, cellPrefab, badgePrefab);
                SetObjectReference(card.GetComponent<RewardDishChoiceCardView>(), "_dishPreview", preview);
                container.SetAsLastSibling();
            });
        }

        private static void PatchShopFoodPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(ShopFoodPrefabPath, root =>
            {
                RectTransform container = EnsureContainer(root.transform, "DishRenderTexture");
                Transform duplicateContainer =
                    root.transform.Find("Visual/DishRenderTexture");
                if (duplicateContainer != null
                    && duplicateContainer != container.transform)
                {
                    UnityEngine.Object.DestroyImmediate(
                        duplicateContainer.gameObject,
                        true);
                }

                container.anchorMin = container.anchorMax = container.pivot = new Vector2(0.5f, 0.5f);
                container.anchoredPosition = new Vector2(0f, 15f);
                container.sizeDelta = new Vector2(70f, 70f);

                DishIconRenderTexturePreview preview = EnsureOutput(container, cellPrefab, badgePrefab);
                SetObjectReference(root.GetComponent<ShopFoodBuyItemView>(), "_dishIconPreview", preview);
            });
        }

        private static void PatchShopBuyCardPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(ShopBuyCardPrefabPath, root =>
            {
                DishIconRenderTexturePreview preview = ReplaceLegacyPreview(
                    root,
                    cellPrefab,
                    badgePrefab);
                SetObjectReference(root.GetComponent<ShopBuyCardView>(), "_dishIconPreview", preview);
            });
        }

        private static void PatchRecipeEditDishPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(RecipeEditDishPrefabPath, root =>
            {
                DishIconRenderTexturePreview preview = ReplaceLegacyPreview(
                    root,
                    cellPrefab,
                    badgePrefab);
                SetObjectReference(root.GetComponent<RecipeEditDishView>(), "_dishPreview", preview);
            });
        }

        private static void PatchRewardFormPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(RewardFormPrefabPath, root =>
            {
                RewardChoiceRowView row = root.GetComponentInChildren<RewardChoiceRowView>(true);
                Transform iconFrame = row?.transform.Find("IconFrame");
                if (row == null || iconFrame == null)
                {
                    Debug.LogError("RewardForm prefab is missing RewardChoiceRowView or IconFrame.");
                    return;
                }

                RectTransform container = EnsureContainer(iconFrame, "DishRenderTexture");
                Stretch(container, 4f);
                DishIconRenderTexturePreview preview = EnsureOutput(container, cellPrefab, badgePrefab);
                SetObjectReference(row, "_dishPreview", preview);
                container.SetAsLastSibling();
            });
        }

        private static void PatchServingOutletPrefab(
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            PatchPrefab(ServingOutletPrefabPath, root =>
            {
                ServingOutletView outlet = root.GetComponentInChildren<ServingOutletView>(true);
                Transform preparedDish = FindDescendant(root.transform, "PreparedDish");
                if (outlet == null || preparedDish == null)
                {
                    Debug.LogError("ServingOutlet prefab is missing ServingOutletView or PreparedDish.");
                    return;
                }

                ServingOutletDishHoverTrigger hoverTrigger =
                    preparedDish.GetComponent<ServingOutletDishHoverTrigger>()
                    ?? preparedDish.gameObject.AddComponent<ServingOutletDishHoverTrigger>();
                Image legacyImage = preparedDish.GetComponent<Image>();
                if (legacyImage != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacyImage, true);
                }

                RectTransform container = EnsureContainer(preparedDish, "DishRenderTexture");
                Stretch(container, 0f);
                DishIconRenderTexturePreview preview = EnsureOutput(container, cellPrefab, badgePrefab);
                SetObjectReference(outlet, "_preparedDishRoot", preparedDish);
                SetObjectReference(outlet, "_dishPreview", preview);
                SetObjectReference(outlet, "_dishHoverTrigger", hoverTrigger);
                SetObjectReference(outlet, "_worldCanvas", outlet.GetComponent<Canvas>());
                container.SetAsLastSibling();
            });
        }

        private static void PatchDishPiecePrefab(Component badgePrefab)
        {
            PatchPrefab(DishPiecePrefabPath, root =>
            {
                DishPieceView piece = root.GetComponent<DishPieceView>();
                DishPieceValueBadgePresenter presenter =
                    root.GetComponent<DishPieceValueBadgePresenter>()
                    ?? root.AddComponent<DishPieceValueBadgePresenter>();
                if (piece == null || presenter == null || badgePrefab == null)
                {
                    Debug.LogError("DishPiece or DishValueBadge prefab is missing its view component.");
                    return;
                }

                SetObjectReference(piece, "_dishValueBadgePresenter", presenter);
                SetObjectReference(presenter, "_badgePrefab", badgePrefab);
            });
        }

        private static void PatchPrefab(string path, Action<GameObject> patch)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                patch(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static DishIconRenderTexturePreview ReplaceLegacyPreview(
            GameObject root,
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            DishIconRenderTexturePreview existing =
                root.GetComponentInChildren<DishIconRenderTexturePreview>(true);
            RectTransform container = existing != null
                ? existing.transform.parent as RectTransform
                : EnsureContainer(root.transform, "DishRenderTexture");
            if (container == null)
            {
                container = EnsureContainer(root.transform, "DishRenderTexture");
            }

            Stretch(container, 0f);
            DishIconRenderTexturePreview preview = EnsureOutput(container, cellPrefab, badgePrefab);
            return preview;
        }

        private static RectTransform EnsureContainer(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            RectTransform rect = existing as RectTransform;
            if (rect == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.layer = LayerMask.NameToLayer("UI");
                rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
            }

            return rect;
        }

        private static DishIconRenderTexturePreview EnsureOutput(
            RectTransform container,
            SpriteRenderer cellPrefab,
            Component badgePrefab)
        {
            Transform existing = container.Find("Output");
            GameObject output;
            if (existing == null)
            {
                output = new GameObject(
                    "Output",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage),
                    typeof(AspectRatioFitter),
                    typeof(DishIconRenderTexturePreview));
                output.layer = LayerMask.NameToLayer("UI");
                output.transform.SetParent(container, false);
            }
            else
            {
                output = existing.gameObject;
            }

            var rect = (RectTransform)output.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = container.rect.size;
            rect.localScale = Vector3.one;

            RawImage rawImage = output.GetComponent<RawImage>() ?? output.AddComponent<RawImage>();
            rawImage.texture = null;
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            AspectRatioFitter fitter = output.GetComponent<AspectRatioFitter>() ?? output.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;

            DishIconRenderTexturePreview preview = output.GetComponent<DishIconRenderTexturePreview>()
                ?? output.AddComponent<DishIconRenderTexturePreview>();
            var serialized = new SerializedObject(preview);
            serialized.FindProperty("_targetImage").objectReferenceValue = rawImage;
            serialized.FindProperty("_aspectRatioFitter").objectReferenceValue = fitter;
            serialized.FindProperty("_cellPrefab").objectReferenceValue = cellPrefab;
            serialized.FindProperty("_badgePrefab").objectReferenceValue = badgePrefab;
            serialized.FindProperty("_pixelsPerCell").intValue = 96;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return preview;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Component FindComponentByTypeName(GameObject target, string typeName)
        {
            if (target == null)
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name == typeName)
                {
                    return component;
                }
            }

            return null;
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            if (target == null)
            {
                Debug.LogError($"Cannot set {propertyName}: target component is missing.");
                return;
            }

            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Cannot set missing serialized property {propertyName} on {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
