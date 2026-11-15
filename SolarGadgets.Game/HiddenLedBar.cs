namespace SolarGadgets.Game
{
    // Battery.Initialize requires an LED bar driver; this one shows nothing.
    public class HiddenLedBar : LedBarDriverBase
    {
        public override void UpdateLeds(int amount)
        {
        }
    }
}
