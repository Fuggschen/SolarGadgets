using System.Collections.Generic;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Drives the charge meter's 10 segments and red lamp from the combined charge of its car's batteries.
    /// Dark while unsoldered or when the car has no battery.
    /// </summary>
    public class ChargeMeterDisplay : MonoBehaviour
    {
        // Serialized so Instantiate remaps them to the copy's own objects.
        public List<Renderer> segments = new List<Renderer>();
        public LampControl? lamp;
        public GadgetChargeMeter meter = null!;

        private readonly ChargeDisplay display = new ChargeDisplay();
        private bool lampOn = true;

        private void Update()
        {
            float? charge = null;
            int trend = 0;
            if (meter != null && meter.IsSoldered && meter.CustomTrainCar != null)
            {
                var network = SolarPowerNetwork.Get(meter.CustomTrainCar);
                if (network != null)
                {
                    charge = network.CombinedCharge;
                    trend = network.ChargeTrend;
                }
            }

            int on = 0;
            int blinkingOff = -1;
            if (charge.HasValue)
            {
                on = display.Update(charge.Value, trend, segments.Count, Time.deltaTime, out blinkingOff);
            }
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                bool active = i < on && i != blinkingOff;
                if (segment != null && segment.gameObject.activeSelf != active)
                {
                    segment.gameObject.SetActive(active);
                }
            }

            SetLamp(charge.HasValue && charge.Value < 0.01f);
        }

        private void SetLamp(bool on)
        {
            if (lamp == null || lampOn == on)
            {
                return;
            }
            lampOn = on;
            lamp.SetLampState(on ? LampControl.LampState.On : LampControl.LampState.Off);
        }
    }
}
