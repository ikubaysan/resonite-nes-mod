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

    public static class NativeMethods
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
    }

    public partial class Form1 : Form
    {
        private Timer _timer;
        private Random _random;
        private const string MemoryMappedFileName = "ResonitePixelData";
        private const int FRAME_WIDTH = 256;
        private const int FRAME_HEIGHT = 240;
        private const int MemoryMappedFileSize = FRAME_WIDTH * FRAME_HEIGHT * 5 * sizeof(int);
        private MemoryMappedFile _memoryMappedFile;

        public Form1()
        {
            InitializeComponent();
            _random = new Random();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _timer = new Timer();
            _timer.Interval = 200; // 0.2 seconds
            _timer.Tick += Timer_Tick;
            _timer.Start();

            pictureBox1.Width = FRAME_WIDTH;
            pictureBox1.Height = FRAME_HEIGHT;

            Console.WriteLine("Form loaded");
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!checkBox1.Checked) return;

            // Generate pixel data
            var pixelData = GeneratePixelDataFromFCEUX(FRAME_WIDTH, FRAME_HEIGHT);
            if (pixelData == null) return;

            // Write to MemoryMappedFile
            WriteToMemoryMappedFile(pixelData);

            // Read from MemoryMappedFile
            var readPixelData = ReadFromMemoryMappedFile();

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = ConvertPixelDataToBitmap(readPixelData, FRAME_WIDTH, FRAME_HEIGHT);
        }


        private Bitmap CaptureFCEUXWindow()
        {
            IntPtr hWnd = NativeMethods.FindWindow(null, "FCEUX 2.1.4a: mario");

            if (hWnd == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.RECT rect;
            NativeMethods.GetWindowRect(hWnd, out rect);

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


        private List<int> GeneratePixelDataFromFCEUX(int width, int height)
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
                    pixelData.Add(x); // row index
                    pixelData.Add(y); // column index
                    pixelData.Add(pixel.R);
                    pixelData.Add(pixel.G);
                    pixelData.Add(pixel.B);
                }
            }

            bmp.Dispose();
            return pixelData;
        }

        // Convert pixel data into a Bitmap
        private Bitmap ConvertPixelDataToBitmap(List<int> pixelData, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height);
            // Because we're incrementing by 5, it's possible that we end up setting i to the last value of the list.
            // But anything beyond that would bring us out of bounds, so we use -1 to prevent this case.
            for (int i = 0; i < pixelData.Count - 1; i += 5)
            {
                int x = pixelData[i];
                int y = pixelData[i + 1];
                Color pixelColor = Color.FromArgb(
                    pixelData[i + 2], // R
                    pixelData[i + 3], // G
                    pixelData[i + 4]  // B
                );
                bmp.SetPixel(x, y, pixelColor);
            }
            return bmp;
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
            //Console.WriteLine("Successfully wrote to MemoryMappedFile");
        }



        private List<int> ReadFromMemoryMappedFile()
        {
            var pixelData = new List<int>();
            try
            {
                if (_memoryMappedFile != null)
                {
                    using (MemoryMappedViewStream stream = _memoryMappedFile.CreateViewStream())
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        while (stream.Position < stream.Length)
                        {
                            pixelData.Add(reader.ReadInt32());
                        }
                    }
                }
                else
                {
                    Console.WriteLine("MemoryMappedFile has not been created yet.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
            }
            return pixelData;
        }



    }
}