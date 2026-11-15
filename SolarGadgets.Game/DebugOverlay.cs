using System.Linq;
using System.Text;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// On-screen list of every car with a solar network: diesel locos and cars with panels or batteries.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        private const float REFRESH_INTERVAL = 0.5f;
        private const float WIDTH = 760f;

        private readonly StringBuilder text = new StringBuilder();
        private string cachedText = "";
        private float refreshTimer;
        private Vector2 scroll;

        public static void Create()
        {
            var overlay = new GameObject("SolarGadgetsDebugOverlay");
            DontDestroyOnLoad(overlay);
            overlay.AddComponent<DebugOverlay>();
        }

        private void Update()
        {
            if (!Main.Settings.debugOverlay)
            {
                return;
            }

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
            {
                return;
            }
            refreshTimer = REFRESH_INTERVAL;

            text.Clear();
            foreach (var network in SolarPowerNetwork.All.Where(network => network.HasContent).OrderBy(network => network.CarLabel))
            {
                network.AppendStatus(text);
            }
            cachedText = text.Length > 0 ? text.ToString() : "No locos or cars with solar gadgets loaded.";
        }

        private void OnGUI()
        {
            if (!Main.Settings.debugOverlay)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10f, 10f, WIDTH, Screen.height * 0.6f), GUI.skin.box);
            GUILayout.Label("SolarGadgets debug");
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(cachedText);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
