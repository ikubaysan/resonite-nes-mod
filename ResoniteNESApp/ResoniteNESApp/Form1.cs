﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.MemoryMappedFiles;
using System.IO;
using System.Runtime.InteropServices;


namespace ResoniteNESApp
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }


        private Timer _timer;
        private Random _random;
        private const string MemoryMappedFileName = "ResonitePixelData";
        private const int FRAME_WIDTH = 256;
        private const int FRAME_HEIGHT = 240;
        private const int FPS = 36;
        // Add 1 to account for the count of pixels that have changed, which is always the 1st integer, written before the pixel data.
        private const int MemoryMappedFileSize = ((FRAME_WIDTH * FRAME_HEIGHT * 2) + 1) * sizeof(int);
        private MemoryMappedFile _memoryMappedFile;
        private Bitmap _currentBitmap = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT);
        private const int FULL_FRAME_INTERVAL = 5000; // 5 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;
        private DateTime programStartTime;
        private int latestFrameMillisecondsOffset;

        public Form1()
        {
            InitializeComponent();
            _random = new Random();
            programStartTime = DateTime.Now;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _timer = new Timer();
            _timer.Interval = (int)((1.0 / FPS) * 1000);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            pictureBox1.Width = FRAME_WIDTH;
            pictureBox1.Height = FRAME_HEIGHT;

            Console.WriteLine("Form loaded");
        }


        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!checkBox1.Checked) return;

            bool forceFullFrame = false;
            if ((DateTime.Now - _lastFullFrameTime).TotalMilliseconds >= FULL_FRAME_INTERVAL)
            {
                forceFullFrame = true;
                _lastFullFrameTime = DateTime.Now;
            }

            // Generate pixel data
            var pixelData = GeneratePixelDataFromFCEUX(FRAME_WIDTH, FRAME_HEIGHT, forceFullFrame);
            if (pixelData == null) return;

            // Write to MemoryMappedFile
            WriteToMemoryMappedFile(pixelData);

            // Read from MemoryMappedFile
            var readPixelData = ReadFromMemoryMappedFile();
            if (readPixelData == null) return;

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = SetPixelDataToBitmap(readPixelData, FRAME_WIDTH, FRAME_HEIGHT);
        }


        private Bitmap CaptureFCEUXWindow()
        {
            IntPtr hWnd = FindWindow(null, "FCEUX 2.1.4a: mario");

            if (hWnd == IntPtr.Zero)
            {
                return null;
            }

            RECT rect;
            GetWindowRect(hWnd, out rect);

            // Adjusting for the title bar and borders - these values are just placeholders
            int titleBarHeight = 30;
            int borderWidth = 8;

            int adjustedTop = rect.Top + titleBarHeight;
            int adjustedLeft = rect.Left + borderWidth;
            int adjustedRight = rect.Right - borderWidth;
            int adjustedBottom = rect.Bottom;

            int width = adjustedRight - adjustedLeft;
            int height = adjustedBottom - adjustedTop;

            Bitmap bmp = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmp);
            g.CopyFromScreen(adjustedLeft, adjustedTop, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            return bmp;
        }




        private int PackXYZ(int x, int y, int z)
        {
            return 1000000000 + x * 1000000 + y * 1000 + z;
        }

        private void UnpackXYZ(int packedXYZ, out int X, out int Y, out int Z)
        {
            Z = packedXYZ % 1000;
            Y = (packedXYZ / 1000) % 1000;
            X = (packedXYZ / 1000000) % 1000;
        }



        private List<int> GeneratePixelDataFromFCEUX(int width, int height, bool forceFullFrame)
        {
            Bitmap bmp = CaptureFCEUXWindow();
            if (bmp == null)
            {
                Console.WriteLine("emulator window not found");
                return null;
            }

            var pixelData = new List<int>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    Color pixel = bmp.GetPixel(x, y);
                    Color currentPixel = _currentBitmap.GetPixel(x, y);

                    if (forceFullFrame || !currentPixel.Equals(pixel))
                    {
                        int spanStart = x;
                        int packedRGB = PackXYZ(pixel.R, pixel.G, pixel.B);

                        // Find the contiguous pixels with the same color
                        while (x < width && bmp.GetPixel(x, y).ToArgb() == pixel.ToArgb())
                        {
                            x++;
                        }
                        int spanLength = x - spanStart;

                        // Append data to the list
                        int packedXYZ = PackXYZ(spanStart, y, spanLength);
                        pixelData.Add(packedXYZ);  // Packed X, Y, and span
                        pixelData.Add(packedRGB);  // Packed RGB value
                    }
                    else
                    {
                        x++;
                    }
                }
            }

            bmp.Dispose();
            return pixelData;
        }


        // Convert pixel data into a Bitmap
        private Bitmap SetPixelDataToBitmap(List<int> pixelData, int width, int height)
        {
            int i;
            int nPixelsChanged = 0;
            for (i = 0; i < pixelData.Count - 1; i += 2) // RGB data of contiguous pixels is represented by 4 ints: (x, y, span, packedRGB)
            {

                UnpackXYZ(pixelData[i], out int xStart, out int y, out int spanLength);
                UnpackXYZ(pixelData[i + 1], out int R, out int G, out int B); // Unpack RGB from the packed value

                for (int x = xStart; x < xStart + spanLength; x++)
                {
                    Color newPixelColor = Color.FromArgb(R, G, B);
                    _currentBitmap.SetPixel(x, y, newPixelColor);
                    nPixelsChanged++;
                }
            }

            Console.WriteLine(nPixelsChanged + " pixels changed since previous frame. pixelData len: " + pixelData.Count);
            return _currentBitmap; // Return the updated bitmap.
        }

        private void WriteToMemoryMappedFile(List<int> pixelData)
        {
            try
            {
                if (_memoryMappedFile == null)
                {
                    _memoryMappedFile = MemoryMappedFile.CreateOrOpen(MemoryMappedFileName, MemoryMappedFileSize);
                }

                using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    // Write the millisecondsOffset (milliseconds since the latest complete second)
                    writer.Write(DateTime.UtcNow.Millisecond);

                    // Write the count of pixels next.
                    writer.Write(pixelData.Count / 2); // RGB data of contiguous pixels is represented by 4 ints: (x, y, span, packedRGB)

                    // Then write the pixel data
                    foreach (int value in pixelData)
                    {
                        writer.Write(value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing to MemoryMappedFile: " + ex.Message);
                return;
            }
        }


        private List<int> ReadFromMemoryMappedFile()
        {
            var pixelData = new List<int>();
            try
            {
                using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // Read the millisecondsOffset.
                    int millisecondsOffset = reader.ReadInt32();
                    if (millisecondsOffset == latestFrameMillisecondsOffset) return null;

                    // Update the latestFrameMillisecondsOffset because it's different, meaning we got a new frame.
                    latestFrameMillisecondsOffset = millisecondsOffset;

                    // Read the count of pixels that have changed.
                    int changedPixelsCount = reader.ReadInt32();

                    // RGB data of contiguous pixels is represented by 4 ints: (x, y, span, packedRGB)
                    int dataToRead = changedPixelsCount * 2;

                    for (int i = 0; i < dataToRead; i++)
                    {
                        pixelData.Add(reader.ReadInt32());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
                _memoryMappedFile = null;
                return null;
            }
            return pixelData;
        }


        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Dispose the MemoryMappedFile
            _memoryMappedFile?.Dispose();
        }
    }
}