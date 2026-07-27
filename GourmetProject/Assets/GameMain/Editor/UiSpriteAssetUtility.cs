#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Editor;

namespace GourmetProject.Game.Editor
{
    public static class UiSpriteAssetUtility
    {
        private const string UiSpriteRoot = "Assets/GameMain/Content/Resources/Sprites/UI/";

        public static Sprite Load(string spriteName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{UiSpriteRoot}{spriteName}.png");
        }

        public static void Apply(Image image, string spriteName, Image.Type type = Image.Type.Sliced)
        {
            if (image == null)
            {
                return;
            }

            Sprite sprite = Load(spriteName);
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = type;
            image.color = Color.white;
            image.preserveAspect = type == Image.Type.Simple;
        }

        public static void ConfigureGeneratedUiBorders()
        {
            SetBorder("ui_hud_panel_side", 110f);
            SetBorder("ui_hud_panel_top_axis", 92f);
            SetBorder("ui_hud_panel_center", 96f);
            SetBorder("ui_hud_panel_drawer", 92f);
            SetBorder("ui_result_panel", 104f);
            SetBorder("ui_shop_section_panel", 90f);
            SetBorder("ui_shop_recipe_strip", 86f);
            SetBorder("ui_recipe_book_chip", 44f);
            SetBorder("ui_recipe_dish_tile", 38f);
            SetBorder("ui_table_backdrop", 0f);
            SetBorder("ui_btn_primary_compact", 18f);
            SetBorder("ui_btn_secondary_compact", 18f);
        }

        private static void SetBorder(string spriteName, float border)
        {
            string path = $"{UiSpriteRoot}{spriteName}.png";
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                return;
            }

            Vector4 next = new Vector4(border, border, border, border);
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteBorder == next)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsPerUnit = SpriteImportPolicy.ProjectSpritePixelsPerUnit;
            importer.spriteBorder = next;
            importer.SaveAndReimport();
        }
    }
}
#endif
