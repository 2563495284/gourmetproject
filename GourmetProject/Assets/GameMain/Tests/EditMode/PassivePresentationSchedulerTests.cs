using System;
using System.Collections.Generic;
using GourmetProject.Game.UI.Battle.View;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PassivePresentationSchedulerTests
    {
        [Test]
        public void Enqueue_LocksUntilDone()
        {
            var locks = new List<bool>();
            var scheduler = new PassivePresentationScheduler(locks.Add);
            Action complete = null;
            scheduler.Enqueue(done => complete = done);

            Assert.That(scheduler.IsBusy, Is.True);
            Assert.That(locks, Is.EqualTo(new[] { true }));

            complete();

            Assert.That(scheduler.IsBusy, Is.False);
            Assert.That(locks, Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void CancelAll_UnlocksAndIgnoresStaleDone()
        {
            var locks = new List<bool>();
            var scheduler = new PassivePresentationScheduler(locks.Add);
            Action complete = null;
            bool cancelled = false;
            scheduler.Enqueue(done => complete = done, () => cancelled = true);

            scheduler.CancelAll();
            complete();

            Assert.That(cancelled, Is.True);
            Assert.That(scheduler.IsBusy, Is.False);
            Assert.That(locks, Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void WhenIdle_WaitsUntilQueueDrains()
        {
            var scheduler = new PassivePresentationScheduler();
            Action first = null;
            Action second = null;
            bool idle = false;
            scheduler.Enqueue(done => first = done);
            scheduler.Enqueue(done => second = done);
            scheduler.WhenIdle(() => idle = true);

            Assert.That(idle, Is.False);
            first();
            Assert.That(idle, Is.False);
            second();
            Assert.That(idle, Is.True);
        }

        [Test]
        public void WhenIdle_FiresImmediatelyWhenEmpty()
        {
            var scheduler = new PassivePresentationScheduler();
            bool idle = false;
            scheduler.WhenIdle(() => idle = true);
            Assert.That(idle, Is.True);
        }

        [Test]
        public void CancelAll_InvokesQueuedCancelsAndThenIdle()
        {
            var scheduler = new PassivePresentationScheduler();
            var cancelled = new List<string>();
            bool idle = false;
            scheduler.Enqueue(_ => { }, () => cancelled.Add("active"));
            scheduler.Enqueue(_ => { }, () => cancelled.Add("queued"));
            scheduler.WhenIdle(() => idle = true);

            scheduler.CancelAll();

            Assert.That(cancelled, Is.EqualTo(new[] { "active", "queued" }));
            Assert.That(idle, Is.True);
            Assert.That(scheduler.IsBusy, Is.False);
        }

        [Test]
        public void StaleDone_DoesNotStartQueuedWorkAfterCancel()
        {
            var scheduler = new PassivePresentationScheduler();
            Action firstDone = null;
            bool secondStarted = false;
            scheduler.Enqueue(done => firstDone = done);
            scheduler.Enqueue(_ => secondStarted = true);

            scheduler.CancelAll();
            firstDone();

            Assert.That(secondStarted, Is.False);
            Assert.That(scheduler.IsBusy, Is.False);
        }
    }
}
