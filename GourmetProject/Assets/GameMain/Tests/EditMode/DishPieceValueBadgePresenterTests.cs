using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DishPieceValueBadgePresenterTests
    {
        [Test]
        public void SetVisible_False_KeepsBadgeHiddenAfterRelayout()
        {
            var host = new GameObject("BadgePresenterHost", typeof(DishPieceValueBadgePresenter));
            var prefabObject = new GameObject("BadgePrefab", typeof(DishValueBadgeView));
            DishPieceValueBadgePresenter presenter = host.GetComponent<DishPieceValueBadgePresenter>();
            try
            {
                var serialized = new SerializedObject(presenter);
                serialized.FindProperty("_badgePrefab").objectReferenceValue =
                    prefabObject.GetComponent<DishValueBadgeView>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                DishShape shape = DishShape.FromRows(new[] { "X" });
                presenter.UpdateLayout(shape, 1f, 1f);

                Assert.That(presenter.View, Is.Not.Null);
                Assert.That(presenter.View.gameObject.activeSelf, Is.True);

                presenter.SetVisible(false);

                Assert.That(presenter.View.gameObject.activeSelf, Is.False);

                presenter.UpdateLayout(shape, 1f, 1f);

                Assert.That(presenter.View.gameObject.activeSelf, Is.False);

                presenter.SetVisible(true);

                Assert.That(presenter.View.gameObject.activeSelf, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(prefabObject);
            }
        }
    }
}
