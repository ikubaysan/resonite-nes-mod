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
        // Add 1 to account for the count of pixels that have changed, which is always the 1st integer, written before the pixel data.
        // Then add FRAME_HEIGHT * 2 to account for pairs of 16-bit integers for identicalRowRanges
        private const int PixelDataMemoryMappedFileSize = (((FRAME_WIDTH * FRAME_HEIGHT * 2) + 1) * sizeof(int)) + (FRAME_HEIGHT * 2 * sizeof(short));
        private MemoryMappedFile _pixelDataMemoryMappedFile;
        private Bitmap _currentBitmap = new Bitmap(FRAME_WIDTH, FRAME_HEIGHT);
        private const int FULL_FRAME_INTERVAL = 10 * 1000; // 10 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;
        private DateTime programStartTime;
        int[] readPixelData = new int[FRAME_WIDTH * FRAME_HEIGHT];
        private int readPixelDataLength;
        private List<(int Start, int End)> previousIdenticalRowRanges = new List<(int Start, int End)>();
        private List<(int Start, int End)> identicalRowRanges;
        private Dictionary<int, List<int>> rgbToSpans; // Map RGB values to spans
        private int[] pixelData;

        private const string PixelDataMemoryMappedFileName = "ResonitePixelData";
        private static MemoryMappedViewStream _pixelDataMemoryMappedViewStream = null;
        private static BinaryReader _pixelDataBinaryReader = null;
        private Int32 latestPublishedFrameMillisecondsOffset;

        private static MemoryMappedViewStream _clientRenderConfirmationMemoryMappedViewStream = null;
        private static BinaryReader _clientRenderConfirmationMemoryMappedBinaryReader = null;


        // Variables for mocking mod
        private static List<(short EndIndex, short Span)> identicalRowRangesFromMMF = new List<(short, short)>();
        private static int[] isIdenticalRow;
        private static int[] isIdentincalRowRangeEndIndex;
        private static int[] identincalRowSpanByEndIndex;
        private static int identicalRowCount;
        private static bool forceRefreshedFrameFromMMF;
        private Dictionary<int, int> rowExpansionAmounts = new Dictionary<int, int>();
        private int minContiguousIdenticalRowSpan = 10;
        private int latestReceivedFrameMillisecondsOffset = -1;
        private const string ClientRenderConfirmationMemoryMappedFileName = "ResoniteClientRenderConfirmation";
        private const int ClientRenderConfirmationMemoryMappedFileSize = sizeof(int);
        private MemoryMappedFile _clientRenderConfirmationMemoryMappedFile;






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
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(ClientRenderConfirmationMemoryMappedFileName))
                {
                    using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                    {
                        using (BinaryReader reader = new BinaryReader(stream))
                        {
                            int value = reader.ReadInt32();
                            return value == latestPublishedFrameMillisecondsOffset;
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
            WritePixelDataToMemoryMappedFile(pixelData, forceFullFrame);

            // Read from MemoryMappedFile
            if (_pixelDataMemoryMappedViewStream == null)
            {
                _pixelDataMemoryMappedViewStream = _pixelDataMemoryMappedFile.CreateViewStream();
                _pixelDataBinaryReader = new BinaryReader(_pixelDataMemoryMappedViewStream);
            }

            ReadPixelDataFromMemoryMappedFile();
            //Console.WriteLine($"Identical Row Ranges from MMF: {string.Join("; ", identicalRowRangesFromMMF.Select(range => $"End Index: {range.EndIndex}, Span: {range.Span}"))}");

            if (readPixelData == null) return;

            isIdenticalRow = new int[FRAME_HEIGHT];
            isIdentincalRowRangeEndIndex = new int[FRAME_HEIGHT];
            identincalRowSpanByEndIndex = new int[FRAME_HEIGHT];
            identicalRowCount = 0;


            foreach (var range in identicalRowRangesFromMMF)
            {
                int startIndex = range.EndIndex - range.Span + 1;
                for (int i = startIndex; i <= range.EndIndex; i++)
                {
                    isIdenticalRow[i] = 1;
                    identicalRowCount++;
                }
                isIdentincalRowRangeEndIndex[range.EndIndex] = 1;
                identincalRowSpanByEndIndex[range.EndIndex] = range.Span;
            }

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = SetPixelDataToBitmap(FRAME_WIDTH, FRAME_HEIGHT);

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
            if (int.TryParse(textBox5.Text, out int selectedMinContiguousIdenticalRowSpan) && selectedMinContiguousIdenticalRowSpan > 1 && selectedMinContiguousIdenticalRowSpan < 10000)
                minContiguousIdenticalRowSpan = selectedMinContiguousIdenticalRowSpan;

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
                    if (spanLength >= minContiguousIdenticalRowSpan)
                    {
                        identicalRowRanges.Add((startIdenticalRowIndex.Value, y - 1));
                    }
                    startIdenticalRowIndex = null;
                }

                var temp = previousRowPixels;
                previousRowPixels = currentRowPixels;
                currentRowPixels = temp;
            }
            if (startIdenticalRowIndex.HasValue && height - startIdenticalRowIndex.Value >= minContiguousIdenticalRowSpan)
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

            // Print how many rows we will force refreshed if the count is > 0
            if (rowsPreviouslyInContiguousRange.Count > 0)
            { 
                //Console.WriteLine(rowsPreviouslyInContiguousRange.Count + " rows to force refresh.");
                //forceFullFrame = true;
            }

            // If a row is identical to another row, and is not the last row in the identical range, then we don't need to process it."
            HashSet<int> skipRows = new HashSet<int>();
            foreach (var range in identicalRowRanges)
            {
                for (int y = range.Start; y < range.End; y++) // Notice that we're going up to but not including the End
                {
                    skipRows.Add(y);
                }
            }

            // Second loop for processing pixel data
            for (int y = 0; y < height; y++)
            {
                // Check if the row is in the skipRows HashSet
                if (skipRows.Contains(y))
                {
                    continue; // Skip processing for this row
                }

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



        public int GetRowHeight(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= FRAME_HEIGHT)
            {
                Console.WriteLine("Invalid row index.");
                return -1;
            }

            if (rowExpansionAmounts.ContainsKey(rowIndex))
            {
                return rowExpansionAmounts[rowIndex];
            }
            else
            {
                return 1;
            }
        }


        public void SetRowHeight(int rowIndex, int rowHeight)
        {
            if (rowIndex < 0 || rowHeight < 1)
            {
                Console.WriteLine("Invalid row index or height.");
                return;
            }

            rowExpansionAmounts[rowIndex] = rowHeight;
        }


        private void ApplyRowHeight(Bitmap bitmap, int rowIndex, int rowHeight)
        {



            if (rowIndex < 0 || rowIndex >= bitmap.Height)
            {
                Console.WriteLine("Row index out of bounds.");
                return;
            }

            if (rowHeight < 1 || rowIndex - rowHeight < -1)
            {
                Console.WriteLine("Invalid row height or not enough rows below to set.");
                return;
            }

            // Expand the row upwards based on its height.
            // Greater rowIndex means lower on the screen.
            List<int> updatedRows = new List<int>();
            for (int y = rowIndex; y > rowIndex - rowHeight; y--)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixelColor = bitmap.GetPixel(x, rowIndex);
                    bitmap.SetPixel(x, y, pixelColor);
                }
                updatedRows.Add(y);
            }
            return;
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

                    
                    if (isIdenticalRow[y] == 1 && !forceRefreshedFrameFromMMF)
                    {
                        if (isIdentincalRowRangeEndIndex[y] != 1) continue;
                    }

                    for (int x = xStart; x < xStart + spanLength; x++)
                    {
                        Color newPixelColor = Color.FromArgb(R, G, B);
                        _currentBitmap.SetPixel(x, y, newPixelColor);
                        nPixelsChanged++;
                    }
                }
                i++; // Skip the negative delimiter
            }

            if (!forceRefreshedFrameFromMMF)
            {
                for (int j = 0; j < isIdentincalRowRangeEndIndex.Length; j++)
                {
                    if (isIdentincalRowRangeEndIndex[j] == 1)
                    {
                        int spanLength = identincalRowSpanByEndIndex[j];
                        if (GetRowHeight(j) != spanLength)
                        {
                            SetRowHeight(j, spanLength);
                            //Console.WriteLine("Set the span of the row at index " + j + " to expanded span " + spanLength);
                        }
                    }
                }
            }

            // Iterate over horizontalLayoutComponentCache and correct the padding top values
            for (int j = 0; j < FRAME_HEIGHT; j++)
            {
                if (forceRefreshedFrameFromMMF || (isIdenticalRow[j] != 1 && GetRowHeight(j) != 1))
                {
                    SetRowHeight(j, 1);
                    //Console.WriteLine("Reset the span of the row at index " + j + " to 1");
                }
            }

            Console.WriteLine(nPixelsChanged + " pixels updated since previous frame. pixelData len: " + readPixelDataLength);

            // After setting all pixels, apply the row expansions
            foreach (var row in rowExpansionAmounts)
            {
                ApplyRowHeight(_currentBitmap, row.Key, row.Value);
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
                    // We use the sign of the 1st 32-bit integer to indicate whether the frame is force refreshed or not, and also as a way to give the reader a way to know if the frame has changed.
                    latestPublishedFrameMillisecondsOffset = DateTime.UtcNow.Millisecond;
                    if (forceRefreshedFrame) latestPublishedFrameMillisecondsOffset = -latestPublishedFrameMillisecondsOffset;

                    writer.Write(latestPublishedFrameMillisecondsOffset);
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

                latestReceivedFrameMillisecondsOffset = _pixelDataBinaryReader.ReadInt32();
                if (latestReceivedFrameMillisecondsOffset < 0)
                {
                    // If the 1st 32-bit int is negative, that indicates that the frame is force refreshed
                    forceRefreshedFrameFromMMF = true;
                }
                else
                {
                    forceRefreshedFrameFromMMF = false;
                }

                readPixelDataLength = _pixelDataBinaryReader.ReadInt32();

                // Read the pairs of 16-bit integers (identicalRowRanges)
                identicalRowRangesFromMMF.Clear();

                if (_pixelDataBinaryReader.ReadInt16() < 0)
                {
                    // If the first 16-bit int is negative, that indicates that there are no identicalRowRanges
                }
                else
                {
                    _pixelDataMemoryMappedViewStream.Position -= sizeof(short); // Rewind the stream by 2 bytes, so we don't have to store the 1st 16 bit int
                    while (true)
                    {
                        short endIndex = _pixelDataBinaryReader.ReadInt16();


                        if (endIndex > 999)
                        {
                            // Something went wrong. This usually happens when I'm alt-tabbing.
                            // I'm not sure why this happens, but I'm guessing it's because the MMF is not being updated.
                            // Raise an exception so we read from the MMF again.
                            throw new Exception("endIndex > 999");
                        }

                        short span = _pixelDataBinaryReader.ReadInt16();

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