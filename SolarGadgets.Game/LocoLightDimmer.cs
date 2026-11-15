using System.Collections.Generic;
using DV.Simulation.Cars;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Dims loco headlights and cab lights the way LightIntensityPortModifier does for steam dynamos.
    /// </summary>
    public class LocoLightDimmer
    {
        private static readonly int GlareBrightnessHash = Shader.PropertyToID("_Brightness");

        private readonly List<KeyValuePair<Light, float>> lights = new List<KeyValuePair<Light, float>>();
        private readonly HeadlightsMainController headlights;
        private readonly HeadlightBeamController? beam;
        private readonly MaterialPropertyBlock? glareBlock;

        private LocoLightDimmer(HeadlightsMainController headlights, CabLightsController? cabLights)
        {
            this.headlights = headlights;
            foreach (var light in headlights.allLightSources)
            {
                lights.Add(new KeyValuePair<Light, float>(light, light.intensity));
            }
            if (cabLights != null)
            {
                foreach (var lightObject in cabLights.lights)
                {
                    var light = lightObject.GetComponent<Light>();
                    if (light != null)
                    {
                        lights.Add(new KeyValuePair<Light, float>(light, light.intensity));
                    }
                }
            }
            beam = headlights.GetComponent<HeadlightBeamController>();
            if (headlights.allGlareRenderers.Count > 0)
            {
                glareBlock = new MaterialPropertyBlock();
            }
        }

        // Locos that already dim through LightIntensityPortModifier keep the game's dimming.
        public static LocoLightDimmer? Create(SimController sim)
        {
            var headlights = sim.headlightsController;
            if (headlights == null || headlights.GetComponent<LightIntensityPortModifier>() != null)
            {
                return null;
            }
            return new LocoLightDimmer(headlights, sim.cabLightsController);
        }

        public void Apply(float brightness)
        {
            foreach (var entry in lights)
            {
                if (entry.Key != null)
                {
                    entry.Key.intensity = entry.Value * brightness;
                }
            }

            if (beam != null)
            {
                beam.intensityMultiplier = brightness;
            }

            if (glareBlock == null)
            {
                return;
            }
            glareBlock.SetFloat(GlareBrightnessHash, brightness);
            foreach (var renderer in headlights.allGlareRenderers)
            {
                renderer.SetPropertyBlock(glareBlock);
            }
        }
    }
}
