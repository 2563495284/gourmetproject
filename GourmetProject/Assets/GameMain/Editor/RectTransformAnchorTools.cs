using UnityEditor;
using UnityEngine;

namespace GourmetProject.Editor
{
    /// <summary>
    /// Editor-only helpers for converting a RectTransform's current rectangle into anchors.
    /// </summary>
    public static class RectTransformAnchorTools
    {
        private const string ToolsMenuPath = "Tools/GourmetProject/UI/Anchors To Corners";

        [MenuItem("CONTEXT/RectTransform/Anchors To Corners")]
        private static void AnchorsToCornersContext(MenuCommand command)
        {
            Convert((RectTransform)command.context);
        }

        [MenuItem(ToolsMenuPath)]
        private static void AnchorsToCornersMenu()
        {
            Convert(Selection.activeTransform as RectTransform);
        }

        [MenuItem(ToolsMenuPath, true)]
        private static bool ValidateAnchorsToCornersMenu()
        {
            RectTransform rect = Selection.activeTransform as RectTransform;
            return rect != null && rect.parent is RectTransform;
        }

        private static void Convert(RectTransform rect)
        {
            if (rect == null || rect.parent is not RectTransform parent)
            {
                Debug.LogWarning("Anchors To Corners requires a RectTransform with a RectTransform parent.");
                return;
            }

            Rect parentRect = parent.rect;
            if (Mathf.Approximately(parentRect.width, 0f) || Mathf.Approximately(parentRect.height, 0f))
            {
                Debug.LogWarning("Anchors To Corners cannot run because the parent RectTransform has zero size.");
                return;
            }

            Undo.RecordObject(rect, "Anchors To Corners");

            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;

            anchorMin.x += rect.offsetMin.x / parentRect.width;
            anchorMin.y += rect.offsetMin.y / parentRect.height;
            anchorMax.x += rect.offsetMax.x / parentRect.width;
            anchorMax.y += rect.offsetMax.y / parentRect.height;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            EditorUtility.SetDirty(rect);
            Debug.Log($"Converted anchors for {rect.name}: min={rect.anchorMin}, max={rect.anchorMax}");
        }
    }
}
