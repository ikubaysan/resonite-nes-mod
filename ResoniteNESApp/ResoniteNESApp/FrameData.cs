using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ResoniteNESApp
{
    internal class FrameData
    {

        private static Dictionary<int, List<int>> rgbToSpans; // Map RGB values to spans
        private static Bitmap _currentBitmap = new Bitmap(Form1.FRAME_WIDTH, Form1.FRAME_HEIGHT);
        private static Bitmap _simulatedCanvas = new Bitmap(Form1.FRAME_WIDTH, Form1.FRAME_HEIGHT);
        private static IntPtr cachedWindowHandle = IntPtr.Zero;
        private static string cachedWindowTitle = "";
        private static Dictionary<int, int> rowExpansionAmounts = new Dictionary<int, int>();
        private static List<int> skippedRows = new List<int>();


        static FrameData()
        {
            //initializeAllColors();
        }

        static Int32 GetIndexFromColor(Color color)
        {
            return color.R * 256 * 256 + color.G * 256 + color.B;
        }

        static Color GetColorFromIndex(int index)
        {
            int r = index / (256 * 256);
            int g = (index / 256) % 256;
            int b = index % 256;
            return Color.FromArgb(r, g, b);
        }



        // A helper function to make sure RGB values stay in the 0-255 range
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        static int PackXYZ(int x, int y, int z)
        {
            return 1000000000 + x * 1000000 + y * 1000 + z;
        }

        static void UnpackXYZ(Int32 packedXYZ, out int X, out int Y, out int Z)
        {
            X = (packedXYZ / 1000000) % 1000;
            Y = (packedXYZ / 1000) % 1000;
            Z = packedXYZ % 1000;
        }
        static int GetXFromPackedXYZ(Int32 packedXYZ)
        {
            return (packedXYZ / 1000000) % 1000;
        }

        static int GetYFromPackedXYZ(Int32 packedXYZ)
        {
            return (packedXYZ / 1000) % 1000;
        }

        static int GetZFromPackedXYZ(Int32 packedXYZ)
        {
            return packedXYZ % 1000;
        }

        static public (List<int>, List<int>) GeneratePixelDataFromWindow(string targetWindowTitle, int titleBarHeight, int width, int height, bool forceFullFrame, double brightnessFactor, bool scanlinesEnabled, double darkenFactor)
        {
            Bitmap bmp = CaptureWindow(targetWindowTitle, titleBarHeight, brightnessFactor, scanlinesEnabled, darkenFactor);
            if (bmp == null)
            {
                return (null, null);
            }

            List<int> pixelDataList = new List<int>();

            rgbToSpans = new Dictionary<int, List<int>>();

            Dictionary<int, List<int>> rowChanges = new Dictionary<int, List<int>>();
            List<int> contiguousIdenticalRows = new List<int>();

            // Use BitmapData for faster pixel access
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bmp.PixelFormat);
            BitmapData currentBmpData = _currentBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, _currentBitmap.PixelFormat);

            int bytesPerPixel = Image.GetPixelFormatSize(bmp.PixelFormat) / 8;
            byte[] bmpBytes = new byte[width * height * bytesPerPixel];
            byte[] currentBmpBytes = new byte[width * height * bytesPerPixel];

            Marshal.Copy(bmpData.Scan0, bmpBytes, 0, bmpBytes.Length);
            Marshal.Copy(currentBmpData.Scan0, currentBmpBytes, 0, currentBmpBytes.Length);

            int length = bmpBytes.Length;
            int stride = bmpData.Stride;
            int spanStart;

            List<int> previousRowChanges = null;
            List<int> contiguousSegmentStarts = new List<int>();

            for (int y = 0; y < height; y++)
            {

                if (skippedRows.Contains(y)) continue;

                List<int> currentRowChanges = new List<int>();

                for (int x = 0; x < width;)
                {
                    int offset = y * stride + x * bytesPerPixel;

                    Color pixel = Color.FromArgb(bmpBytes[offset + 2], bmpBytes[offset + 1], bmpBytes[offset]);
                    Color currentPixel = Color.FromArgb(currentBmpBytes[offset + 2], currentBmpBytes[offset + 1], currentBmpBytes[offset]);

                    if (forceFullFrame || currentPixel.R != pixel.R || currentPixel.G != pixel.G || currentPixel.B != pixel.B)
                    {
                        spanStart = x;

                        while (x < width && bmpBytes[offset + 2] == pixel.R && bmpBytes[offset + 1] == pixel.G && bmpBytes[offset] == pixel.B)
                        {
                            x++;
                            offset = y * stride + x * bytesPerPixel;
                        }

                        int spanLength = x - spanStart;
                        int packedXYZ = PackXYZ(spanStart, y, spanLength);
                        int RGBIndex = GetIndexFromColor(pixel);

                        if (!rgbToSpans.TryGetValue(RGBIndex, out var spanList))
                        {
                            spanList = new List<int>();
                            rgbToSpans[RGBIndex] = spanList;
                        }
                        spanList.Add(packedXYZ);
                        currentRowChanges.Add(packedXYZ);
                    }
                    else
                    {
                        x++;
                    }
                }

                rowChanges[y] = currentRowChanges;

                if (currentRowChanges.Count == 0)
                {
                    if (contiguousSegmentStarts.Count > 0)
                    {
                        contiguousIdenticalRows.AddRange(contiguousSegmentStarts);
                        contiguousSegmentStarts.Clear();
                    }
                }
                else if (previousRowChanges == null || !currentRowChanges.SequenceEqual(previousRowChanges))
                {
                    if (contiguousSegmentStarts.Count > 0)
                    {
                        contiguousIdenticalRows.AddRange(contiguousSegmentStarts);
                    }
                    contiguousSegmentStarts.Clear();
                    contiguousSegmentStarts.Add(y);
                }
                else
                {
                    contiguousSegmentStarts.Add(y);
                }

                previousRowChanges = currentRowChanges;
            }

            // Post processing the contiguousIdenticalRows list to obtain pairs
            List<int> skippedRowsCurrent = new List<int>();
            List<int> contiguousRangePairs = new List<int>();

            int? previousValue = null;
            int rowRangeEndIndex, span;
            spanStart = -1;
            foreach (int currentValue in contiguousIdenticalRows)
            {
                if (previousValue == null)
                {
                    spanStart = currentValue;
                }
                else if (currentValue - previousValue.Value > 1)
                {
                    rowRangeEndIndex = previousValue.Value;
                    span = rowRangeEndIndex - spanStart + 1;
                    contiguousRangePairs.Add(rowRangeEndIndex);
                    contiguousRangePairs.Add(span);
                    spanStart = currentValue;

                    // TODO: Take note for accuracy
                    for (int i = rowRangeEndIndex; i > rowRangeEndIndex - span; i--)
                    {
                        skippedRowsCurrent.Add(i);
                    }

                }
                previousValue = currentValue;
            }

            if (previousValue != null)
            {
                rowRangeEndIndex = previousValue.Value;
                span = previousValue.Value - spanStart + 1;
                contiguousRangePairs.Add(rowRangeEndIndex);
                contiguousRangePairs.Add(span);

                // TODO: Take note for accuracy
                for (int i = rowRangeEndIndex; i > rowRangeEndIndex - span; i--)
                {
                    skippedRowsCurrent.Add(i);
                }
            }

            // For the rows that are in skippedRows but not in skippedRowsCurrent, we need to "force refresh" them.
            // For those rows, we need to get the pixel values from _currentBitmap and compare them to bmp, get that pixel change data,
            // and add it to pixelDataList. There won't be any existing pixel change data for those rows because they were skipped.
            var rowsToForceRefresh = skippedRows.Except(skippedRowsCurrent).ToList();


            foreach (int y in rowsToForceRefresh)
            {
                List<int> forceRefreshRowChanges = new List<int>();

                for (int x = 0; x < width;)
                {
                    int offset = y * stride + x * bytesPerPixel;

                    Color pixel = Color.FromArgb(bmpBytes[offset + 2], bmpBytes[offset + 1], bmpBytes[offset]);
                    Color currentPixel = Color.FromArgb(currentBmpBytes[offset + 2], currentBmpBytes[offset + 1], currentBmpBytes[offset]);

                    if (currentPixel.R != pixel.R || currentPixel.G != pixel.G || currentPixel.B != pixel.B)
                    {
                        spanStart = x;
                        while (x < width && bmpBytes[offset + 2] == pixel.R && bmpBytes[offset + 1] == pixel.G && bmpBytes[offset] == pixel.B)
                        {
                            x++;
                            offset = y * stride + x * bytesPerPixel;
                        }

                        int spanLength = x - spanStart;
                        int packedXYZ = PackXYZ(spanStart, y, spanLength);
                        int RGBIndex = GetIndexFromColor(pixel);

                        if (!rgbToSpans.TryGetValue(RGBIndex, out var spanList))
                        {
                            spanList = new List<int>();
                            rgbToSpans[RGBIndex] = spanList;
                        }
                        spanList.Add(packedXYZ);
                        forceRefreshRowChanges.Add(packedXYZ);
                    }
                    else
                    {
                        x++;
                    }
                }
            }

            // Now write the pixel data to pixelDataList
            foreach (var kvp in rgbToSpans)
            {
                int RGBIndex = kvp.Key;
                pixelDataList.Add(RGBIndex);

                List<int> spanList = kvp.Value;

                pixelDataList.AddRange(spanList);
                pixelDataList.Add(-kvp.Value.Last());
            }

            // Print contiguous identical row indices
            Console.WriteLine("Contiguous identical row indices (" + contiguousIdenticalRows.Count + "): " + string.Join(", ", contiguousIdenticalRows));

            // Print the range pairs in one line
            Console.WriteLine("Contiguous row end and spans: (" + contiguousRangePairs.Count + "): " + string.Join(", ", contiguousRangePairs));

            bmp.UnlockBits(bmpData);
            _currentBitmap.UnlockBits(currentBmpData);

            _currentBitmap = bmp;

            skippedRows = skippedRowsCurrent;

            return (pixelDataList, contiguousRangePairs);
        }


        private static Bitmap CaptureWindow(string targetWindowTitle, int titleBarHeight, double brightnessFactor, bool scanlinesEnabled, double darkenFactor)
        {
            IntPtr hWnd = IntPtr.Zero;
            NativeMethods.RECT rect = new NativeMethods.RECT { Top = 0, Left = 0, Right = 0, Bottom = 0 };
            bool cachedRectSet = false;

            // Check if the targetWindowTitle is the same as the cachedWindowTitle
            if (cachedWindowTitle == targetWindowTitle)
            {
                hWnd = cachedWindowHandle;

                // Check if the cached hWnd is still valid (the window is still open)
                if (NativeMethods.GetWindowRect(hWnd, out rect))
                {
                    cachedRectSet = true;
                }
                else
                {
                    // Window is no longer open. Reset the cached handle and search again.
                    cachedWindowHandle = IntPtr.Zero;
                    cachedWindowTitle = "";
                    hWnd = NativeMethods.FindWindowByTitleSubstring(targetWindowTitle);
                }
            }
            else
            {
                hWnd = NativeMethods.FindWindowByTitleSubstring(targetWindowTitle);
            }

            // If a window handle was found, cache it for next time
            if (hWnd != IntPtr.Zero)
            {
                cachedWindowHandle = hWnd;
                cachedWindowTitle = targetWindowTitle;
            }
            else
            {
                Console.WriteLine("Window with title " + targetWindowTitle + " not found");
                return null;
            }

            if (!cachedRectSet) NativeMethods.GetWindowRect(hWnd, out rect);

            // Adjusting for the title bar and borders - these values are just placeholders
            int borderWidth = 8;

            int adjustedTop = rect.Top + titleBarHeight;
            int adjustedLeft = rect.Left + borderWidth;
            int adjustedRight = rect.Right - borderWidth;
            int adjustedBottom = rect.Bottom;

            int width = adjustedRight - adjustedLeft;
            int height = adjustedBottom - adjustedTop;

            Bitmap bmp = new Bitmap(Form1.FRAME_WIDTH, Form1.FRAME_HEIGHT, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmp);
            g.CopyFromScreen(adjustedLeft, adjustedTop, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);


            if (Form1.brightnessFactor != 1.0)
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
            if (scanlinesEnabled && darkenFactor > 0.0)
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
            return bmp;
        }

        public static int GetRowHeight(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= Form1.FRAME_HEIGHT)
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


        private static void SetRowHeight(int rowIndex, int rowHeight)
        {
            if (rowIndex < 0 || rowHeight < 1)
            {
                Console.WriteLine("Invalid row index or height.");
                return;
            }

            rowExpansionAmounts[rowIndex] = rowHeight;
        }


        private static void ApplyRowHeight(Bitmap bitmap, int rowIndex, int rowHeight)
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

        private static void ApplyRowHeights(Bitmap bitmap)
        {
            foreach (var row in rowExpansionAmounts)
                ApplyRowHeight(_simulatedCanvas, row.Key, row.Value);
        }

        public static Bitmap SetPixelDataToBitmap(int width, int height)
        {
            int i = 0;
            int nPixelsChanged = 0;
            while (i < MemoryMappedFileManager.readPixelDataLength)
            {
                int colorIndex = MemoryMappedFileManager.readPixelData[i++];

                while (i < MemoryMappedFileManager.readPixelDataLength && MemoryMappedFileManager.readPixelData[i] >= 0)
                {
                    int packedxStartYSpan = MemoryMappedFileManager.readPixelData[i++];
                    UnpackXYZ(packedxStartYSpan, out int xStart, out int y, out int spanLength);
                    for (int x = xStart; x < xStart + spanLength; x++)
                    {
                        Color newPixelColor = GetColorFromIndex(colorIndex);
                        _simulatedCanvas.SetPixel(x, y, newPixelColor);
                        nPixelsChanged++;
                    }
                }
                i++; // Skip the negative delimiter
            }


            for (int j = 0; j < Form1.FRAME_HEIGHT; j++)
            {
                SetRowHeight(j, 1);
            }

            
            for (i = 0; i < MemoryMappedFileManager.readContiguousRangePairsLength; i += 2)
            {
                int rowIndex = MemoryMappedFileManager.readContiguousRangePairs[i];
                int rowHeight = MemoryMappedFileManager.readContiguousRangePairs[i + 1];
                SetRowHeight(rowIndex, rowHeight);
            }
            

            //SetRowHeight(239, 50);
            ApplyRowHeights(_simulatedCanvas);

            return _simulatedCanvas;
        }
    }
}
