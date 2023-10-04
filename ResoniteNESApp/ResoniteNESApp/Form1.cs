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

namespace ResoniteNESApp
{
    public partial class Form1 : Form
    {
        private Timer _timer;
        private Random _random;

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
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!checkBox1.Checked) return;

            // Generate pixel data
            var pixelData = GenerateRandomPixelData(25, 25);

            // Convert pixel data to Bitmap and set to PictureBox
            pictureBox1.Image = ConvertPixelDataToBitmap(pixelData, 25, 25);
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
    }
}
