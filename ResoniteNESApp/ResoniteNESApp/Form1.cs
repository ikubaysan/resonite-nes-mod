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
        private const int FRAME_WIDTH = 256;
        private const int FRAME_HEIGHT = 240;
        private int FPS = 24;
        // Add 3 to account for the 3 ints we write before the pixel data
        private const int PixelDataMemoryMappedFileSize = ((FRAME_WIDTH * FRAME_HEIGHT * 2) + 3) * sizeof(int);
        private MemoryMappedFile _pixelDataMemoryMappedFile;
        private Bitmap _currentBitmap = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT);
        private int fullFrameInterval = 10 * 1000; // 10 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private DateTime programStartTime;
        int[] readPixelData = new int[FRAME_WIDTH * FRAME_HEIGHT];
        private int readPixelDataLength;
        private Dictionary<int, List<int>> rgbToSpans; // Map RGB values to spans
        private int[] pixelData;

        private const string PixelDataMemoryMappedFileName = "ResonitePixelData";
        private static MemoryMappedViewStream _pixelDataMemoryMappedViewStream = null;
        private static BinaryReader _pixelDataBinaryReader = null;
        private Int32 latestPublishedFrameMillisecondsOffset;

        // Variables for mocking mod
        private static bool forceRefreshedFrameFromMMF;
        private int latestReceivedFrameMillisecondsOffset = -1;
        private const string ClientRenderConfirmationMemoryMappedFileName = "ResoniteClientRenderConfirmation";
        private const int ClientRenderConfirmationMemoryMappedFileSize = sizeof(int);
        private MemoryMappedFile _clientRenderConfirmationMemoryMappedFile;
        private const int clientRenderConfirmedAfterSeconds = 5; // We will assume the client has rendered the frame after this many seconds have passed since the frame was published

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


        /// <summary>
        /// Returns true if the client has confirmed that it has rendered the latest frame which this program has published.
        /// </summary>
        /// <returns></returns>
        private bool clientRenderConfirmed()
        {

            if ((DateTime.Now - _lastFrameTime).TotalSeconds >= clientRenderConfirmedAfterSeconds)
            {
                return true;
            }

            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(ClientRenderConfirmationMemoryMappedFileName))
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
                Console.WriteLine("The memory-mapped file " + ClientRenderConfirmationMemoryMappedFileName + " does not exist.");
            }
            catch (IOException e)
            {
                Console.WriteLine($"Error reading from the memory-mapped file " + ClientRenderConfirmationMemoryMappedFileName + ": { e.Message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"An unexpected error occurred when attempting to read memory-mapped file " + ClientRenderConfirmationMemoryMappedFileName + ": { e.Message}");
            }
            return false;
        }


        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!checkBox1.Checked) return;

            if (checkBox3.Checked && !(clientRenderConfirmed())) return;

            if (int.TryParse(textBox6.Text, out int selectedFullFrameInterval) && selectedFullFrameInterval >= 1)
                fullFrameInterval = selectedFullFrameInterval * 1000;

            bool forceFullFrame = false;
            if ((DateTime.Now - _lastFullFrameTime).TotalMilliseconds >= fullFrameInterval)
            {
                forceFullFrame = true;
                _lastFullFrameTime = DateTime.Now;
            }
            _lastFrameTime = DateTime.Now;

            // Generate pixel data
            pixelData = GeneratePixelDataFromFCEUX(FRAME_WIDTH, FRAME_HEIGHT, forceFullFrame);
            if (pixelData == null) return;

            // Write to MemoryMappedFile
            WritePixelDataToMemoryMappedFile(pixelData, forceFullFrame);

            // Read from MemoryMappedFile
            if (_pixelDataMemoryMappedViewStream == null)
            {
                _pixelDataMemoryMappedViewStream = _pixelDataMemoryMappedFile.CreateViewStream();
                _pixelDataBinaryReader = new BinaryReader(_pixelDataMemoryMappedViewStream);
            }

            ReadPixelDataFromMemoryMappedFile();
            if (readPixelData == null) return;

            if (checkBox5.Checked)
            {
                // Preview is enabled, so convert pixel data to Bitmap and set to PictureBox
                pictureBox1.Image = SetPixelDataToBitmap(FRAME_WIDTH, FRAME_HEIGHT);
            }

            if (checkBox4.Checked)
            {
                WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile();
                Console.WriteLine("Confirmation of render from server is enabled, so called WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile()");
            }
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

            Bitmap bmp = CaptureFCEUXWindow();
            if (bmp == null)
            {
                Console.WriteLine("emulator window not found");
                return null;
            }

            List<int> pixelDataList = new List<int>();
            rgbToSpans = new Dictionary<int, List<int>>(); // Map RGB values to spans

            // Processing pixel data
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

            // Compile the pixel data in the new format
            foreach (var kvp in rgbToSpans)
            {
                pixelDataList.Add(kvp.Key);
                pixelDataList.AddRange(kvp.Value);
                pixelDataList.Add(-kvp.Value.Last());
            }

            bmp.Dispose();

            return pixelDataList.ToArray();
        }

        // Convert pixel data into a Bitmap
        private Bitmap SetPixelDataToBitmap(int width, int height)
        {
            //StringBuilder logBuilder = new StringBuilder();
            //logBuilder.AppendFormat("{0}, {1};", latestReceivedFrameMillisecondsOffset, readPixelDataLength);
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
            return _currentBitmap;
        }

        private void WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile()
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

        private void WritePixelDataToMemoryMappedFile(int[] pixelData, bool forceRefreshedFrame)
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
                    writer.Write((Int32)pixelData.Length); // The amount of integers that are currently relevant

                    // Finally, write the pixel data
                    foreach (Int32 value in pixelData)
                    {
                        writer.Write(value);
                    }

                    stream.Seek(0, SeekOrigin.Begin); // Seek back to the beginning
                    writer.Write((short)1); // Set status to "ready"
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing to MemoryMappedFile: " + ex.Message);
                return;
            }
        }

        private void ReadPixelDataFromMemoryMappedFile()
        {
            try
            {
                if (_pixelDataBinaryReader == null)
                {
                    Console.WriteLine("Binary reader not initialized");
                    readPixelDataLength = -1;
                    return;
                }


                short status = _pixelDataBinaryReader.ReadInt16();
                if (status == 0) 
                {
                    // The data is not ready yet
                    readPixelDataLength = -1;
                    return;
                }

                int millisecondsOffset = _pixelDataBinaryReader.ReadInt32();
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

                readPixelDataLength = _pixelDataBinaryReader.ReadInt32();

                // Now read the pixel data, based on readPixelDataLength
                for (int i = 0; i < readPixelDataLength; i++)
                {
                    readPixelData[i] = _pixelDataBinaryReader.ReadInt32();
                }

                _pixelDataMemoryMappedViewStream.Position = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
                readPixelDataLength = -1;
                _pixelDataMemoryMappedViewStream.Position = 0;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Dispose the MemoryMappedFile
            _pixelDataMemoryMappedFile?.Dispose();
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            // We need to use an event handler because we have to update the timer's interval
            if (int.TryParse(textBox4.Text, out int selectedFPS) && selectedFPS <= 60 && selectedFPS >= 1)
            {
                FPS = selectedFPS;
                _timer.Interval = (int)((1.0 / FPS) * 1000); // Update timer's interval here
                Console.WriteLine("FPS changed to " + FPS + " and Timer Interval set to " + _timer.Interval);
            }
        }


    }
}