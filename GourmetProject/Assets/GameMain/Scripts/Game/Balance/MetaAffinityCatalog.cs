using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Balance
{
    [CreateAssetMenu(menuName = "Gourmet/Balance/Meta Affinity Catalog", fileName = "MetaAffinityCatalog")]
    public sealed class MetaAffinityCatalog : ScriptableObject
    {
        public List<MetaAffinityEntry> Entries = new List<MetaAffinityEntry>();

        public float Get(string itemId, MetaRoute route)
        {
            MetaAffinityEntry entry = Entries.Find(v => v.ItemId == itemId);
            if (entry == null) return route == MetaRoute.Normal ? 1f : 0f;
            switch (route)
            {
                case MetaRoute.Event: return entry.Event;
                case MetaRoute.Interest: return entry.Interest;
                case MetaRoute.Shop: return entry.Shop;
                default: return entry.Normal;
            }
        }
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
