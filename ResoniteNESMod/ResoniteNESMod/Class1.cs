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


namespace ResoniteNESMod
{
    public class ResoniteNESMod : ResoniteMod
    {
        public override string Author => "Ikubaysan";
        public override string Name => "ResoniteNESMod";
        public override string Version => "1.0.0";


        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("enabled", "Should the mod be enabled", () => true); 

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

        // OnAttach() is called whenever a Canvas is spawned (as well as any time a component is attached to a Canvas)
        //[HarmonyPatch(typeof(Canvas), "OnAttach")]
        //[HarmonyPatch(typeof(Canvas), "OnAttach")]
        //[HarmonyPatch(typeof(Canvas), "OnAwake")]
        [HarmonyPatch(typeof(Canvas), "FinishCanvasUpdate")]  // (many hits, but matched for once)

        class ReosoniteNESModPatcher
        {
            private const string CANVAS_SLOT_NAME = "UIXCanvas";
            private const int CANVAS_SLOT_WIDTH = 50;
            private const int CANVAS_SLOT_HEIGHT = 50;
            
            static void Postfix(Canvas __instance)
            {

                if (!Config.GetValue(ENABLED)) return;

                if (__instance.Slot.Name != CANVAS_SLOT_NAME)
                {
                    /*
                    Msg("Slot name of " + __instance.Slot.Name + " does not match the constant: " + "UIXCanvas");
                    Msg("Parent name: " + __instance.Slot.Parent.Name);

                    // Print a list of the child names as 1 line
                    string childNames = "";
                    foreach (Slot child in __instance.Slot.Children)
                    {
                        childNames += child.Name + ", ";
                    }
                    Msg("Child names: " + childNames);
                    */
                    return;
                }

                // Slot name matches the constant
                Msg("Matched with the slot name: " + __instance.Slot.Name);

                __instance.Slot.Name = "VeryCoolModded_" + __instance.Slot.Name;

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
                
                // For the count of the height constant, call contentSlot.AddSlot
                for (int i = 0; i < CANVAS_SLOT_HEIGHT; i++)
                {
                    Slot horizontalLayoutSlot = contentSlot.AddSlot("HorizontalLayout" + i);
                    horizontalLayoutSlot.AttachComponent<HorizontalLayout>();

                    // Add a slot for each column in the horizontal layout
                    for (int j = 0; j < CANVAS_SLOT_WIDTH; j++)
                    {
                        Slot slot = horizontalLayoutSlot.AddSlot("VerticalSlot" + j);
                    }
                }

                Msg("Created new HorizontalLayouts according to the height constant: " + 50);
            }
        }
    }
}
