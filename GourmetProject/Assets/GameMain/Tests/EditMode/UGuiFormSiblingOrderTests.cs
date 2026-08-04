using GourmetProject.Runtime.UI;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class UGuiFormSiblingOrderTests
    {
        [Test]
        public void Open_ReusedFormMovesToLastSibling()
        {
            var group = new GameObject("UIGroup");
            try
            {
                var reusedObject = new GameObject("ReusedForm");
                reusedObject.transform.SetParent(group.transform, false);
                var reusedForm = reusedObject.AddComponent<TestUGuiForm>();
                reusedForm.InitializeForTest();
                reusedObject.SetActive(false);

                var currentForm = new GameObject("CurrentForm");
                currentForm.transform.SetParent(group.transform, false);
                Assert.That(
                    reusedObject.transform.GetSiblingIndex(),
                    Is.LessThan(currentForm.transform.GetSiblingIndex()));

                reusedForm.OpenForTest();

                Assert.That(reusedObject.activeSelf, Is.True);
                Assert.That(
                    reusedObject.transform.GetSiblingIndex(),
                    Is.EqualTo(group.transform.childCount - 1));
            }
            finally
            {
                Object.DestroyImmediate(group);
            }
        }
    }

    internal sealed class TestUGuiForm : UGuiForm
    {
        public void InitializeForTest()
        {
            OnInit(null);
        }

        public void OpenForTest()
        {
            OnOpen(null);
        }
    }
}
