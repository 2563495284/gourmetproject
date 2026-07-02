#if UNITY_EDITOR
using GourmetProject.Game.UI.Meta;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Editor
{
    public static class ShopFormPrefabBuilder
    {
        private const string ShopFormPath = "Assets/GameMain/UI/ShopForm.prefab";
        private const string BuyCardPath = "Assets/GameMain/UI/ShopBuyCardView.prefab";
        private const string RecipeBookPath = "Assets/GameMain/UI/ShopRecipeBookView.prefab";
        private const string EditBookPath = "Assets/GameMain/UI/RecipeEditBookView.prefab";
        private const string EditDishPath = "Assets/GameMain/UI/RecipeEditDishView.prefab";

        private static readonly Color Ink = new Color(0.05f, 0.035f, 0.025f, 1f);
        private static readonly Color Cream = new Color(1f, 0.86f, 0.48f, 1f);
        private static readonly Color Orange = new Color(0.95f, 0.42f, 0.13f, 1f);
        private static readonly Color Pale = new Color(1f, 0.94f, 0.74f, 1f);
        private static readonly Color Green = new Color(0.40f, 0.72f, 0.34f, 1f);

        [MenuItem("GourmetProject/UI/Rebuild Shop Form Prefab")]
        public static void RebuildShopFormPrefab()
        {
            ShopRecipeBookView recipeBookPrefab = EnsureRecipeBookPrefab();
            RecipeEditBookView editBookPrefab = EnsureEditBookPrefab();
            RecipeEditDishView editDishPrefab = EnsureEditDishPrefab();
            ShopBuyCardView buyCardPrefab = AssetDatabase.LoadAssetAtPath<ShopBuyCardView>(BuyCardPath);

            GameObject root = PrefabUtility.LoadPrefabContents(ShopFormPath);
            try
            {
                ClearChildren(root.transform);

                RectTransform rootRect = EnsureRect(root);
                Stretch(rootRect);

                Image dim = CreateImage(root.transform, "Dim", new Color(0f, 0f, 0f, 0.45f));
                Stretch((RectTransform)dim.transform);

                RectTransform content = CreatePanel(root.transform, "Content", Ink, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
                CreatePanel(content, "Paper", Pale, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));

                GameObject shopPanel = CreatePanel(content, "ShopPanel", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one).gameObject;
                GameObject editPanel = CreatePanel(content, "RecipeEditPanel", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one).gameObject;
                editPanel.SetActive(false);

                Text goldText = CreateText(shopPanel.transform, "Gold", "金币 0", 32, TextAnchor.MiddleLeft, Ink);
                Place((RectTransform)goldText.transform, new Vector2(0.04f, 0.88f), new Vector2(0.26f, 0.97f));

                Text title = CreateText(shopPanel.transform, "Title", "商店", 46, TextAnchor.MiddleCenter, Ink);
                Place((RectTransform)title.transform, new Vector2(0.35f, 0.88f), new Vector2(0.65f, 0.98f));

                Button leaveButton = CreateButton(shopPanel.transform, "Leave", "离开商店", Orange);
                Place((RectTransform)leaveButton.transform, new Vector2(0.78f, 0.88f), new Vector2(0.96f, 0.97f));

                BuildShopSection(shopPanel.transform, "FoodSection", "食物购买", new Vector2(0.04f, 0.48f), new Vector2(0.48f, 0.84f), out RectTransform foodContainer, out Text foodEmpty);
                BuildShopSection(shopPanel.transform, "FragmentSection", "棋盘碎片包", new Vector2(0.52f, 0.48f), new Vector2(0.96f, 0.84f), out RectTransform fragmentContainer, out Text fragmentEmpty);
                BuildShopSection(shopPanel.transform, "PassiveSection", "被动道具购买", new Vector2(0.04f, 0.18f), new Vector2(0.48f, 0.44f), out RectTransform passiveContainer, out Text passiveEmpty);
                BuildShopSection(shopPanel.transform, "ActiveSection", "主动道具购买", new Vector2(0.52f, 0.18f), new Vector2(0.96f, 0.44f), out RectTransform activeContainer, out Text activeEmpty);

                RectTransform recipeStrip = CreatePanel(shopPanel.transform, "RecipeStrip", Ink, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.15f));
                CreatePanel(recipeStrip, "Inner", Cream, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
                Button editButton = CreateButton(recipeStrip, "EditRecipe", "编辑菜谱", Green);
                Place((RectTransform)editButton.transform, new Vector2(0.02f, 0.18f), new Vector2(0.16f, 0.82f));
                RectTransform recipeContainer = CreateLayoutPanel(recipeStrip, "RecipeContainer", new Vector2(0.18f, 0.14f), new Vector2(0.78f, 0.86f), horizontal: true);
                Button buyRecipeButton = CreateButton(recipeStrip, "BuyRecipeBook", "+ 20", Orange);
                Place((RectTransform)buyRecipeButton.transform, new Vector2(0.80f, 0.18f), new Vector2(0.90f, 0.82f));
                Text recipeLimitText = CreateText(recipeStrip, "RecipeLimit", "2/4", 24, TextAnchor.MiddleCenter, Ink);
                Place((RectTransform)recipeLimitText.transform, new Vector2(0.91f, 0.18f), new Vector2(0.98f, 0.82f));

                BuildRecipeEditor(editPanel.transform, out RectTransform editBooksContainer, out RecipeTrashDropZone trashZone, out Text trashPriceText, out Button exitEditButton);

                ShopForm form = root.GetComponent<ShopForm>();
                if (form == null)
                {
                    form = root.AddComponent<ShopForm>();
                }

                SerializedObject so = new SerializedObject(form);
                SetObject(so, "_shopPanel", shopPanel);
                SetObject(so, "_recipeEditPanel", editPanel);
                SetObject(so, "_goldText", goldText);
                SetObject(so, "_leaveButton", leaveButton);
                SetObject(so, "_foodContainer", foodContainer);
                SetObject(so, "_fragmentContainer", fragmentContainer);
                SetObject(so, "_passiveContainer", passiveContainer);
                SetObject(so, "_activeContainer", activeContainer);
                SetObject(so, "_foodEmptyText", foodEmpty);
                SetObject(so, "_fragmentEmptyText", fragmentEmpty);
                SetObject(so, "_passiveEmptyText", passiveEmpty);
                SetObject(so, "_activeEmptyText", activeEmpty);
                SetObject(so, "_buyCardPrefab", buyCardPrefab);
                SetObject(so, "_recipeStripContainer", recipeContainer);
                SetObject(so, "_recipeBookPrefab", recipeBookPrefab);
                SetObject(so, "_buyRecipeBookButton", buyRecipeButton);
                SetObject(so, "_editRecipeButton", editButton);
                SetObject(so, "_recipeLimitText", recipeLimitText);
                SetObject(so, "_editBooksContainer", editBooksContainer);
                SetObject(so, "_editBookPrefab", editBookPrefab);
                SetObject(so, "_editDishPrefab", editDishPrefab);
                SetObject(so, "_trashZone", trashZone);
                SetObject(so, "_trashPriceText", trashPriceText);
                SetObject(so, "_exitEditButton", exitEditButton);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, ShopFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void BuildShopSection(Transform parent, string name, string title, Vector2 min, Vector2 max, out RectTransform container, out Text emptyText)
        {
            RectTransform panel = CreatePanel(parent, name, Ink, min, max);
            CreatePanel(panel, "Inner", Cream, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            Text titleText = CreateText(panel, "Title", title, 26, TextAnchor.MiddleLeft, Ink);
            Place((RectTransform)titleText.transform, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.96f));
            container = CreatePanel(panel, "Container", new Color(0f, 0f, 0f, 0f), new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.76f));
            GridLayoutGroup grid = container.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140f, 92f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = name == "FragmentSection" ? 1 : 2;
            emptyText = CreateText(panel, "Empty", "暂无", 22, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)emptyText.transform, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.70f));
        }

        private static void BuildRecipeEditor(Transform parent, out RectTransform booksContainer, out RecipeTrashDropZone trashZone, out Text trashPriceText, out Button exitButton)
        {
            Text title = CreateText(parent, "EditTitle", "删除食物至垃圾桶", 40, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)title.transform, new Vector2(0.18f, 0.88f), new Vector2(0.82f, 0.97f));
            Text hint = CreateText(parent, "EditHint", "拖拽食物可移动至其他菜谱", 24, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)hint.transform, new Vector2(0.18f, 0.82f), new Vector2(0.82f, 0.88f));

            booksContainer = CreateLayoutPanel(parent, "EditBooksContainer", new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.80f), horizontal: true);
            HorizontalLayoutGroup group = booksContainer.GetComponent<HorizontalLayoutGroup>();
            group.spacing = 14f;
            group.padding = new RectOffset(8, 8, 8, 8);

            RectTransform trash = CreatePanel(parent, "Trash", Ink, new Vector2(0.20f, 0.04f), new Vector2(0.48f, 0.15f));
            CreatePanel(trash, "Inner", Orange, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            CreateText(trash, "Label", "垃圾桶", 28, TextAnchor.MiddleCenter, Color.white);
            trashPriceText = CreateText(trash, "Price", "-15", 22, TextAnchor.MiddleRight, Color.white);
            Place((RectTransform)trashPriceText.transform, new Vector2(0.68f, 0.10f), new Vector2(0.95f, 0.90f));
            trashZone = trash.gameObject.AddComponent<RecipeTrashDropZone>();

            exitButton = CreateButton(parent, "ExitEdit", "离开编辑", Green);
            Place((RectTransform)exitButton.transform, new Vector2(0.56f, 0.04f), new Vector2(0.80f, 0.15f));
        }

        private static ShopRecipeBookView EnsureRecipeBookPrefab()
        {
            GameObject root = new GameObject("ShopRecipeBookView", typeof(RectTransform), typeof(Image), typeof(Button), typeof(ShopRecipeBookView));
            RectTransform rect = EnsureRect(root);
            rect.sizeDelta = new Vector2(150f, 64f);
            root.GetComponent<Image>().color = Ink;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            CreatePanel(root.transform, "Inner", Pale, Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f));
            Text title = CreateText(root.transform, "Title", "菜谱1", 22, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)title.transform, new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.90f));
            Text capacity = CreateText(root.transform, "Capacity", "0/12", 18, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)capacity.transform, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.48f));
            Assign(root.GetComponent<ShopRecipeBookView>(), ("_titleText", title), ("_capacityText", capacity), ("_button", button));
            return SavePrefab<ShopRecipeBookView>(root, RecipeBookPath);
        }

        private static RecipeEditBookView EnsureEditBookPrefab()
        {
            GameObject root = new GameObject("RecipeEditBookView", typeof(RectTransform), typeof(Image), typeof(RecipeEditBookView));
            RectTransform rect = EnsureRect(root);
            rect.sizeDelta = new Vector2(230f, 380f);
            root.GetComponent<Image>().color = Ink;
            CreatePanel(root.transform, "Inner", Cream, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            Text title = CreateText(root.transform, "Title", "菜谱1", 26, TextAnchor.MiddleLeft, Ink);
            Place((RectTransform)title.transform, new Vector2(0.08f, 0.88f), new Vector2(0.62f, 0.98f));
            Text capacity = CreateText(root.transform, "Capacity", "0/12", 22, TextAnchor.MiddleRight, Ink);
            Place((RectTransform)capacity.transform, new Vector2(0.62f, 0.88f), new Vector2(0.92f, 0.98f));
            RectTransform dishContainer = CreatePanel(root.transform, "DishContainer", new Color(0f, 0f, 0f, 0f), new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.84f));
            GridLayoutGroup grid = dishContainer.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(88f, 56f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            Assign(root.GetComponent<RecipeEditBookView>(), ("_titleText", title), ("_capacityText", capacity), ("_dishContainer", dishContainer));
            return SavePrefab<RecipeEditBookView>(root, EditBookPath);
        }

        private static RecipeEditDishView EnsureEditDishPrefab()
        {
            GameObject root = new GameObject("RecipeEditDishView", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(RecipeEditDishView));
            RectTransform rect = EnsureRect(root);
            rect.sizeDelta = new Vector2(88f, 56f);
            root.GetComponent<Image>().color = Pale;
            Text name = CreateText(root.transform, "Name", "菜品", 18, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)name.transform, new Vector2(0.04f, 0.36f), new Vector2(0.96f, 0.92f));
            Text shape = CreateText(root.transform, "Shape", "1x1", 14, TextAnchor.MiddleCenter, Ink);
            Place((RectTransform)shape.transform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.36f));
            Assign(root.GetComponent<RecipeEditDishView>(), ("_nameText", name), ("_shapeText", shape));
            return SavePrefab<RecipeEditDishView>(root, EditDishPath);
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            return CreatePanel(parent, name, color, min, max, Vector2.zero, Vector2.zero);
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image image = CreateImage(parent, name, color);
            RectTransform rect = (RectTransform)image.transform;
            Place(rect, min, max, offsetMin, offsetMax);
            return rect;
        }

        private static RectTransform CreateLayoutPanel(Transform parent, string name, Vector2 min, Vector2 max, bool horizontal)
        {
            RectTransform panel = CreatePanel(parent, name, new Color(0f, 0f, 0f, 0f), min, max);
            if (horizontal)
            {
                HorizontalLayoutGroup layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;
                layout.spacing = 8f;
            }
            else
            {
                VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.spacing = 8f;
            }

            return panel;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string text, int size, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            Stretch((RectTransform)go.transform);
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color color)
        {
            Image image = CreateImage(parent, name, Ink);
            RectTransform inner = CreatePanel(image.transform, "Inner", color, Vector2.zero, Vector2.one, new Vector2(5f, 5f), new Vector2(-5f, -5f));
            Text text = CreateText(inner, "Label", label, 22, TextAnchor.MiddleCenter, Color.white);
            Stretch((RectTransform)text.transform);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static RectTransform EnsureRect(GameObject go)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            return rect != null ? rect : go.AddComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            Place(rect, Vector2.zero, Vector2.one);
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            Place(rect, min, max, Vector2.zero, Vector2.zero);
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        private static void Assign(Object target, params (string field, Object value)[] values)
        {
            SerializedObject so = new SerializedObject(target);
            foreach ((string field, Object value) in values)
            {
                SetObject(so, field, value);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static T SavePrefab<T>(GameObject root, string path) where T : Component
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<T>();
        }
    }
}
#endif
