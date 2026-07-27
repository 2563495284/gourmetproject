using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Editor
{
    /// <summary>
    /// Keeps generated sprite imports deterministic. Visual size should live in
    /// RectTransforms, SpriteRenderers, or generated pixel dimensions, not per-asset PPU hacks.
    /// </summary>
    public sealed class SpriteImportPolicy : AssetPostprocessor
    {
        public const float ProjectSpritePixelsPerUnit = 100f;

        private const string SpriteRoot = "Assets/GameMain/Content/Resources/Sprites/";
        private const string SpriteRootFolder = "Assets/GameMain/Content/Resources/Sprites";
        private const string ReportMenuPath = "Tools/GourmetProject/Assets/Report Non-100 Sprite PPU";
        private const string ReimportMenuPath = "Tools/GourmetProject/Assets/Reimport Sprites With Import Policy";

        private void OnPreprocessTexture()
        {
            if (!IsProjectSprite(assetPath) || assetImporter is not TextureImporter importer)
            {
                return;
            }

            PreserveSpriteBorderWorldSize(importer);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = ProjectSpritePixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
        }

        [MenuItem(ReportMenuPath)]
        private static void ReportNonStandardSpritePpu()
        {
            List<string> offenders = FindNonStandardSpritePpu();
            if (offenders.Count == 0)
            {
                Debug.Log($"All sprites under {SpriteRootFolder} use PPU={ProjectSpritePixelsPerUnit}.");
                return;
            }

            Debug.LogWarning(
                $"{offenders.Count} sprite(s) under {SpriteRootFolder} do not use PPU={ProjectSpritePixelsPerUnit}:\n" +
                string.Join("\n", offenders));
        }

        [MenuItem(ReimportMenuPath)]
        private static void ReimportSpritesWithPolicy()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reimport sprite assets",
                    $"Reimport all Texture2D assets under {SpriteRootFolder} and apply PPU={ProjectSpritePixelsPerUnit}?",
                    "Reimport",
                    "Cancel"))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteRootFolder });
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsProjectSprite(path))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            ReportNonStandardSpritePpu();
        }

        private static List<string> FindNonStandardSpritePpu()
        {
            var offenders = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpriteRootFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsProjectSprite(path))
                {
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                    !Mathf.Approximately(importer.spritePixelsPerUnit, ProjectSpritePixelsPerUnit))
                {
                    offenders.Add($"{path}  ppu={importer.spritePixelsPerUnit}");
                }
            }

            return offenders;
        }

        private static bool IsProjectSprite(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(SpriteRoot, System.StringComparison.Ordinal) &&
                   path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
        }

        private static void PreserveSpriteBorderWorldSize(TextureImporter importer)
        {
            float previousPpu = importer.spritePixelsPerUnit;
            if (previousPpu <= 0f || Mathf.Approximately(previousPpu, ProjectSpritePixelsPerUnit))
            {
                return;
            }

            Vector4 border = importer.spriteBorder;
            if (!HasBorder(border))
            {
                return;
            }

            importer.spriteBorder = ScaleBorder(border, ProjectSpritePixelsPerUnit / previousPpu);
        }

        private static bool HasBorder(Vector4 border)
        {
            return border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f;
        }

        private static Vector4 ScaleBorder(Vector4 border, float scale)
        {
            return new Vector4(
                RoundBorder(border.x * scale),
                RoundBorder(border.y * scale),
                RoundBorder(border.z * scale),
                RoundBorder(border.w * scale));
        }

        private static float RoundBorder(float value)
        {
            return Mathf.Round(value * 1000f) / 1000f;
        }
    }
}
