﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ResoniteModLoader;
using System.Reflection;
using FrooxEngine;
using FrooxEngine.UIX;
using Elements.Core;
using System.IO.MemoryMappedFiles;
using System.IO;


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
        private static readonly ModConfigurationKey<int> CANVAS_SLOT_WIDTH = new ModConfigurationKey<int>("canvas_slot_width", "The width of the canvas slot", () => 256);
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> CANVAS_SLOT_HEIGHT = new ModConfigurationKey<int>("canvas_slot_height", "The height of the canvas slot", () => 240);
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
        [HarmonyPatch(typeof(Canvas), "FinishCanvasUpdate")]  // (many hits, but matched for once)

        class ReosoniteNESModPatcher
        {
            private static bool initialized = false;
            private static Canvas _latestCanvasInstance;
            private static MemoryMappedFile _memoryMappedFile;
            private const string MemoryMappedFileName = "ResonitePixelData";
            private static int latestFrameMillisecondsOffset;
            private static Image[][] imageComponentCache;
            private static int[] readPixelData;
            private static int readPixelDataLength = -1;
            private static MemoryMappedViewStream _memoryMappedViewStream;
            private static BinaryReader _binaryReader;
            private static Dictionary<int, colorX> colorCache = new Dictionary<int, colorX>();
            private static List<(short EndIndex, short Span)> identicalRowRangesFromMMF = new List<(short, short)>();
            private static int[] identincalRowIndices;


            static void Postfix(Canvas __instance)
            {
                if (!Config.GetValue(ENABLED)) return;
                if (__instance.Slot.Name != Config.GetValue(CANVAS_SLOT_NAME)) return;
                _latestCanvasInstance = __instance;


                if (!initialized)
                {
                    Msg("Canvas must be initialized");
                    try
                    { 
                        InitializeCanvas(__instance);
                    }
                    catch (Exception e)
                    {
                        Error("Failed to initialize canvas " + __instance.Slot.Name);
                        Error(e.ToString());
                        return;
                    }
                    initialized = true;
                }
            }

            static void InitializeCanvas(Canvas __instance)
            {
                // Retrieve the values of configuration keys at the time the method is called
                int canvasSlotWidth = Config.GetValue(CANVAS_SLOT_WIDTH);
                int canvasSlotHeight = Config.GetValue(CANVAS_SLOT_HEIGHT);
                string canvasSlotName = Config.GetValue(CANVAS_SLOT_NAME);

                // Slot name matches the constant
                Msg("Matched with the slot name: " + __instance.Slot.Name);

                __instance.Slot.GetComponent<Canvas>().Size.Value = new float2(canvasSlotWidth, canvasSlotHeight);
                Msg("Set the size of the canvas to: " + __instance.Slot.GetComponent<Canvas>().Size.Value);

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

                // Initialize the cache
                imageComponentCache = new Image[canvasSlotHeight][];

                // For the count of the height constant, call contentSlot.AddSlot
                for (int i = 0; i < canvasSlotHeight; i++)
                {
                    Slot horizontalLayoutSlot = contentSlot.AddSlot("HorizontalLayout" + i);
                    horizontalLayoutSlot.AttachComponent<RectTransform>();
                    HorizontalLayout horizontalLayoutComponent = horizontalLayoutSlot.AttachComponent<HorizontalLayout>();
                    horizontalLayoutComponent.PaddingTop.Value = i;
                    horizontalLayoutComponent.PaddingBottom.Value = canvasSlotHeight - i - 1;

                    // Create new slots for each column in the horizontal layout and add them to the cache
                    imageComponentCache[i] = new Image[canvasSlotWidth];

                    // Add a slot for each column in the horizontal layout
                    for (int j = 0; j < canvasSlotWidth; j++)
                    {
                        Slot verticalSlot = horizontalLayoutSlot.AddSlot("VerticalSlot" + j);
                        verticalSlot.AttachComponent<RectTransform>();
                        Image imageComponent = verticalSlot.AttachComponent<Image>();
                        imageComponentCache[i][j] = imageComponent;
                        // Set the tint to a random color
                        imageComponent.Tint.Value = new colorX(
                            (float)rand.NextDouble(),
                            (float)rand.NextDouble(),
                            (float)rand.NextDouble(),
                            1);
                    }
                }
                Msg("Created new HorizontalLayouts according to the height constant: " + canvasSlotHeight);
                readPixelData = new int[Config.GetValue(CANVAS_SLOT_WIDTH) * Config.GetValue(CANVAS_SLOT_HEIGHT)];
                _memoryMappedFile = MemoryMappedFile.OpenExisting(MemoryMappedFileName);
                _memoryMappedViewStream = _memoryMappedFile.CreateViewStream();
                _binaryReader = new BinaryReader(_memoryMappedViewStream);
                Msg("_memoryMappedFile has been newly initialized with " + MemoryMappedFileName);
            }


            static void SetPixelDataToCanvas(Canvas __instance)
            {
                int i = 0;
                int packedRGB;
                float R, G, B;
                int packedXYZ, xStart, y, spanLength;
                int x, xEnd;
                colorX cachedColor;

                while (i < readPixelDataLength)
                {
                    packedRGB = readPixelData[i++];

                    // Check if we already have this RGB value cached
                    if (!colorCache.TryGetValue(packedRGB, out cachedColor))
                    {
                        // If not, create and cache it
                        R = ((packedRGB / 1000000) % 1000) / 1000f;
                        G = ((packedRGB / 1000) % 1000) / 1000f;
                        B = (packedRGB % 1000) / 1000f;
                        //cachedColor = new colorX(R + 0.01f, G + 0.01f, B + 0.01f, 1);
                        cachedColor = new colorX(R, G, B, 1, ColorProfile.Linear);
                        colorCache[packedRGB] = cachedColor;
                    }

                    while (i < readPixelDataLength && readPixelData[i] >= 0)
                    {
                        packedXYZ = readPixelData[i++];
                        xStart = (packedXYZ / 1000000) % 1000;
                        y = (packedXYZ / 1000) % 1000;

                        //Same as: xEnd = xStart + spanLength;
                        xEnd = ((packedXYZ / 1000000) % 1000) + (packedXYZ % 1000);

                        for (x = xStart; x < xEnd; x++)
                        {
                            imageComponentCache[y][x].Tint.Value = cachedColor;
                        }
                    }
                    i++; // Skip the negative delimiter. We've hit a new color.
                }
            }

            static void ReadFromMemoryMappedFile()
            {
                try
                {
                    if (_binaryReader == null)
                    {
                        Console.WriteLine("Binary reader not initialized");
                        readPixelDataLength = -1;
                        return;
                    }

                    int millisecondsOffset = _binaryReader.ReadInt32();
                    if (millisecondsOffset == latestFrameMillisecondsOffset)
                    {
                        readPixelDataLength = -1;
                        return;
                    }
                    latestFrameMillisecondsOffset = millisecondsOffset;

                    readPixelDataLength = _binaryReader.ReadInt32();

                    // Read the pairs of 16-bit integers (identicalRowRanges)
                    identicalRowRangesFromMMF.Clear();

                    if (_binaryReader.ReadInt16() < 0)
                    {
                        // If the first 16-bit int is negative, that indicates that there are no identicalRowRanges
                    }
                    else
                    {
                        _memoryMappedViewStream.Position -= sizeof(short); // Rewind the stream by 2 bytes, so we don't have to store the 1st 16 bit int
                        while (true)
                        {
                            short endIndex = _binaryReader.ReadInt16();


                            if (endIndex > 999)
                            { 
                                // Something went wrong. This usually happens when I'm alt-tabbing.
                                // I'm not sure why this happens, but I'm guessing it's because the MMF is not being updated.
                                // Raise an exception so we read from the MMF again.
                                throw new Exception("endIndex > 999");
                            }

                            short span = _binaryReader.ReadInt16();

                            if (span < 0)  // Negative span indicates the end of the range list
                            {
                                span = (short)-span;  // Convert span back to positive
                                identicalRowRangesFromMMF.Add((endIndex, span));
                                break;
                            }
                            else
                            {
                                identicalRowRangesFromMMF.Add((endIndex, span));
                            }
                        }
                    }

                    // Now read the pixel data, based on readPixelDataLength
                    for (int i = 0; i < readPixelDataLength; i++)
                    {
                        readPixelData[i] = _binaryReader.ReadInt32();
                    }

                    _memoryMappedViewStream.Position = 0;
                }
                catch (Exception ex)
                {
                    Error("Error reading from MemoryMappedFile: " + ex.Message);
                    readPixelDataLength = -1;
                    _memoryMappedViewStream.Position = 0;
                }
            }


            [HarmonyPatch(typeof(FrooxEngine.Animator), "OnCommonUpdate")]
            public static class AnimatorOnCommonUpdatePatcher
            {
                public static void Postfix()
                {
                    if (!initialized || _latestCanvasInstance == null) return;


                    if (readPixelDataLength == -1 && Config.GetValue(ENABLED))
                    {
                        ReadFromMemoryMappedFile();
                        Msg($"Identical Row Ranges from MMF: {string.Join("; ", identicalRowRangesFromMMF.Select(range => $"End Index: {range.EndIndex}, Span: {range.Span}"))}");

                        identincalRowIndices = new int[Config.GetValue(CANVAS_SLOT_HEIGHT)];

                        foreach (var range in identicalRowRangesFromMMF)
                        {
                            int startIndex = range.EndIndex - range.Span + 1;
                            for (int i = startIndex; i <= range.EndIndex; i++)
                            {
                                identincalRowIndices[i] = 1;
                            }
                        }

                        // Print identincalRowIndices
                        Msg($"identincalRowIndices: {string.Join("; ", identincalRowIndices)}");

                        return;
                    }

                    try
                    {
                        SetPixelDataToCanvas(_latestCanvasInstance);
                    }
                    catch (Exception e)
                    {
                        Error("Failed to update frame for initialized canvas " + _latestCanvasInstance.Slot.Name);
                        Error(e.ToString());
                        initialized = false;
                        Error("Set initialized to false.");
                        readPixelDataLength = -1;
                        return;
                    }
                    readPixelDataLength = -1;
                    return;
                }
            }
        }
    }
}