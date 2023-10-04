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

namespace ResoniteNESApp
{
    public partial class Form1 : Form
    {
        private Timer _timer;
        private Random _random;
        private const string MemoryMappedFileName = "ResonitePixelData";
        private const int MemoryMappedFileSize = 25 * 25 * 5 * sizeof(int); // Assuming a maximum of 25x25 image and 5 ints per pixel
        private MemoryMappedFile _memoryMappedFile;

        public Form1()
        {
            InitializeComponent();
            _random = new Random();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _timer = new Timer();
            _timer.Interval = 1000; // 1 second
            _timer.Tick += Timer_Tick;
            _timer.Start();
            Console.WriteLine("Form loaded");
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!checkBox1.Checked) return;

            // Generate pixel data
            var pixelData = GenerateRandomPixelData(25, 25);

            // Write to MemoryMappedFile
            WriteToMemoryMappedFile(pixelData);

            // Read from MemoryMappedFile
            var readPixelData = ReadFromMemoryMappedFile();

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = ConvertPixelDataToBitmap(readPixelData, 25, 25);
        }

        // Generate random pixel data in the specified format:
        // [row index, column index, r value, g value, b value,
        //  row index, column index, r value, g value, b value, ...]
        private List<int> GenerateRandomPixelData(int width, int height)
        {
            var pixelData = new List<int>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    pixelData.Add(x); // row index
                    pixelData.Add(y); // column index
                    pixelData.Add(_random.Next(256)); // R
                    pixelData.Add(_random.Next(256)); // G
                    pixelData.Add(_random.Next(256)); // B
                }
            }
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