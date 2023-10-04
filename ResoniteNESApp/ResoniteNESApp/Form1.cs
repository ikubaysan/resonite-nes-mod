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
            for (int i = 0; i < pixelData.Count; i += 5)
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
                using (MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen(MemoryMappedFileName, MemoryMappedFileSize))
                using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    foreach (int value in pixelData)
                    {
                        writer.Write(value);
                    }

                    // Write validation
                    stream.Seek(0, SeekOrigin.Begin);  // Reset the stream position to the beginning

                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        for (int i = 0; i < pixelData.Count; i++)
                        {
                            int value = reader.ReadInt32();
                            if (value != pixelData[i])
                            {
                                Console.WriteLine($"Mismatch at position {i}. Expected: {pixelData[i]}, Found: {value}");
                            }
                            else
                            {
                                //Console.WriteLine("Matched!!");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle the exception, maybe log it or show a message to the user
                Console.WriteLine("Error writing to MemoryMappedFile: " + ex.Message);
                return;
            }
            Console.WriteLine("Successuflly wrote to MemoryMappedFile");
        }


        private List<int> ReadFromMemoryMappedFile()
        {
            var pixelData = new List<int>();
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(MemoryMappedFileName))
                {
                    Console.WriteLine("Successfully opened MMF for reading.");
                    using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                    {
                        using (BinaryReader reader = new BinaryReader(stream))
                        {
                            while (stream.Position < stream.Length)
                            {
                                pixelData.Add(reader.ReadInt32());
                            }
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // MemoryMappedFile has not been created yet. Log or handle this scenario as needed.
                Console.WriteLine("MemoryMappedFile does not exist yet.");
            }
            catch (Exception ex)
            {
                // Handle other exceptions that might occur
                Console.WriteLine("Error reading from MemoryMappedFile: " + ex.Message);
            }
            return pixelData;
        }


    }
}