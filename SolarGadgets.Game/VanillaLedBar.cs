using System.Collections.Generic;
using DV;
using DV.Customization.Gadgets;
using DV.Customization.Gadgets.Implementations;
using UnityEngine;

namespace SolarGadgets.Game
{
    /// <summary>
    /// Clones the game's brake cylinder LED bar at runtime, so the charge meter reuses its model without
    /// shipping vanilla assets, and splits its LED strip into 10 recolored segments.
    /// </summary>
    public static class VanillaLedBar
    {
        public const string VISUAL_NAME = "gadget_brake_led_bar_base";
        private const string LED_PATH = "Brake LED Bar/LED";
        private const string LAMP_PATH = "L_BrakePipe";

        // Same bands as the battery LEDs: 10-40 red, 50-70 yellow, 80-100 green.
        private static readonly Color Red = new Color(1f, 0.08f, 0.04f);
        private static readonly Color Yellow = new Color(1f, 0.8f, 0.05f);
        private static readonly Color Green = new Color(0.35f, 1f, 0.08f);
        private const float EMISSION_STRENGTH = 0.8f;

        // The big lamp: a dim blue lens when off, glowing blue when the batteries are empty.
        private static readonly Color LampBlue = new Color(0.1f, 0.35f, 1f);
        private const float LAMP_OFF_BRIGHTNESS = 0.5f;
        private const float LAMP_GLOW_STRENGTH = 1.5f;

        // Shrinks each segment a little so the unlit base strip shows between them.
        private const float SEGMENT_FILL = 0.88f;

        // Gap between the shelf's front edge and the meter, like the panel and battery shelf prefabs.
        private const float SHELF_MARGIN = 0.02f;

        // The LED_BrakeTrain quad's V runs from 0 at its +X end to this value at its -X end.
        private const float LED_UV_LENGTH = 0.71875f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static GadgetItem? item;
        private static readonly Dictionary<Texture, Texture2D> grayscaleCopies = new Dictionary<Texture, Texture2D>();
        private static readonly Dictionary<int, Material> segmentMaterials = new Dictionary<int, Material>();
        private static Material? lampMaterial;

        public static GadgetItem? Item
        {
            get
            {
                if (item == null)
                {
                    item = FindItem();
                }
                return item;
            }
        }

        // The brake LED bar is the only gadget whose LOD object is a GadgetBrakeLEDBarLOD.
        private static GadgetItem? FindItem()
        {
            var specs = Globals.G != null && Globals.G.Items != null ? Globals.G.Items.items : null;
            if (specs != null)
            {
                foreach (var spec in specs)
                {
                    var gadgetItem = spec != null ? spec.GetComponent<GadgetItem>() : null;
                    if (gadgetItem != null && gadgetItem.gadgetPrefab != null
                        && gadgetItem.gadgetPrefab.GetComponentInChildren<GadgetBrakeLEDBarLOD>(true) != null)
                    {
                        return gadgetItem;
                    }
                }
            }
            Main.Warning("Brake LED bar item not found, the charge meter has no model");
            return null;
        }

        public static Transform? FindVisual(Transform root)
        {
            return BatteryIndicator.FindDeep(root, VISUAL_NAME);
        }

        // Clones the model under parent, keeping its offset to the vanilla root. Returns its 10 segments, bottom first.
        public static List<Renderer> CloneInto(Transform sourceRoot, Transform parent, out LampControl? lamp)
        {
            lamp = null;
            var source = FindVisual(sourceRoot);
            if (source == null)
            {
                Main.Warning($"[ChargeMeter] No {VISUAL_NAME} under {sourceRoot.name}");
                return new List<Renderer>();
            }

            var clone = Object.Instantiate(source.gameObject, parent, false);
            clone.name = VISUAL_NAME;
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;

            lamp = clone.transform.Find(LAMP_PATH)?.GetComponent<LampControl>();
            if (lamp != null && lamp.lampInd != null)
            {
                lamp.lampInd.emissionColor = LampBlue * LAMP_GLOW_STRENGTH;
                foreach (var renderer in lamp.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = LampMaterial(renderer.sharedMaterial);
                }
            }

            var sourceLed = source.Find(LED_PATH);
            var cloneLed = clone.transform.Find(LED_PATH);
            return sourceLed != null && cloneLed != null ? SplitLed(sourceLed, cloneLed) : new List<Renderer>();
        }

