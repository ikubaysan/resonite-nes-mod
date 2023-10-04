using System;
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

        // Move the P/Invoke methods and structures here
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
        private const int MemoryMappedFileSize = ((FRAME_WIDTH * FRAME_HEIGHT * 5) + 100) * sizeof(int);
        private MemoryMappedFile _memoryMappedFile;
        private Bitmap _currentBitmap = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT);
        private const int FULL_FRAME_INTERVAL = 5000; // 5 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;

        public Form1()
        {
            InitializeComponent();
            _random = new Random();
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



        private int PackRGB(int r, int g, int b)
        {
            return 1000000000 + r * 1000000 + g * 1000 + b;
        }


        private (int R, int G, int B) UnpackRGB(int packedRGB)
        {
            int b = packedRGB % 1000;
            int g = (packedRGB / 1000) % 1000;
            int r = (packedRGB / 1000000) % 1000;
            return (r, g, b);
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
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color pixel = bmp.GetPixel(x, y);
                    Color currentPixel = _currentBitmap.GetPixel(x, y);
                    if (forceFullFrame || !currentPixel.Equals(pixel))
                    {
                        int packedRGB = PackRGB(pixel.R, pixel.G, pixel.B);
                        pixelData.Add(x);
                        pixelData.Add(y);
                        pixelData.Add(packedRGB);  // Add packed RGB value
                    }
                }
            }

            bmp.Dispose();
            return pixelData;
        }


        // Convert pixel data into a Bitmap
        private Bitmap SetPixelDataToBitmap(List<int> pixelData, int width, int height)
        {
            int updates = 0;
            for (int i = 0; i < pixelData.Count - 1; i += 3)  // Changed 5 to 3 because each pixel is now represented by 3 data points (x, y, packedRGB)
            {
                int x = pixelData[i];
                int y = pixelData[i + 1];
                var (R, G, B) = UnpackRGB(pixelData[i + 2]);  // Unpack RGB from the packed value
                Color newPixelColor = Color.FromArgb(R, G, B);
                _currentBitmap.SetPixel(x, y, newPixelColor);
                updates++;
            }
            Console.WriteLine(updates + " pixels changed since previous frame.");
            return _currentBitmap; // Return the updated bitmap.
        }

        private void WriteToMemoryMappedFile(List<int> pixelData)
        {
            try
            {
                if (_memoryMappedFile == null)
                {
                    _memoryMappedFile = MemoryMappedFile.CreateNew(MemoryMappedFileName, MemoryMappedFileSize);
                }

                using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    // Write the count of pixels first.
                    writer.Write(pixelData.Count / 3);  // Because each pixel has 3 data points (x, y, packedRGB)

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
            int latestIndex = 0;
            try
            {
                if (_memoryMappedFile != null)
                {
                    using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        // Read the count of pixels that have changed.
                        int changedPixelsCount = reader.ReadInt32();

                        // Considering each pixel has 3 data points (x, y, packedRGB)
                        int dataToRead = changedPixelsCount * 3;

                        for (int i = 0; i < dataToRead; i++)
                        {
                            pixelData.Add(reader.ReadInt32());
                            latestIndex++;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("MemoryMappedFile has not been created yet.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
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