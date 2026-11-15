using System.Collections.Generic;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Drives the battery's 10 indicator LEDs (children "10".."100" of "BatteryIndicator") from its charge,
    /// with the animations of <see cref="ChargeDisplay"/>.
    /// </summary>
    public class BatteryIndicator : MonoBehaviour
    {
        public const string INDICATOR_NAME = "BatteryIndicator";

        // One gadget drains about 1 %/h of game time, only a few thousandths of a percent per real second.
        private const float TREND_SAMPLE_SECONDS = 2f;
        private const float TREND_EPSILON = 0.00001f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // LEDs without emission are switched by darkening their albedo instead.
        private const float OFF_ALBEDO_FACTOR = 0.2f;

        // Serialized so Instantiate remaps them to the copy's own LEDs.
        public List<Renderer> leds = new List<Renderer>();
        public Battery battery = null!;

        private readonly List<Color> onColors = new List<Color>();
        private readonly List<int> colorIds = new List<int>();
        private bool[] shown = new bool[0];
        private MaterialPropertyBlock block = null!;

        private float sampledCharge = -1f;
        private float sampleTimer;
        private int trend;
        private readonly ChargeDisplay display = new ChargeDisplay();

        // Finds the LEDs under BatteryIndicator, ordered by their numeric names. Returns null without them.
        public static BatteryIndicator? Attach(GameObject gadget, Battery battery)
        {
            var root = FindDeep(gadget.transform, INDICATOR_NAME);
            if (root == null)
            {
                return null;
            }

            var ordered = new SortedList<int, Renderer>();
            foreach (Transform child in root)
            {
                if (int.TryParse(child.name, out int percent) && child.TryGetComponent(out Renderer renderer))
                {
                    ordered[percent] = renderer;
                }
            }
            if (ordered.Count == 0)
            {
                return null;
            }

            var indicator = gadget.GetComponent<BatteryIndicator>();
            if (indicator == null)
            {
                indicator = gadget.AddComponent<BatteryIndicator>();
            }
            indicator.leds = new List<Renderer>(ordered.Values);
            indicator.battery = battery;
            return indicator;
        }

        public static Transform? FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                var found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ReadLedColors();
        }

        // Added at runtime to an item, Awake runs before Attach fills in the LEDs, so Update reads them then.
        private void ReadLedColors()
        {
            colorIds.Clear();
            onColors.Clear();
            foreach (var led in leds)
            {
                var material = led != null ? led.sharedMaterial : null;
                bool emissive = material != null
                    && material.IsKeywordEnabled("_EMISSION")
                    && material.GetColor(EmissionColorId).maxColorComponent > 0f;
                int id = emissive ? EmissionColorId : ColorId;
                colorIds.Add(id);
                onColors.Add(material != null && material.HasProperty(id) ? material.GetColor(id) : Color.white);
            }
            shown = new bool[leds.Count];
            for (int i = 0; i < shown.Length; i++)
            {
                shown[i] = true;
            }
        }

        private void Update()
        {
            if (battery == null || leds.Count == 0)
            {
                return;
            }
            if (shown.Length != leds.Count)
            {
                ReadLedColors();
            }

            float charge = battery.CurrentPower;
            UpdateTrend(charge);

            int on = display.Update(charge, trend, leds.Count, Time.deltaTime, out int blinkingOff);
            for (int i = 0; i < leds.Count; i++)
            {
                SetLed(i, i < on && i != blinkingOff);
            }
        }

        // Charge only moves on network ticks, so compare against a sample from a couple of seconds ago.
        private void UpdateTrend(float charge)
        {
            sampleTimer += Time.deltaTime;
            if (sampledCharge < 0f)
            {
                sampledCharge = charge;
                return;
            }
            if (sampleTimer < TREND_SAMPLE_SECONDS)
            {
                return;
            }
            sampleTimer = 0f;

            float delta = charge - sampledCharge;
            trend = delta > TREND_EPSILON ? 1 : delta < -TREND_EPSILON ? -1 : 0;
            sampledCharge = charge;
        }

        private void SetLed(int index, bool on)
        {
            if (shown[index] == on || leds[index] == null)
            {
                return;
            }
            shown[index] = on;

            int id = colorIds[index];
            Color off = id == EmissionColorId ? Color.black : onColors[index] * OFF_ALBEDO_FACTOR;
            leds[index].GetPropertyBlock(block);
            block.SetColor(id, on ? onColors[index] : off);
            leds[index].SetPropertyBlock(block);
        }
    }
}