        // LedBarDriverUV shows n of 10 by shrinking one quad toward its +X end and scaling the texture's V.
        // Here each segment gets its own quad and V slice, so segments can switch and blink individually.
        // Base values come from the untouched vanilla LED, because the clone's driver may already have shrunk it.
        private static List<Renderer> SplitLed(Transform sourceLed, Transform cloneLed)
        {
            var segments = new List<Renderer>();
            var driver = sourceLed.GetComponent<LedBarDriverUV>();
            var sourceRenderer = sourceLed.GetComponent<MeshRenderer>();
            if (driver == null || sourceRenderer == null)
            {
                return segments;
            }

            int count = driver.ledsCount;
            float length = driver.length;
            Vector3 baseScale = sourceLed.localScale;
            Vector3 basePosition = sourceLed.localPosition;

            var cloneDriver = cloneLed.GetComponent<LedBarDriverUV>();
            if (cloneDriver != null)
            {
                Object.DestroyImmediate(cloneDriver);
            }

            for (int i = 0; i < count; i++)
            {
                var segment = i == 0 ? cloneLed : Object.Instantiate(cloneLed.gameObject, cloneLed.parent, false).transform;
                segment.name = $"LED {(i + 1) * 100 / count}";
                segment.gameObject.SetActive(true);
                segment.localScale = new Vector3(baseScale.x / count * SEGMENT_FILL, baseScale.y, baseScale.z);
                segment.localPosition = new Vector3(basePosition.x + length / 2f - length / count * (i + 0.5f), basePosition.y, basePosition.z);

                var renderer = segment.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = SegmentMaterial(sourceRenderer.sharedMaterial, i, count);
                // The vanilla driver's Awake left a property block with texture scale 0 on the original LED.
                renderer.SetPropertyBlock(null);
                segments.Add(renderer);
            }
            return segments;
        }

        private static Color SegmentColor(int index, int count)
        {
            float percent = (index + 1) * 100f / count;
            return percent <= 40f ? Red : percent <= 70f ? Yellow : Green;
        }

