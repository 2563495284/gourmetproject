using System.Reflection;
using GourmetProject.Game.Meta;
using GourmetProject.Game.UI.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ShopBuyItemViewBaseTests
    {
        [Test]
        public void BindUpdatesSiblingBuyLabel()
        {
            GameObject root = new GameObject("ShopCard", typeof(RectTransform));
            try
            {
                TestShopBuyItemView view = root.AddComponent<TestShopBuyItemView>();

                GameObject buttonObject = new GameObject("Image", typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(root.transform, false);
                Button button = buttonObject.GetComponent<Button>();

                GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(root.transform, false);
                Text label = labelObject.GetComponent<Text>();
                label.text = "购买 45";

                SetPrivateField(view, "_buyButton", button);

                var entry = new ShopEntry(ShopEntryKind.Dish, "dish_test", "测试食物", string.Empty, 45, 37);
                view.Bind(new ShopBuyItemViewContext(null, entry, true, null, null, null));

                Assert.That(label.text, Is.EqualTo("购买 37"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetPrivateField<T>(ShopBuyItemViewBase view, string fieldName, T value)
        {
            FieldInfo field = typeof(ShopBuyItemViewBase).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(view, value);
        }

        private sealed class TestShopBuyItemView : ShopBuyItemViewBase
        {
            protected override void ConfigureContent(ShopBuyItemViewContext context)
            {
            }
        }
    }
}
