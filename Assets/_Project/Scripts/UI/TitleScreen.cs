using System;
using AdaptiveBossArena.Utilities.Statistics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// The title screen: the game's front door.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately its own scene rather than a panel over the arena. The fight scene spins up
    /// services, combatants and a learning loop the moment it loads; a menu has no business paying
    /// that cost, and keeping them separate means the fight always begins from a clean, freshly
    /// loaded state.
    /// </para>
    /// <para>
    /// Starting the game is a scene load rather than an additive reveal for the same reason a retry
    /// within a fight is <em>not</em> a scene load: the fight wants one clean construction up front,
    /// and then to reset itself in place so a seed survives repeated attempts.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class TitleScreen : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Name of the arena scene to load. Must be in the build settings.")]
        private string _arenaSceneName = "Arena";

        [SerializeField]
        [Tooltip("The quit button, hidden on platforms where quitting does nothing.")]
        private GameObject _quitButton;

        /// <summary>Loads the fight. Bound to the start button.</summary>
        public void StartGame() => SceneManager.LoadScene(_arenaSceneName);

        /// <summary>
        /// Quits the application, or leaves play mode in the editor. Bound to the quit button.
        /// </summary>
        /// <remarks>
        /// Does nothing in a browser, where a page cannot close its own tab. The button is hidden
        /// there instead of sitting on the front door doing nothing — see <see cref="Start"/>.
        /// </remarks>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_WEBGL
            Application.Quit();
#endif
        }

        private void Awake()
        {
            CleanLegacyChallenges();
        }

        private void OnEnable()
        {
            CleanLegacyChallenges();
        }

        private void CleanLegacyChallenges()
        {
            // Remove legacy challenges and modifiers that are no longer used for the rule-based duel
            string[] legacyElements = {
                "ChallengesHeader",
                "Fast Learner Label", "Fast Learner Toggle",
                "No Healing Label", "No Healing Toggle",
                "Fragile Label", "Fragile Toggle",
                "Training Label", "Training Toggle"
            };

            foreach (string elem in legacyElements)
            {
                Transform directChild = transform.Find(elem);
                if (directChild != null)
                {
                    directChild.gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(directChild.gameObject);
                    else DestroyImmediate(directChild.gameObject);
                }

                GameObject obj = GameObject.Find(elem);
                if (obj != null)
                {
                    obj.SetActive(false);
                    if (Application.isPlaying) Destroy(obj);
                    else DestroyImmediate(obj);
                }
            }

            var runMod = GetComponent<RunModifierMenu>();
            if (runMod != null)
            {
                if (Application.isPlaying) Destroy(runMod);
                else DestroyImmediate(runMod);
            }
        }

        private void Start()
        {
            // A run launched to measure performance goes straight into the fight. The probe that
            // measures lives in the arena, so a capture left waiting here measured a menu and never
            // wrote its result. An ordinary launch requests nothing and is unaffected.
            if (PerfCaptureRequest.Read(Environment.GetCommandLineArgs(), Application.absoluteURL).IsRequested)
            {
                StartGame();
                return;
            }

            if (_quitButton != null && !PauseMenu.CanQuit)
            {
                _quitButton.SetActive(false);
            }
        }

        /// <summary>Assigns the scene to load. Used by the scene generator.</summary>
        /// <param name="arenaSceneName">Name of the arena scene.</param>
        /// <param name="quitButton">Quit button, hidden where quitting is meaningless.</param>
        public void Bind(string arenaSceneName, GameObject quitButton = null)
        {
            _arenaSceneName = arenaSceneName;
            _quitButton = quitButton;
        }
    }
}