        // The vanilla LED texture is orange; a grayscale copy keeps its detail and takes any tint.
        // One material per segment holds its texture slice: property blocks would not survive Instantiate.
        private static Material SegmentMaterial(Material vanilla, int index, int count)
        {
            if (segmentMaterials.TryGetValue(index, out var cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(vanilla);
            var gray = GrayscaleCopy(vanilla.GetTexture(MainTexId));
            if (gray != null)
            {
                material.SetTexture(MainTexId, gray);
                material.SetTexture(EmissionMapId, gray);
            }
            material.SetTextureScale(MainTexId, new Vector2(1f, 1f / count));
            material.SetTextureOffset(MainTexId, new Vector2(0f, LED_UV_LENGTH * index / count));

            var color = SegmentColor(index, count);
            material.SetColor(ColorId, color);
            material.SetColor(EmissionColorId, color * EMISSION_STRENGTH);
            segmentMaterials[index] = material;
            return material;
        }

        // The vanilla lens is reddish in both its albedo and emission map; grayscale copies take the blue tint.
        // IndicatorEmission still drives _EmissionColor, so only the off look is set here.
        private static Material LampMaterial(Material vanilla)
        {
            if (lampMaterial != null)
            {
                return lampMaterial;
            }

            var material = new Material(vanilla);
            var albedo = GrayscaleCopy(vanilla.GetTexture(MainTexId));
            if (albedo != null)
            {
                material.SetTexture(MainTexId, albedo);
            }
            var emission = GrayscaleCopy(vanilla.GetTexture(EmissionMapId));
            if (emission != null)
            {
                material.SetTexture(EmissionMapId, emission);
            }
            material.SetColor(ColorId, LampBlue * LAMP_OFF_BRIGHTNESS);
            lampMaterial = material;
            return material;
        }

        // The game texture is not readable, so it is copied through a RenderTexture first.
        private static Texture2D? GrayscaleCopy(Texture? source)
        {
            if (source == null)
            {
                return null;
            }
            if (grayscaleCopies.TryGetValue(source, out var cached) && cached != null)
            {
                return cached;
            }

            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            var pixels = copy.GetPixels();
            float brightest = 0.0001f;
            foreach (var pixel in pixels)
            {
                brightest = Mathf.Max(brightest, pixel.maxColorComponent);
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = pixels[i].maxColorComponent / brightest;
                pixels[i] = new Color(value, value, value, pixels[i].a);
            }
            copy.SetPixels(pixels);
            copy.Apply(true);
            grayscaleCopies[source] = copy;
            return copy;
        }

        // On the gadget prefab: the vanilla size, mount needs, collider and placement highlight.
        // custom_item_mod passes the bundle asset here, which cannot get child objects, so the model comes in Awake.
        // Also called again from GadgetItem.Awake, in case custom_item_mod copied the proxy's fields over ours.
        public static void SetUpGadgetPrefab(GadgetChargeMeter meter)
        {
            var vanillaItem = Item;
            if (vanillaItem == null)
            {
                return;
            }
            var vanillaGadget = vanillaItem.gadgetPrefab;
            if (meter.boundsSize == vanillaGadget.boundsSize && meter.highlightMeshes == vanillaGadget.highlightMeshes)
            {
                return;
            }

            meter.boundsCenter = vanillaGadget.boundsCenter;
            meter.boundsSize = vanillaGadget.boundsSize;
            meter.requiredMountPoints = vanillaGadget.requiredMountPoints;
            meter.removalMethod = vanillaGadget.removalMethod;
            // Both prefabs sit at the origin, so the vanilla meshes line up for the placement highlight.
            meter.highlightMeshes = vanillaGadget.highlightMeshes;
            meter.highlightMeshesInitialized = false;
            CopyCollider(vanillaGadget.GetComponentInChildren<BoxCollider>(true), meter.gameObject);
        }

        // On each gadget instance: the vanilla model and a display driving its LEDs.
        public static void SetUpGadgetInstance(GadgetChargeMeter meter)
        {
            var vanillaItem = Item;
            if (vanillaItem == null || FindVisual(meter.transform) != null)
            {
                return;
            }

            var segments = CloneInto(vanillaItem.gadgetPrefab.transform, meter.transform, out var lamp);

            // The halo is built relative to this instance, so it needs this instance's own meshes.
            meter.highlightMeshes = CloneHighlightMeshes(vanillaItem.gadgetPrefab, meter.transform);
            meter.highlightMeshesInitialized = false;

            var display = meter.gameObject.AddComponent<ChargeMeterDisplay>();
            display.segments = segments;
            display.lamp = lamp;
            display.meter = meter;
        }

        // The shop shelf shows a plain copy of the bundle item, so it gets the vanilla model too.
        // Item and shelf use the gadget's model: the vanilla item's own copy has an empty LED object.
        public static void SetUpShelfSample(GameObject sample)
        {
            var vanillaItem = Item;
            if (vanillaItem != null && FindVisual(sample.transform) == null)
            {
                CloneInto(vanillaItem.gadgetPrefab.transform, sample.transform, out _);
                var visual = FindVisual(sample.transform);
                RemoveLodCulling(visual);

                // The shelf origin is its front edge and the sample is turned 180 degrees, so local +Z is "back".
                // Lift the bottom onto the shelf and keep the front SHELF_MARGIN behind the edge.
                var bounds = vanillaItem.gadgetPrefab.Bounds;
                visual!.localPosition += new Vector3(0f, bounds.extents.y - bounds.center.y, bounds.extents.z - bounds.center.z + SHELF_MARGIN);
            }
        }

        // Gives the held item the vanilla model, collider and icons. The segments stay lit as a preview.
        public static void SetUpItem(GadgetItem meterItem)
        {
            var vanillaItem = Item;
            if (vanillaItem == null || FindVisual(meterItem.transform) != null)
            {
                return;
            }

            CloneInto(vanillaItem.gadgetPrefab.transform, meterItem.transform, out _);
            CopyCollider(vanillaItem.GetComponent<BoxCollider>(), meterItem.gameObject);

            // custom_item_mod lists the grab colliders once from the stub, before this collider existed.
            var itemSpec = meterItem.GetComponent<DV.CabControls.Spec.Item>();
            if (itemSpec != null)
            {
                var colliders = new List<GameObject>(itemSpec.colliderGameObjects ?? new GameObject[0]);
                if (!colliders.Contains(meterItem.gameObject))
                {
                    colliders.Add(meterItem.gameObject);
                }
                itemSpec.colliderGameObjects = colliders.ToArray();
            }

            var spec = meterItem.GetComponent<InventoryItemSpec>();
            var vanillaSpec = vanillaItem.GetComponent<InventoryItemSpec>();
            if (spec != null && vanillaSpec != null)
            {
                spec.itemIconSprite = vanillaSpec.itemIconSprite;
                spec.itemIconSpriteStandard = vanillaSpec.itemIconSpriteStandard;
                spec.itemIconSpriteDropped = vanillaSpec.itemIconSpriteDropped;
            }
        }

        // The gadget's LODGroup is tuned for a cab and culls the small bar from shelf distance.
        // Keep LOD0 only: hide the lower LOD meshes and drop the group.
        private static void RemoveLodCulling(Transform? visual)
        {
            var lodGroup = visual != null ? visual.GetComponent<LODGroup>() : null;
            if (lodGroup == null)
            {
                return;
            }

            var lods = lodGroup.GetLODs();
            for (int i = 1; i < lods.Length; i++)
            {
                foreach (var renderer in lods[i].renderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
            }
            Object.DestroyImmediate(lodGroup);
        }

        // The clone's counterparts of the vanilla highlight meshes, matched by name.
        private static MeshFilter[] CloneHighlightMeshes(GadgetBase vanillaGadget, Transform cloneRoot)
        {
            var result = new List<MeshFilter>();
            foreach (var vanillaMesh in vanillaGadget.highlightMeshes ?? new MeshFilter[0])
            {
                var match = vanillaMesh != null ? BatteryIndicator.FindDeep(cloneRoot, vanillaMesh.name) : null;
                if (match != null && match.TryGetComponent(out MeshFilter filter))
                {
                    result.Add(filter);
                }
            }
            return result.ToArray();
        }

        private static void CopyCollider(BoxCollider? source, GameObject target)
        {
            if (source == null)
            {
                return;
            }
            var collider = target.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = target.AddComponent<BoxCollider>();
            }
            collider.center = source.transform.localPosition + source.center;
            collider.size = source.size;
        }
    }
}
