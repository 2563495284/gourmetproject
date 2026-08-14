using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>餐桌格的单层平面 Sprite。</summary>
    public readonly struct DiningTableCellSprites
    {
        public DiningTableCellSprites(Sprite plate)
        {
            Plate = plate;
        }

        public Sprite Plate { get; }

        public bool IsValid => Plate != null;
    }

    /// <summary>集中维护餐桌格 Resources 命名与回退规则。</summary>
    internal static class DiningTableCellSpriteResources
    {
        private const string SpriteRoot = "Sprites/UI/";
        private static readonly HashSet<string> ReportedMissing = new();

        public static DiningTableCellSprites LoadDefault()
        {
            DiningTableCellSprites sprites = Load(null);
            if (!sprites.IsValid)
            {
                ReportMissing("default", fallback: false);
            }

            return sprites;
        }

        public static DiningTableCellSprites LoadMaterial(
            string materialId,
            DiningTableCellSprites fallback)
        {
            if (string.IsNullOrEmpty(materialId))
            {
                return fallback;
            }

            DiningTableCellSprites sprites = Load(materialId);
            if (sprites.IsValid)
            {
                return sprites;
            }

            ReportMissing(materialId, fallback: true);
            return fallback;
        }

        private static DiningTableCellSprites Load(string materialId)
        {
            string suffix = string.IsNullOrEmpty(materialId) ? string.Empty : $"_{materialId}";
            return new DiningTableCellSprites(LoadSprite($"board_cell_plate{suffix}"));
        }

        private static Sprite LoadSprite(string name)
        {
            string path = SpriteRoot + name;
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }

        private static void ReportMissing(
            string key,
            bool fallback)
        {
            if (!ReportedMissing.Add(key))
            {
                return;
            }

            string action = fallback ? "，已回退普通材质" : string.Empty;
            Debug.LogError(
                $"餐桌格 Sprite 缺失（{key}）{action}。");
        }
    }
}
