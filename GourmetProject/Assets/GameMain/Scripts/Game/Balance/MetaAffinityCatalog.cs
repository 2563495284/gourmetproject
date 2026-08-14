using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    [CreateAssetMenu(menuName = "Gourmet/Balance/Meta Affinity Catalog", fileName = "MetaAffinityCatalog")]
    public sealed class MetaAffinityCatalog : ScriptableObject
    {
        public const string DefaultProfileId = "default-meta-affinity-v1";

        public string ProfileId = DefaultProfileId;
        public List<MetaAffinityEntry> Entries = new List<MetaAffinityEntry>();

        public float Get(string itemId, MetaRoute route)
        {
            MetaAffinityEntry entry = Entries?.Find(v => v != null && v.ItemId == itemId);
            if (entry == null) return route == MetaRoute.Normal ? 1f : 0f;
            switch (route)
            {
                case MetaRoute.Event: return entry.Event;
                case MetaRoute.Interest: return entry.Interest;
                case MetaRoute.Shop: return entry.Shop;
                default: return entry.Normal;
            }
        }

        /// <summary>
        /// 校验路线档案是否与当前配置一致。正式 Balance Lab 会要求全部当前道具都有显式条目，
        /// 这样配置新增道具时不会悄悄退回 Normal 路线。
        /// </summary>
        public List<string> Validate(cfg.Tables tables, bool requireAllConfigured)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(ProfileId)) errors.Add("路线档案缺少 ProfileId。");
            if (tables == null)
            {
                errors.Add("路线档案无法校验：配置表为空。");
                return errors;
            }

            var configuredIds = new HashSet<string>(StringComparer.Ordinal);
            var availableIds = new HashSet<string>(
                tables.TbPassiveItem.DataList.Select(item => item.Id)
                    .Concat(tables.TbActiveItem.DataList.Select(item => item.Id))
                    .Where(id => !string.IsNullOrEmpty(id)),
                StringComparer.Ordinal);
            bool hasEvent = false;
            bool hasInterest = false;
            bool hasShop = false;
            foreach (MetaAffinityEntry entry in Entries ?? new List<MetaAffinityEntry>())
            {
                if (entry == null)
                {
                    errors.Add("路线档案包含空条目。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    errors.Add("路线档案包含空 ItemId。");
                    continue;
                }

                if (!configuredIds.Add(entry.ItemId)) errors.Add($"路线档案重复配置：{entry.ItemId}");
                if (!availableIds.Contains(entry.ItemId)) errors.Add($"路线档案引用了不存在的道具：{entry.ItemId}");
                if (!ValidWeight(entry.Normal) || !ValidWeight(entry.Event)
                    || !ValidWeight(entry.Interest) || !ValidWeight(entry.Shop))
                    errors.Add($"路线档案权重必须是有限非负数：{entry.ItemId}");
                if (entry.Normal <= 0f && entry.Event <= 0f && entry.Interest <= 0f && entry.Shop <= 0f)
                    errors.Add($"路线档案至少需要一个正权重：{entry.ItemId}");
                hasEvent |= entry.Event > entry.Normal;
                hasInterest |= entry.Interest > entry.Normal;
                hasShop |= entry.Shop > entry.Normal;
            }

            if (requireAllConfigured)
            {
                foreach (string missing in availableIds.Except(configuredIds).OrderBy(id => id, StringComparer.Ordinal))
                    errors.Add($"路线档案缺少当前道具：{missing}");
            }

            if (!hasEvent) errors.Add("路线档案没有任何偏向事件路线的道具。");
            if (!hasInterest) errors.Add("路线档案没有任何偏向利息路线的道具。");
            if (!hasShop) errors.Add("路线档案没有任何偏向商店路线的道具。");
            return errors.Distinct().ToList();
        }

        public void RebuildRecommended(cfg.Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            ProfileId = DefaultProfileId;
            Entries = tables.TbPassiveItem.DataList.Select(item => item.Id)
                .Concat(tables.TbActiveItem.DataList.Select(item => item.Id))
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(BuildRecommendedEntry)
                .ToList();
        }

        public string ComputeContentHash()
        {
            var canonical = new StringBuilder(ProfileId ?? string.Empty).Append('\n');
            foreach (MetaAffinityEntry entry in (Entries ?? new List<MetaAffinityEntry>())
                         .Where(entry => entry != null)
                         .OrderBy(entry => entry.ItemId, StringComparer.Ordinal))
            {
                canonical.Append(entry.ItemId ?? string.Empty).Append('|')
                    .Append(entry.Normal.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.Event.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.Interest.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.Shop.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static MetaAffinityEntry BuildRecommendedEntry(string itemId)
        {
            MetaRoute route = RecommendedRoute(itemId);
            return new MetaAffinityEntry
            {
                ItemId = itemId,
                Normal = route == MetaRoute.Normal ? 1f : 0.25f,
                Event = route == MetaRoute.Event ? 100f : 0f,
                Interest = route == MetaRoute.Interest ? 100f : 0f,
                Shop = route == MetaRoute.Shop ? 100f : 0f,
            };
        }

        private static MetaRoute RecommendedRoute(string itemId)
        {
            string id = itemId ?? string.Empty;
            if (id.IndexOf("interest", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("loan", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("debt", StringComparison.OrdinalIgnoreCase) >= 0)
                return MetaRoute.Interest;
            if (id.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("discount", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("restock", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0)
                return MetaRoute.Shop;
            if (id.IndexOf("event", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lottery", StringComparison.OrdinalIgnoreCase) >= 0)
                return MetaRoute.Event;
            return MetaRoute.Normal;
        }

        private static bool ValidWeight(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }

    [Serializable]
    public sealed class MetaAffinityEntry
    {
        public string ItemId;
        public float Normal = 1f;
        public float Event;
        public float Interest;
        public float Shop;
    }
}
