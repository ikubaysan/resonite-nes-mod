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
        private static readonly ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("enabled", "Should the mod be enabled", () => true); //Optional config settings

        private static ModConfiguration Config;//If you use config settings, this will be where you interface with them

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
        [HarmonyPatch(typeof(Canvas), "OnAttach")]
        class ReosoniteNESModPatcher
        {
            private const string CANVAS_SLOT_NAME = "UIXCanvas";
            
            /*
            static void Prefix(Canvas __instance)
            {
                if (__instance.Slot.Name != CANVAS_SLOT_NAME)
                {
                    Msg("Slot name of " + __instance.Slot.Name + " does not match the constant: " + CANVAS_SLOT_NAME);
                    return;
                }
                // Slot name matches the constant
                Msg("Matched with the slot name: " + __instance.Slot.Name);
            }
            */

            static void Postfix(Canvas __instance)
            {
                if (__instance.Slot.Name != CANVAS_SLOT_NAME)
                {
                    Msg("Slot name of " + __instance.Slot.Name + " does not match the constant: " + CANVAS_SLOT_NAME);
                    return;
                }

                // Slot name matches the constant
                Msg("Matched with the slot name: " + __instance.Slot.Name);

                __instance.Slot.Name = "VeryCoolModded_" + __instance.Slot.Name;

                Msg("Changed the slot name to: " + __instance.Slot.Name);
            }
        }
    }
}
