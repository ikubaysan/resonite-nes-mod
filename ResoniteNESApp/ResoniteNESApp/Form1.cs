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
            pictureBox1.Image = GenerateRandomImage(25, 25); 
        }

        private Bitmap GenerateRandomImage(int width, int height)
        { 
            Bitmap bmp = new Bitmap(width, height);
            for (int x = 0; x < width; x++)
            { 
                for (int y = 0; y < height; y++)
                {
                    Color randomColor = Color.FromArgb(
                        _random.Next(256), // R
                        _random.Next(256), // G
                        _random.Next(256) // B
                        );
                    bmp.SetPixel(x, y, randomColor);
                }
            }
            return bmp;
        }
    }
}
