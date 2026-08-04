using GourmetProject.Config;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopFormSlotCountTests
    {
        private const string ShopFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/ShopForm.prefab";
        private const string BattleFormPrefabPath =
            "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";

        [TestCase(ShopFormPrefabPath)]
        [TestCase(BattleFormPrefabPath)]
        public void ShopPrefab_ItemSectionsMatchConfiguredSaleSlotCounts(string prefabPath)
        {
            var config = new ConfigService();
            config.LoadAll();

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ShopForm shopForm = root.GetComponentInChildren<ShopForm>(true);
                Assert.That(shopForm, Is.Not.Null, $"{prefabPath} 缺少 ShopForm。");

                var serializedShopForm = new SerializedObject(shopForm);
                AssertSection<ShopPassiveItemBuyItemView>(
                    serializedShopForm,
                    "_passiveContainer",
                    config.Tables.TbGameBase.ShopPassiveItemSaleSlotCount,
                    "PassiveSlot_");
                AssertSection<ShopActiveItemBuyItemView>(
                    serializedShopForm,
                    "_activeContainer",
                    config.Tables.TbGameBase.ShopActiveItemSaleSlotCount,
                    "ActiveSlot_");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertSection<TCard>(
            SerializedObject serializedShopForm,
            string containerPropertyName,
            int expectedCount,
            string slotNamePrefix)
            where TCard : ShopBuyItemViewBase
        {
            SerializedProperty property = serializedShopForm.FindProperty(containerPropertyName);
            Assert.That(property, Is.Not.Null, $"ShopForm 缺少 {containerPropertyName} 序列化字段。");

            var container = property.objectReferenceValue as RectTransform;
            Assert.That(container, Is.Not.Null, $"{containerPropertyName} 未绑定。");
            Assert.That(container.childCount, Is.EqualTo(expectedCount));

            for (int i = 0; i < expectedCount; i++)
            {
                Transform slot = container.GetChild(i);
                var rect = slot as RectTransform;
                float expectedAnchorX = (i + 0.5f) / expectedCount;
                Assert.That(slot.name, Is.EqualTo($"{slotNamePrefix}{i + 1}"));
                Assert.That(slot.GetComponent<TCard>(), Is.Not.Null);
                Assert.That(rect, Is.Not.Null);
                Assert.That(rect.anchorMin.x, Is.EqualTo(expectedAnchorX).Within(0.0001f));
                Assert.That(rect.anchorMax, Is.EqualTo(rect.anchorMin));
                Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(110f, 110f)));
            }
        }
    }
}
