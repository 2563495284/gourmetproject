#if UNITY_EDITOR
using GourmetProject.Game.UI.Hud;
using GourmetProject.Game.UI.Meta;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Editor
{
    public static class InterfacePrefabBuilder
    {
        private const string WeekCardPath = "Assets/GameMain/UI/WeekEventCardView.prefab";
        private const string AxisCellPath = "Assets/GameMain/UI/Hud/ActionAxisCellView.prefab";
        private const string BattleFormPath = "Assets/GameMain/UI/BattleForm.prefab";

        private static readonly Color Ink = new Color(0.05f, 0.035f, 0.025f, 1f);
        private static readonly Color Cream = new Color(1f, 0.88f, 0.52f, 1f);
        private static readonly Color Alert = new Color(0.92f, 0.16f, 0.08f, 1f);

        [MenuItem("GourmetProject/UI/Polish Interface Prefabs")]
        public static void PolishInterfacePrefabs()
        {
            PolishWeekCardPrefab();
            PolishAxisCellPrefab();
            PolishBattleFormAxis();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void PolishWeekCardPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WeekCardPath);
            try
            {
                WeekEventCardView view = root.GetComponent<WeekEventCardView>();
                Image art = FindOrCreateImage(root.transform, "Art", new Color(1f, 1f, 1f, 0f));
                Place((RectTransform)art.transform, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.72f));
                art.preserveAspect = true;

                Image badge = FindOrCreateImage(root.transform, "RewardBadge", Alert);
                Place((RectTransform)badge.transform, new Vector2(0.74f, 0.20f), new Vector2(0.90f, 0.34f));
                Text badgeText = FindOrCreateText(badge.transform, "Label", "!", 32, TextAnchor.MiddleCenter, Color.white);
                Stretch((RectTransform)badgeText.transform);
                badge.gameObject.SetActive(false);

                SerializedObject so = new SerializedObject(view);
                SetObject(so, "_artImage", art);
                SetObject(so, "_rewardBadgeImage", badge);
                SetObject(so, "_rewardBadgeText", badgeText);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, WeekCardPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PolishAxisCellPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AxisCellPath);
            try
            {
                ActionAxisCellView view = root.GetComponent<ActionAxisCellView>();
                Image nodeImage = FindOrCreateImage(root.transform, "NodeImage", new Color(1f, 1f, 1f, 0f));
                Place((RectTransform)nodeImage.transform, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f));
                nodeImage.preserveAspect = true;
                nodeImage.gameObject.SetActive(false);

                SerializedObject so = new SerializedObject(view);
                SetObject(so, "_nodeImage", nodeImage);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, AxisCellPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PolishBattleFormAxis()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BattleFormPath);
            try
            {
                ActionAxisBar axis = root.GetComponentInChildren<ActionAxisBar>(true);
                if (axis == null)
                {
                    return;
                }

                RectTransform marker = SerializedObjectRef<RectTransform>(axis, "_positionMarker");
                Text remaining = null;
                if (marker != null)
                {
                    remaining = FindOrCreateText(marker, "RemainingDays", "0天", 20, TextAnchor.MiddleCenter, Ink);
                    RectTransform rect = (RectTransform)remaining.transform;
                    rect.anchorMin = new Vector2(-2.0f, -1.75f);
                    rect.anchorMax = new Vector2(2.0f, -0.65f);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                }

                SerializedObject so = new SerializedObject(axis);
                SetObject(so, "_remainingDaysText", remaining);
                SetObject(so, "_shopNodeSprite", LoadSprite("Assets/GameMain/Resources/Sprites/UI/icon_axis_shop.png"));
                SetObject(so, "_interestNodeSprite", LoadSprite("Assets/GameMain/Resources/Sprites/UI/icon_axis_interest.png"));
                SetObject(so, "_bossNodeSprite", LoadSprite("Assets/GameMain/Resources/Sprites/UI/icon_axis_boss.png"));
                SetObject(so, "_eventNodeSprite", LoadSprite("Assets/GameMain/Resources/Sprites/UI/icon_axis_event.png"));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BattleFormPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Image FindOrCreateImage(Transform parent, string name, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing == null ? new GameObject(name, typeof(RectTransform), typeof(Image)) : existing.gameObject;
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
            }

            Image image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text FindOrCreateText(Transform parent, string name, string value, int size, TextAnchor anchor, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing == null ? new GameObject(name, typeof(RectTransform), typeof(Text)) : existing.gameObject;
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
            }

            Text text = go.GetComponent<Text>() ?? go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            Place(rect, Vector2.zero, Vector2.one);
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
            }
        }

        private static T SerializedObjectRef<T>(Object target, string field) where T : Object
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            return prop == null ? null : prop.objectReferenceValue as T;
        }

        private static Sprite LoadSprite(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
#endif
