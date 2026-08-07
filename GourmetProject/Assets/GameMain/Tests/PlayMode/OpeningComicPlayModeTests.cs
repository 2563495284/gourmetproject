using System.Collections;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GourmetProject.Tests.PlayMode
{
    public sealed class OpeningComicPlayModeTests
    {
        [UnityTest]
        public IEnumerator OpeningComic_AdvancesEightPanels_ThenPersistsAndOpensMainMenu()
        {
            string key = PlayerPrefsOpeningComicProgressStore.CompletedVersionKey;
            bool hadOriginalValue = PlayerPrefs.HasKey(key);
            int originalOpeningVersion = PlayerPrefs.GetInt(key, 0);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();

            try
            {
                SceneManager.LoadScene("Launch");

                OpeningComicForm form = null;
                float openDeadline = Time.realtimeSinceStartup + 10f;
                while (form == null && Time.realtimeSinceStartup < openDeadline)
                {
                    form = Object.FindFirstObjectByType<OpeningComicForm>();
                    yield return null;
                }

                Assert.That(form, Is.Not.Null, "Opening comic did not open before the main menu.");
                Assert.That(Object.FindFirstObjectByType<MainMenuForm>(), Is.Null);

                yield return new WaitForSecondsRealtime(0.45f);
                for (int click = 0; click < 7; click++)
                {
                    form.HandleAdvanceRequest();
                    form.HandleAdvanceRequest(); // Same-frame rapid click must be ignored.
                    yield return new WaitForSecondsRealtime(0.58f);
                    Assert.That(form, Is.Not.Null);
                }

                form.HandleAdvanceRequest();

                MainMenuForm mainMenu = null;
                float completionDeadline = Time.realtimeSinceStartup + 8f;
                while (mainMenu == null && Time.realtimeSinceStartup < completionDeadline)
                {
                    mainMenu = Object.FindFirstObjectByType<MainMenuForm>();
                    yield return null;
                }

                Assert.That(mainMenu, Is.Not.Null, "Final click did not open the main menu.");

                Assert.That(
                    PlayerPrefs.GetInt(key, 0),
                    Is.EqualTo(OpeningComicProgress.CurrentVersion));
                Assert.That(OpeningComicProgress.ShouldPlay(), Is.False);
            }
            finally
            {
                if (hadOriginalValue)
                {
                    PlayerPrefs.SetInt(key, originalOpeningVersion);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }

                PlayerPrefs.Save();
            }
        }
    }
}
