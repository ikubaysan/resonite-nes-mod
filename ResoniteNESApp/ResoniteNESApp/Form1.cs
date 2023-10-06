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
        private int FPS = 24;
        // Add 1 to account for the count of pixels that have changed, which is always the 1st integer, written before the pixel data.
        // Then add FRAME_HEIGHT * 2 to account for pairs of 16-bit integers for identicalRowRanges
        private const int MemoryMappedFileSize = (((FRAME_WIDTH * FRAME_HEIGHT * 2) + 1) * sizeof(int)) + (FRAME_HEIGHT * 2 * sizeof(short));
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
        private List<(int Start, int End)> previousIdenticalRowRanges = new List<(int Start, int End)>();
        private List<(int Start, int End)> identicalRowRanges;
        private static List<(short EndIndex, short Span)> identicalRowRangesFromMMF = new List<(short, short)>();
        private Dictionary<int, List<int>> rgbToSpans; // Map RGB values to spans
        private int[] pixelData;



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

            Console.WriteLine("Form loaded with Timer Interval: " + _timer.Interval);
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
            pixelData = GeneratePixelDataFromFCEUX(FRAME_WIDTH, FRAME_HEIGHT, forceFullFrame);
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
            Console.WriteLine($"Identical Row Ranges from MMF: {string.Join("; ", identicalRowRangesFromMMF.Select(range => $"End Index: {range.EndIndex}, Span: {range.Span}"))}");

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

        // A helper function to make sure RGB values stay in the 0-255 range
        private int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
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

            // Adjusting brightness of the entire frame
            // A value of 1 means no change. Values > 1 increase brightness, and values < 1 decrease it.
            double brightnessFactor;
            if (!double.TryParse(textBox2.Text, out brightnessFactor) || brightnessFactor < 0 || brightnessFactor > 2.0)
            {
                // Handle the error case. For this example, we'll default to 1.0 if invalid or out of expected range
                brightnessFactor = 1.0;
            }

            if (brightnessFactor != 1.0)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color original = bmp.GetPixel(x, y);
                        int newRed = Clamp((int)(original.R * brightnessFactor), 0, 255);
                        int newGreen = Clamp((int)(original.G * brightnessFactor), 0, 255);
                        int newBlue = Clamp((int)(original.B * brightnessFactor), 0, 255);

                        bmp.SetPixel(x, y, Color.FromArgb(newRed, newGreen, newBlue));
                    }
                }
            }

            // Adding scanline effect
            if (checkBox2.Checked)
            {
                double darkenFactor;  // A value between 0 (no change) and 1 (fully black). Adjust as needed.
                if (!double.TryParse(textBox3.Text, out darkenFactor) || darkenFactor < 0 || darkenFactor > 1.0)
                {
                    // Handle the error case. For this example, we'll default to 0.0 if invalid or out of expected range
                    darkenFactor = 0.0;
                }


                if (darkenFactor > 0.0)
                {
                    for (int y = 0; y < bmp.Height; y += 2)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            Color original = bmp.GetPixel(x, y);
                            int newRed = (int)(original.R * (1 - darkenFactor));
                            int newGreen = (int)(original.G * (1 - darkenFactor));
                            int newBlue = (int)(original.B * (1 - darkenFactor));

                            bmp.SetPixel(x, y, Color.FromArgb(newRed, newGreen, newBlue));
                        }
                    }
                }
            }
            return bmp;
        }




        private int PackXYZ(int x, int y, int z)
        {
            return 1000000000 + x * 1000000 + y * 1000 + z;
        }

        static void UnpackXYZ(Int32 packedXYZ, out int X, out int Y, out int Z)
        {
            X = (packedXYZ / 1000000) % 1000;
            Y = (packedXYZ / 1000) % 1000;
            Z = packedXYZ % 1000;
        }

        private int[] GeneratePixelDataFromFCEUX(int width, int height, bool forceFullFrame)
        {
            const int MIN_SPAN_LENGTH = 3;

            Bitmap bmp = CaptureFCEUXWindow();
            if (bmp == null)
            {
                Console.WriteLine("emulator window not found");
                return null;
            }

            List<int> pixelDataList = new List<int>();
            rgbToSpans = new Dictionary<int, List<int>>(); // Map RGB values to spans

            List<Color> previousRowPixels = new List<Color>();
            List<Color> currentRowPixels = new List<Color>();

            // Start and End are both inclusive
            identicalRowRanges = new List<(int Start, int End)>();
            int? startIdenticalRowIndex = null;

            // Create a set of rows that were previously in a contiguous identical range
            var rowsPreviouslyInContiguousRange = new HashSet<int>();
            foreach (var range in previousIdenticalRowRanges)
            {
                for (int y = range.Start; y <= range.End; y++)
                {
                    rowsPreviouslyInContiguousRange.Add(y);
                }
            }

            // First loop to identify identical rows
            for (int y = 0; y < height; y++)
            {
                currentRowPixels.Clear();

                for (int x = 0; x < width; x++)
                {
                    currentRowPixels.Add(bmp.GetPixel(x, y));
                }

                if (currentRowPixels.SequenceEqual(previousRowPixels))
                {
                    if (!startIdenticalRowIndex.HasValue)
                    {
                        startIdenticalRowIndex = y - 1;
                    }
                }
                else if (startIdenticalRowIndex.HasValue)
                {
                    int spanLength = y - startIdenticalRowIndex.Value;
                    if (spanLength >= MIN_SPAN_LENGTH)
                    {
                        identicalRowRanges.Add((startIdenticalRowIndex.Value, y - 1));
                    }
                    startIdenticalRowIndex = null;
                }

                var temp = previousRowPixels;
                previousRowPixels = currentRowPixels;
                currentRowPixels = temp;
            }
            if (startIdenticalRowIndex.HasValue && height - startIdenticalRowIndex.Value >= MIN_SPAN_LENGTH)
            {
                identicalRowRanges.Add((startIdenticalRowIndex.Value, height - 1));
            }

            // Remove rows from the set that are still in a contiguous identical range
            foreach (var range in identicalRowRanges)
            {
                for (int y = range.Start; y <= range.End; y++)
                {
                    rowsPreviouslyInContiguousRange.Remove(y);
                }
            }

            // Print the ranges of contiguous identical rows
            int totalCount = 0;
            foreach (var range in identicalRowRanges)
            {
                totalCount += (range.End - range.Start + 1);
            }
            
            /*
            Console.Write(totalCount + " contiguous identical rows in ranges: ");
            foreach (var range in identicalRowRanges)
            {
                Console.Write("[" + range.Start + "-" + range.End + "] ");
            }
            Console.Write("\n");
            */

            // Print how many rows we force refreshed if the count is > 0
            if (rowsPreviouslyInContiguousRange.Count > 0) Console.WriteLine(rowsPreviouslyInContiguousRange.Count + " rows to force refresh.");

            // Second loop for processing pixel data
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                bool shouldForceRefresh = forceFullFrame || rowsPreviouslyInContiguousRange.Contains(y);

                while (x < width)
                {
                    Color pixel = bmp.GetPixel(x, y);
                    Color currentPixel = _currentBitmap.GetPixel(x, y);

                    if (shouldForceRefresh || !currentPixel.Equals(pixel))
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

            // Compile the pixel data in the new format
            foreach (var kvp in rgbToSpans)
            {
                pixelDataList.Add(kvp.Key);
                pixelDataList.AddRange(kvp.Value);
                pixelDataList.Add(-kvp.Value.Last());
            }

            bmp.Dispose();

            // Update the previousIdenticalRowRanges
            previousIdenticalRowRanges = identicalRowRanges;

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
                    int packedxStartYSpan = readPixelData[i++];
                    UnpackXYZ(packedxStartYSpan, out int xStart, out int y, out int spanLength);
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
                    // Note: if something is accessing the MemoryMappedFile, this will open and not create a new one.
                    // Keep this in mind if you're experimenting with resizing the MemoryMappedFile. You'll want to change this to
                    // close the programs using it and temporarily change this to MemoryMappedFile.CreateNew() 
                    _memoryMappedFile = MemoryMappedFile.CreateOrOpen(MemoryMappedFileName, MemoryMappedFileSize);
                }

                using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write((Int32)DateTime.UtcNow.Millisecond);
                    writer.Write((Int32)pixelData.Length); // The amount of integers that are currently relevant

                    if (identicalRowRanges.Count == 0)
                    {
                        // We don't need to write any identicalRowRanges, so we'll just write a -1 to indicate that
                        // Then the reader will see that the 1st 16 bit int is negative and realize there are no identicalRowRanges
                        writer.Write((Int16)(-1));
                    }
                    else
                    {
                        // Writing identicalRowRanges as 16-bit signed integers
                        for (int i = 0; i < identicalRowRanges.Count; i++)
                        {
                            var range = identicalRowRanges[i];

                            Int16 endIndex = (Int16)range.End;
                            // The ranges are inclusive at both ends, so we need to add 1 to get the span
                            Int16 span = (Int16)(range.End - range.Start + 1);

                            writer.Write(endIndex);
                            if (i == identicalRowRanges.Count - 1) // Check if this is the last value
                            {
                                writer.Write((Int16)(-span));  // Write negative span as delimiter
                            }
                            else
                            {
                                writer.Write(span);
                            }
                        }
                    }

                    // Finally, write the pixel data
                    foreach (Int32 value in pixelData)
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

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(textBox4.Text, out int selectedFPS) && selectedFPS <= 60 && selectedFPS >= 1)
            {
                FPS = selectedFPS;
                _timer.Interval = (int)((1.0 / FPS) * 1000); // Update timer's interval here
                Console.WriteLine("FPS changed to " + FPS + " and Timer Interval set to " + _timer.Interval);
            }
        }
    }
}