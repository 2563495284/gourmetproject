using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PendingPresentationCallbacksTests
    {
        [Test]
        public void CompleteAll_InvokesShownThenHiddenOnce()
        {
            var callbacks = new PendingPresentationCallbacks();
            var order = new System.Collections.Generic.List<string>();
            callbacks.SetShown(() => order.Add("shown"));
            callbacks.SetHidden(() => order.Add("hidden"));

            callbacks.CompleteAll();
            callbacks.CompleteAll();

            Assert.That(order, Is.EqualTo(new[] { "shown", "hidden" }));
        }

        [Test]
        public void CompleteShown_DoesNotDropHidden()
        {
            var callbacks = new PendingPresentationCallbacks();
            bool shown = false;
            bool hidden = false;
            callbacks.SetShown(() => shown = true);
            callbacks.SetHidden(() => hidden = true);

            callbacks.CompleteShown();

            Assert.That(shown, Is.True);
            Assert.That(hidden, Is.False);

            callbacks.CompleteHidden();
            Assert.That(hidden, Is.True);
        }
    }
}
