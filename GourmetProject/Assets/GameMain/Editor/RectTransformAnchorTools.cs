using UnityEditor;
using UnityEngine;

namespace GourmetProject.Editor
{
    /// <summary>
    /// Editor-only helpers for converting between stretched anchors and fixed width/height.
    /// </summary>
    public static class RectTransformAnchorTools
    {
        private const string AnchorsToCornersMenuPath = "Tools/GourmetProject/UI/Anchors To Corners";
        private const string CornersToSizeMenuPath = "Tools/GourmetProject/UI/Corners To Size";

        [MenuItem("CONTEXT/RectTransform/Anchors To Corners")]
        private static void AnchorsToCornersContext(MenuCommand command)
        {
            ConvertAnchorsToCorners((RectTransform)command.context);
        }

        [MenuItem(AnchorsToCornersMenuPath)]
        private static void AnchorsToCornersMenu()
        {
            ConvertAnchorsToCorners(Selection.activeTransform as RectTransform);
        }

        [MenuItem(AnchorsToCornersMenuPath, true)]
        private static bool ValidateAnchorsToCornersMenu()
        {
            RectTransform rect = Selection.activeTransform as RectTransform;
            return rect != null && rect.parent is RectTransform;
        }

        [MenuItem("CONTEXT/RectTransform/Corners To Size")]
        private static void CornersToSizeContext(MenuCommand command)
        {
            ConvertCornersToSize((RectTransform)command.context);
        }

        [MenuItem(CornersToSizeMenuPath)]
        private static void CornersToSizeMenu()
        {
            ConvertCornersToSize(Selection.activeTransform as RectTransform);
        }

        [MenuItem(CornersToSizeMenuPath, true)]
        private static bool ValidateCornersToSizeMenu()
        {
            RectTransform rect = Selection.activeTransform as RectTransform;
            return rect != null && rect.parent is RectTransform;
        }

        private static void ConvertAnchorsToCorners(RectTransform rect)
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
            Debug.Log($"Anchors To Corners on {rect.name}: min={rect.anchorMin}, max={rect.anchorMax}");
        }

        private static void ConvertCornersToSize(RectTransform rect)
        {
            if (rect == null || rect.parent is not RectTransform parent)
            {
                Debug.LogWarning("Corners To Size requires a RectTransform with a RectTransform parent.");
                return;
            }

            Rect parentRect = parent.rect;
            if (Mathf.Approximately(parentRect.width, 0f) || Mathf.Approximately(parentRect.height, 0f))
            {
                Debug.LogWarning("Corners To Size cannot run because the parent RectTransform has zero size.");
                return;
            }

            // Must use SetSizeWithCurrentAnchors per axis: assigning anchorMin/anchorMax
            // directly only collapses one axis because Unity preserves the visual rect via offsets.
            Vector2 size = rect.rect.size;

            Undo.RecordObject(rect, "Corners To Size");

            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            EditorUtility.SetDirty(rect);
            Debug.Log(
                $"Corners To Size on {rect.name}: size=({size.x:F1}, {size.y:F1}), " +
                $"anchor=({rect.anchorMin.x:F3}, {rect.anchorMin.y:F3})");
        }
    }
}
