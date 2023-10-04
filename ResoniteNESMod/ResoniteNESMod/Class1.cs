using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.UIX;
using Elements.Core;

namespace ResoniteNESMod
{
    public class ResoniteNESMod : ResoniteMod
    {
        public override string Author => "Ikubaysan";
        public override string Name => "ResoniteNESMod";
        public override string Version => "1.0.0";


        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("enabled", "Should the mod be enabled?", () => true);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> CANVAS_SLOT_WIDTH = new ModConfigurationKey<int>("canvas_slot_width", "The width of the canvas slot", () => 50);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> CANVAS_SLOT_HEIGHT = new ModConfigurationKey<int>("canvas_slot_height", "The height of the canvas slot", () => 50);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<string> CANVAS_SLOT_NAME = new ModConfigurationKey<string>("canvas_slot_name", "The name of the canvas slot", () => "NESUIXCanvas");

        private static ModConfiguration Config; //If you use config settings, this will be where you interface with them

        public override void OnEngineInit()
        {
            Config = GetConfiguration(); //Get this mods' current ModConfiguration
            Config.Save(true); //If you'd like to save the default config values to file
            Harmony harmony = new Harmony("com.ikubaysan.ResoniteNESMod");
            harmony.PatchAll();

            Debug("a debug log from ResoniteNESMod...");
            Msg("a regular log from ResoniteNESMod...");
            Warn("a warn log from ResoniteNESMod...");
            Error("an error log from ResoniteNESMod...");
        }

        // OnAttach() is called whenever a new Canvas is created, but not when I spawn one from Inventory.
        //[HarmonyPatch(typeof(Canvas), "OnAttach")]
        //[HarmonyPatch(typeof(Canvas), "OnAttach")]
        //[HarmonyPatch(typeof(Canvas), "OnAwake")]
        [HarmonyPatch(typeof(Canvas), "FinishCanvasUpdate")]  // (many hits, but matched for once)

        class ReosoniteNESModPatcher
        {
            static void Postfix(Canvas __instance)
            {
                if (!Config.GetValue(ENABLED)) return;
                if (__instance.Slot.Name != Config.GetValue(CANVAS_SLOT_NAME)) return;

                // Retrieve the values of configuration keys at the time the method is called
                int canvasSlotWidth = Config.GetValue(CANVAS_SLOT_WIDTH);
                int canvasSlotHeight = Config.GetValue(CANVAS_SLOT_HEIGHT);
                string canvasSlotName = Config.GetValue(CANVAS_SLOT_NAME);

                // Slot name matches the constant
                Msg("Matched with the slot name: " + __instance.Slot.Name);

                __instance.Slot.Name = "VeryCoolModded_" + __instance.Slot.Name;
                

                __instance.Slot.GetComponent<Canvas>().Size.Value = new int2(canvasSlotWidth, canvasSlotHeight);


                Msg("Changed the slot name to: " + __instance.Slot.Name);

                Slot backgroundSlot = __instance.Slot.FindChild("Background");
                if (backgroundSlot == null)
                {
                    Msg("Could not find the child slot: Background");
                    return;
                }

                Msg("Found the child slot: " + backgroundSlot.Name);

                Slot imageSlot = backgroundSlot.FindChild("Image");
                if (imageSlot == null)
                {
                    Msg("Could not find the child slot: Image");
                    return;
                }

                Msg("Found the child slot: " + imageSlot.Name);


                Slot contentSlot = imageSlot.FindChild("Content");
                if (contentSlot == null)
                {
                    Msg("Could not find the child slot: Content");
                    return;
                }

                Msg("Found the child slot: " + contentSlot.Name);

                // Delete all existing children of the content slot, which are HorizontalLayouts
                contentSlot.DestroyChildren();

                Msg("Destroyed all children of the content slot: " + contentSlot.Name);

                // Create new HorizontalLayouts according to the height constant

                Random rand = new Random();

                // For the count of the height constant, call contentSlot.AddSlot
                for (int i = 0; i < canvasSlotHeight; i++)
                {
                    Slot horizontalLayoutSlot = contentSlot.AddSlot("HorizontalLayout" + i);
                    horizontalLayoutSlot.AttachComponent<RectTransform>();
                    HorizontalLayout horizontalLayoutComponent = horizontalLayoutSlot.AttachComponent<HorizontalLayout>();
                    horizontalLayoutComponent.PaddingTop.Value = i;
                    horizontalLayoutComponent.PaddingBottom.Value = canvasSlotHeight - i - 1;

                    // Add a slot for each column in the horizontal layout
                    for (int j = 0; j < canvasSlotWidth; j++)
                    {
                        Slot verticalSlot = horizontalLayoutSlot.AddSlot("VerticalSlot" + j);
                        verticalSlot.AttachComponent<RectTransform>();
                        Image imageComponent = verticalSlot.AttachComponent<Image>();
                        // Set the tint to a random color
                        imageComponent.Tint.Value = new colorX(
                            (float) rand.NextDouble(), 
                            (float) rand.NextDouble(), 
                            (float) rand.NextDouble(), 
                            1);
                    }
                }

                Msg("Created new HorizontalLayouts according to the height constant: " + canvasSlotHeight);
            }
        }
    }
}
