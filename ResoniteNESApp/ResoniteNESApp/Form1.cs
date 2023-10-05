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
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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
        private const int FPS = 24;
        // Add 1 to account for the count of pixels that have changed, which is always the 1st integer, written before the pixel data.
        private const int MemoryMappedFileSize = ((FRAME_WIDTH * FRAME_HEIGHT * 2) + 1) * sizeof(int);
        private MemoryMappedFile _memoryMappedFile;
        private Bitmap _currentBitmap = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT);
        private const int FULL_FRAME_INTERVAL = 10 * 1000; // 10 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;
        private DateTime programStartTime;
        private int latestFrameMillisecondsOffset;
        int[] readPixelData = new int[FRAME_WIDTH * FRAME_HEIGHT];
        private int readPixelDataLength;
        private static MemoryMappedViewStream _memoryMappedViewStream = null;
        private static BinaryReader _binaryReader = null;


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

            if (_memoryMappedViewStream == null)
            {
                _memoryMappedViewStream = _memoryMappedFile.CreateViewStream();
                _binaryReader = new BinaryReader(_memoryMappedViewStream);
            }

            ReadFromMemoryMappedFile();
            if (readPixelData == null) return;

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = SetPixelDataToBitmap(FRAME_WIDTH, FRAME_HEIGHT);
        }

        private IntPtr FindWindowByTitleSubstring(string titleSubstring)
        {
            IntPtr foundWindowHandle = IntPtr.Zero;
            EnumWindows((hWnd, lParam) => {
                int len = GetWindowTextLength(hWnd);
                if (len > 0)
                {
                    StringBuilder windowTitle = new StringBuilder(len + 1);
                    GetWindowText(hWnd, windowTitle, len + 1);
                    if (windowTitle.ToString().Contains(titleSubstring))
                    {
                        foundWindowHandle = hWnd;
                        return false; // Stop the enumeration
                    }
                }
                return true; // Continue the enumeration
            }, IntPtr.Zero);

            return foundWindowHandle;
        }


        private Bitmap CaptureFCEUXWindow()
        {
            IntPtr hWnd = FindWindowByTitleSubstring("FCEUX");

            if (hWnd == IntPtr.Zero)
            {
                Console.WriteLine("FCEUX window not found");
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

        static void UnpackXYZ(int packedXYZ, out int X, out int Y, out int Z)
        {
            X = (packedXYZ / 1000000) % 1000;
            Y = (packedXYZ / 1000) % 1000;
            Z = packedXYZ % 1000;
        }

        private int[] GeneratePixelDataFromFCEUX(int width, int height, bool forceFullFrame)
        {
            Bitmap bmp = CaptureFCEUXWindow();
            if (bmp == null)
            {
                Console.WriteLine("emulator window not found");
                return null;
            }

            List<int> pixelDataList = new List<int>();
            var rgbToSpans = new Dictionary<int, List<int>>(); // Map RGB values to spans

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

                        while (x < width && bmp.GetPixel(x, y).ToArgb() == pixel.ToArgb())
                        {
                            x++;
                        }
                        int spanLength = x - spanStart;

                        int packedXYZ = PackXYZ(spanStart, y, spanLength);

                        if (!rgbToSpans.ContainsKey(packedRGB))
                        {
                            rgbToSpans[packedRGB] = new List<int>();
                        }

                        rgbToSpans[packedRGB].Add(packedXYZ);
                    }
                    else
                    {
                        x++;
                    }
                }
            }

            // Now, compile the pixel data in the new format
            foreach (var kvp in rgbToSpans)
            {
                pixelDataList.Add(kvp.Key); // RGB value
                pixelDataList.AddRange(kvp.Value); // Spans
                pixelDataList.Add(-kvp.Value.Last()); // Negation as delimiter
            }

            bmp.Dispose();
            return pixelDataList.ToArray();
        }



        // Convert pixel data into a Bitmap
        private Bitmap SetPixelDataToBitmap(int width, int height)
        {
            int i = 0;
            int nPixelsChanged = 0;
            while (i < readPixelDataLength)
            {
                int packedRGB = readPixelData[i++];
                UnpackXYZ(packedRGB, out int R, out int G, out int B); // Unpack RGB

                while (i < readPixelDataLength && readPixelData[i] >= 0)
                {
                    UnpackXYZ(readPixelData[i++], out int xStart, out int y, out int spanLength);
                    for (int x = xStart; x < xStart + spanLength; x++)
                    {
                        Color newPixelColor = Color.FromArgb(R, G, B);
                        _currentBitmap.SetPixel(x, y, newPixelColor);
                        nPixelsChanged++;
                    }
                }
                i++; // Skip the negative delimiter
            }
            Console.WriteLine(nPixelsChanged + " pixels changed since previous frame. pixelData len: " + readPixelDataLength); 
            return _currentBitmap;
        }

        private void WriteToMemoryMappedFile(int[] pixelData)
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
                    writer.Write(DateTime.UtcNow.Millisecond);
                    writer.Write(pixelData.Length); // Now it's simply the amount of integers that are currently relevant

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


        private void ReadFromMemoryMappedFile()
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

                // Read all pixel data at once
                byte[] buffer = _binaryReader.ReadBytes(readPixelDataLength * sizeof(int));
                Buffer.BlockCopy(buffer, 0, readPixelData, 0, buffer.Length);
                _memoryMappedViewStream.Position = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
                readPixelDataLength = -1;
                _memoryMappedViewStream.Position = 0;
            }
        }



        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Dispose the MemoryMappedFile
            _memoryMappedFile?.Dispose();
        }
    }
}