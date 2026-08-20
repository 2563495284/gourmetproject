using System.Collections;
using GourmetProject.Game.UI.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GourmetProject.Tests.PlayMode
{
    public sealed class ToastPlayModeTests
    {
        [UnityTest]
        public IEnumerator Presenter_UsesUnscaledTime_RisesFadesAndReusesCleanly()
        {
#if UNITY_EDITOR
            ToastForm prefab = AssetDatabase.LoadAssetAtPath<ToastForm>(
                "Assets/GameMain/Content/Prefabs/UI/Common/ToastForm.prefab");
#else
            ToastForm prefab = null;
#endif
            ToastTheme theme = Resources.Load<ToastTheme>("Sprites/UI/ToastFresh/ToastTheme");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(theme, Is.Not.Null);

            ToastForm instance = Object.Instantiate(prefab);
            ToastPresenter presenter = instance.GetComponentInChildren<ToastPresenter>(true);
            Assert.That(presenter, Is.Not.Null);

            float originalTimeScale = Time.timeScale;
            int callbacks = 0;
            try
            {
                Time.timeScale = 0f;
                presenter.Play(
                    new ToastRequest("获得了新的料理灵感", ToastKind.Success),
                    () => callbacks++);
                Assert.That(presenter.CurrentIcon, Is.SameAs(theme.ResolveIcon(ToastKind.Success)));
                Assert.That(presenter.Alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(presenter.PositionY, Is.EqualTo(theme.EnterOffsetY).Within(0.01f));
                Assert.That(presenter.Scale, Is.EqualTo(1f).Within(0.001f));

                yield return new WaitForSecondsRealtime(theme.EnterDuration + 0.08f);
                Assert.That(presenter.Alpha, Is.GreaterThan(0.95f));
                Assert.That(presenter.PositionY, Is.EqualTo(0f).Within(1f));
                Assert.That(presenter.Scale, Is.EqualTo(1f).Within(0.001f));

                yield return new WaitForSecondsRealtime(
                    Mathf.Max(0f, theme.HoldDuration - 0.04f) + theme.ExitDuration * 0.5f);
                Assert.That(presenter.Alpha, Is.InRange(0.05f, 0.9f));
                Assert.That(presenter.PositionY, Is.EqualTo(0f).Within(0.01f));

                yield return new WaitForSecondsRealtime(theme.ExitDuration * 0.6f + 0.08f);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(presenter.IsPlaying, Is.False);
                Assert.That(presenter.Alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(presenter.PositionY, Is.EqualTo(theme.EnterOffsetY).Within(0.01f));

                ToastKind[] kinds = { ToastKind.Info, ToastKind.Warning, ToastKind.Success };
                foreach (ToastKind kind in kinds)
                {
                    presenter.Play(new ToastRequest("复用测试", kind), () => callbacks++);
                    Assert.That(presenter.CurrentIcon, Is.SameAs(theme.ResolveIcon(kind)), kind.ToString());
                    presenter.CompleteImmediately();
                }

                Assert.That(callbacks, Is.EqualTo(4));

                presenter.Play(
                    new ToastRequest(
                        "这是一条用于检查两行自适应宽度和省略行为的超长中文轻提示文案，这部分内容还会继续增长。",
                        ToastKind.Info),
                    () => callbacks++);
                Assert.That(
                    presenter.GetComponent<RectTransform>().sizeDelta.y,
                    Is.EqualTo(theme.DoubleLineHeight).Within(0.01f));
                presenter.CompleteImmediately();
                Assert.That(callbacks, Is.EqualTo(5));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                Object.Destroy(instance.gameObject);
            }
        }
    }
}
