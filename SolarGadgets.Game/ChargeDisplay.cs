using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Which of N charge LEDs are on. Idle: steady bar. Discharging: the highest lit LED blinks.
    /// Charging: a light runs up from the bar to the top, until full.
    /// </summary>
    public class ChargeDisplay
    {
        private const float BLINK_PERIOD = 1f;
        private const float RUN_STEP_SECONDS = 0.3f;
        private const float FULL_CHARGE = 99.9f;

        private float runTimer;

        // Returns how many LEDs from the bottom are on, and the index of one LED among them that is off this frame.
        public int Update(float charge, int trend, int count, float deltaTime, out int blinkingOff)
        {
            blinkingOff = -1;

            // Any charge above a step lights that step, so 30% shows 3 of 10 and 31% shows 4.
            int lit = Mathf.Clamp(Mathf.CeilToInt(charge / 100f * count - 0.001f), 0, count);

            if (trend > 0 && charge < FULL_CHARGE)
            {
                runTimer += deltaTime;
                return lit + (int)(runTimer / RUN_STEP_SECONDS) % (count - lit + 1);
            }

            runTimer = 0f;
            if (trend < 0 && lit > 0 && Time.time % BLINK_PERIOD >= BLINK_PERIOD / 2f)
            {
                blinkingOff = lit - 1;
            }
            return lit;
        }
    }
}
