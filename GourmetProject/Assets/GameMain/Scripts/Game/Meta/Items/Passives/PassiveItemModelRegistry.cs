using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    /// <summary>
    /// 标注某 <see cref="PassiveItemModel"/> 子类绑定的被动道具 id（对应 tbpassiveitem.id）。
    /// 一个 id 对应一个子类；<see cref="PassiveItemModelRegistry"/> 反射建表。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PassiveItemModelAttribute : Attribute
    {
        public PassiveItemModelAttribute(string itemId)
        {
            ItemId = itemId;
        }

        public string ItemId { get; }
    }

    /// <summary>无行为兜底模型：未注册 id 或纯占位（TODO）道具使用，保证每个持有道具都有模型。</summary>
    [Preserve]
    public sealed class NoopPassiveModel : PassiveItemModel
    {
    }

    /// <summary>
    /// itemId → <see cref="PassiveItemModel"/> 子类的工厂。启动时反射扫描所有带
    /// <see cref="PassiveItemModelAttribute"/> 的类型建表；未注册 id 兜底 <see cref="NoopPassiveModel"/>。
    /// AOT/IL2CPP：模型仅经反射构造，需保留（子类各自 [Preserve]，或用 link.xml 保留本命名空间）。
    /// </summary>
    public static class PassiveItemModelRegistry
    {
        private static Dictionary<string, Type> _map;

        public static PassiveItemModel Create(string itemId)
        {
            EnsureBuilt();
            if (!string.IsNullOrEmpty(itemId) && _map.TryGetValue(itemId, out Type type))
            {
                return (PassiveItemModel)Activator.CreateInstance(type);
            }

            return new NoopPassiveModel();
        }

        public static bool HasModel(string itemId)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(itemId) && _map.ContainsKey(itemId);
        }

        private static void EnsureBuilt()
        {
            if (_map != null)
            {
                return;
            }

            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type baseType = typeof(PassiveItemModel);
            foreach (Type type in baseType.Assembly.GetTypes())
            {
                if (type.IsAbstract || !baseType.IsAssignableFrom(type))
                {
                    continue;
                }

                foreach (PassiveItemModelAttribute attr in type.GetCustomAttributes<PassiveItemModelAttribute>(false))
                {
                    if (!string.IsNullOrEmpty(attr.ItemId))
                    {
                        map[attr.ItemId] = type;
                    }
                }
            }

            _map = map;
        }
    }
}
