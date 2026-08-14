using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>一套餐桌格视觉资源：桌体与其上方盘子必须成对出现。</summary>
    public readonly struct DiningTableCellSprites
    {
        public DiningTableCellSprites(Sprite table, Sprite plate)
        {
            Table = table;
            Plate = plate;
        }

        public Sprite Table { get; }

        public Sprite Plate { get; }

        public bool IsValid => Table != null && Plate != null;
    }

    /// <summary>集中维护餐桌/盘子的 Resources 命名与整对回退规则。</summary>
    internal static class DiningTableCellSpriteResources
    {
        private const string SpriteRoot = "Sprites/UI/";
        private static readonly HashSet<string> ReportedMissingPairs = new();

        public static DiningTableCellSprites LoadDefault()
        {
            DiningTableCellSprites sprites = LoadPair(null);
            if (!sprites.IsValid)
            {
                ReportMissingPair("default", sprites, fallback: false);
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

            DiningTableCellSprites sprites = LoadPair(materialId);
            if (sprites.IsValid)
            {
                return sprites;
            }

            ReportMissingPair(materialId, sprites, fallback: true);
            return fallback;
        }

        private static DiningTableCellSprites LoadPair(string materialId)
        {
            string suffix = string.IsNullOrEmpty(materialId) ? string.Empty : $"_{materialId}";
            return new DiningTableCellSprites(
                LoadSprite($"board_cell{suffix}"),
                LoadSprite($"board_cell_plate{suffix}"));
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

        private static void ReportMissingPair(
            string key,
            DiningTableCellSprites sprites,
            bool fallback)
        {
            if (!ReportedMissingPairs.Add(key))
            {
                return;
            }

            string action = fallback ? "，已整对回退普通材质" : string.Empty;
            Debug.LogError(
                $"餐桌 Sprite 对不完整（{key}）：" +
                $"table={(sprites.Table != null ? "ok" : "missing")}, " +
                $"plate={(sprites.Plate != null ? "ok" : "missing")}{action}。");
        }
    }
}
