using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace ResoniteNESApp
{
    public static class MemoryMappedFileManager
    {

        public static MemoryMappedFile _pixelDataMemoryMappedFile;
        public static MemoryMappedFile _clientRenderConfirmationMemoryMappedFile;
        public const string ClientRenderConfirmationMemoryMappedFileName = "ResoniteClientRenderConfirmation";
        public const int ClientRenderConfirmationMemoryMappedFileSize = sizeof(int);
        public static int latestReceivedFrameMillisecondsOffset = -1;
        private const int clientRenderConfirmedAfterSeconds = 5; // We will assume the client has rendered the frame after this many seconds have passed since the frame was published
        private const string PixelDataMemoryMappedFileName = "ResonitePixelData";
        public static MemoryMappedViewStream _pixelDataMemoryMappedViewStream = null;
        public static BinaryReader _pixelDataBinaryReader = null;
        // Add 3 to account for the 3 ints we write before the pixel data
        public static Int32 latestPublishedFrameMillisecondsOffset;
        public static int readPixelDataLength;
        public static DateTime _lastFrameTime = DateTime.MinValue;
        public static int[] contiguousRangePairs = new int[Form1.FRAME_WIDTH * Form1.FRAME_HEIGHT];
        public static int[] readPixelData = new int[Form1.FRAME_WIDTH * Form1.FRAME_HEIGHT];
        private static bool forceRefreshedFrameFromMMF;

        public static bool clientRenderConfirmed()
        {

            if ((DateTime.Now - _lastFrameTime).TotalSeconds >= clientRenderConfirmedAfterSeconds)
            {
                return true;
            }

            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(MemoryMappedFileManager.ClientRenderConfirmationMemoryMappedFileName))
                {
                    using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                    {
                        using (BinaryReader reader = new BinaryReader(stream))
                        {
                            int value = reader.ReadInt32();
                            return value == latestPublishedFrameMillisecondsOffset || value == -1;
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("The memory-mapped file " + MemoryMappedFileManager.ClientRenderConfirmationMemoryMappedFileName + " does not exist.");
            }
            catch (IOException e)
            {
                Console.WriteLine($"Error reading from the memory-mapped file " + MemoryMappedFileManager.ClientRenderConfirmationMemoryMappedFileName + ": { e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"An unexpected error occurred when attempting to read memory-mapped file " + MemoryMappedFileManager.ClientRenderConfirmationMemoryMappedFileName + ": { e.Message}");
            }
            return false;
        }


        public static void WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile()
        {
            if (_clientRenderConfirmationMemoryMappedFile == null)
            {
                _clientRenderConfirmationMemoryMappedFile = MemoryMappedFile.CreateOrOpen(ClientRenderConfirmationMemoryMappedFileName, ClientRenderConfirmationMemoryMappedFileSize);
            }
            using (MemoryMappedViewStream stream = _clientRenderConfirmationMemoryMappedFile.CreateViewStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(latestReceivedFrameMillisecondsOffset);
            }
        }

        public static void WritePixelDataToMemoryMappedFile(List<int> pixelData, List<int> contiguousRangePairs, int PixelDataMemoryMappedFileSize, bool forceRefreshedFrame)
        {
            try
            {
                if (_pixelDataMemoryMappedFile == null)
                {
                    // Note: if something is accessing the MemoryMappedFile, this will open and not create a new one.
                    // Keep this in mind if you're experimenting with resizing the MemoryMappedFile. You'll want to change this to
                    // close the programs using it and temporarily change this to MemoryMappedFile.CreateNew() 
                    _pixelDataMemoryMappedFile = MemoryMappedFile.CreateOrOpen(PixelDataMemoryMappedFileName, PixelDataMemoryMappedFileSize);
                }

                using (MemoryMappedViewStream stream = _pixelDataMemoryMappedFile.CreateViewStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write((short)0); // Initially set status to "not ready"

                    // We use the sign of the 1st 32-bit integer to indicate whether the frame is force refreshed or not, and also as a way to give the reader a way to know if the frame has changed.
                    latestPublishedFrameMillisecondsOffset = DateTime.UtcNow.Millisecond;
                    if (forceRefreshedFrame) latestPublishedFrameMillisecondsOffset = -latestPublishedFrameMillisecondsOffset;

                    writer.Write(latestPublishedFrameMillisecondsOffset);

                    // Write the count of contiguousRangePairs
                    Int32 contiguousRangePairsCount = contiguousRangePairs.Count;
                    writer.Write(contiguousRangePairsCount);

                    // Iterate over contiguousRangePairs with a for loop
                    foreach (Int16 value in contiguousRangePairs)
                    {
                        writer.Write(value);
                    }

                    writer.Write((Int32)pixelData.Count); // Write the count of pixelData, which is the amount of integers that are currently relevant

                    // Finally, write the pixel data
                    foreach (Int32 value in pixelData)
                    {
                        writer.Write(value);
                    }

                    stream.Seek(0, SeekOrigin.Begin); // Seek back to the beginning so we can...
                    writer.Write((short)1); // set status to "ready"
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing to MemoryMappedFile: " + ex.Message);
                return;
            }
        }

        public static void ReadPixelDataFromMemoryMappedFile()
        {
            try
            {
                if (_pixelDataMemoryMappedFile == null)
                {
                    Console.WriteLine("MemoryMappedFile not initialized");
                    readPixelDataLength = -1;
                    return;
                }

                using (MemoryMappedViewStream stream = _pixelDataMemoryMappedFile.CreateViewStream())
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    short status = reader.ReadInt16();
                    if (status == 0)
                    {
                        // The data is not ready yet
                        readPixelDataLength = -1;
                        return;
                    }

                    int millisecondsOffset = reader.ReadInt32();
                    if (millisecondsOffset == latestReceivedFrameMillisecondsOffset)
                    {
                        readPixelDataLength = -1;
                        return;
                    }

                    if (millisecondsOffset < 0)
                    {
                        // If the 1st 32-bit int is negative, that indicates that the frame is force refreshed
                        forceRefreshedFrameFromMMF = true;
                    }
                    else
                    {
                        forceRefreshedFrameFromMMF = false;
                    }

                    latestReceivedFrameMillisecondsOffset = millisecondsOffset;

                    // Read the count of contiguousRangePairs
                    int contiguousRangePairsCount = reader.ReadInt32();

                    // Now read the contiguousRangePairs, based on contiguousRangePairsCount
                    for (int i = 0; i < contiguousRangePairsCount; i++)
                    {
                        contiguousRangePairs[i] = reader.ReadInt16();
                    }

                    // Read the count of pixelData
                    readPixelDataLength = reader.ReadInt32();

                    // Now read the pixel data, based on readPixelDataLength
                    for (int i = 0; i < readPixelDataLength; i++)
                    {
                        readPixelData[i] = reader.ReadInt32();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from PixelData MemoryMappedFile: " + ex.Message);
                readPixelDataLength = -1;
            }
        }
    }
}
