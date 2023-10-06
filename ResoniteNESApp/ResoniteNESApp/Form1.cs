﻿using System;
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
        private Timer _timer;
        private Random _random;
        public const int FRAME_WIDTH = 256;
        public const int FRAME_HEIGHT = 240;
        private int FPS = 24;

        private const int PixelDataMemoryMappedFileSize = ((FRAME_WIDTH * FRAME_HEIGHT * 2) + 3) * sizeof(int);

        private int fullFrameInterval = 10 * 1000; // 10 seconds in milliseconds
        private DateTime _lastFullFrameTime = DateTime.MinValue;
        private DateTime programStartTime;
        private int[] pixelData;

        public static double brightnessFactor = 1.0;
        public double darkenFactor = 0.0;
        public bool scanlinesEnabled = true;


        // Variables for mocking mod

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

            if (checkBox3.Checked && !(MemoryMappedFileManager.clientRenderConfirmed())) return;

            if (int.TryParse(textBox6.Text, out int selectedFullFrameInterval) && selectedFullFrameInterval >= 1)
                fullFrameInterval = selectedFullFrameInterval * 1000;

            bool forceFullFrame = false;
            if ((DateTime.Now - _lastFullFrameTime).TotalMilliseconds >= fullFrameInterval)
            {
                forceFullFrame = true;
                _lastFullFrameTime = DateTime.Now;
            }
            MemoryMappedFileManager._lastFrameTime = DateTime.Now;

            // Generate pixel data
            pixelData = FrameData.GeneratePixelDataFromFCEUX(FRAME_WIDTH, FRAME_HEIGHT, forceFullFrame, brightnessFactor, scanlinesEnabled, darkenFactor);
            if (pixelData == null) return;

            // Write to MemoryMappedFile
            MemoryMappedFileManager.WritePixelDataToMemoryMappedFile(pixelData, PixelDataMemoryMappedFileSize, forceFullFrame);

            // Read from MemoryMappedFile
            if (MemoryMappedFileManager._pixelDataMemoryMappedViewStream == null)
            {
                MemoryMappedFileManager._pixelDataMemoryMappedViewStream = MemoryMappedFileManager._pixelDataMemoryMappedFile.CreateViewStream();
                MemoryMappedFileManager._pixelDataBinaryReader = new BinaryReader(MemoryMappedFileManager._pixelDataMemoryMappedViewStream);
            }

            MemoryMappedFileManager.ReadPixelDataFromMemoryMappedFile();
            if (MemoryMappedFileManager.readPixelData == null) return;

            if (checkBox5.Checked)
            {
                // Preview is enabled, so convert pixel data to Bitmap and set to PictureBox
                pictureBox1.Image = FrameData.SetPixelDataToBitmap(FRAME_WIDTH, FRAME_HEIGHT);
            }

            if (checkBox4.Checked)
            {
                MemoryMappedFileManager.WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile();
                Console.WriteLine("Confirmation of render from server is enabled, so called WriteLatestReceivedFrameMillisecondsOffsetToMemoryMappedFile()");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Dispose the MemoryMappedFile
            MemoryMappedFileManager._pixelDataMemoryMappedFile?.Dispose();
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

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            // Adjusting brightness of the entire frame
            // A value of 1 means no change. Values > 1 increase brightness, and values < 1 decrease it.
            // 
            if (!double.TryParse(textBox2.Text, out brightnessFactor) || brightnessFactor < 0 || brightnessFactor > 2.0) return;

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            // A value between 0 (no change) and 1 (fully black). Adjust as needed.
            if (!double.TryParse(textBox3.Text, out darkenFactor) || darkenFactor < 0 || darkenFactor > 1.0)
            {
                // Handle the error case. For this example, we'll default to 0.0 if invalid or out of expected range
                darkenFactor = 0.0;
            }
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                scanlinesEnabled = true;
                // Scanlines are enabled
                if (!double.TryParse(textBox3.Text, out darkenFactor) || darkenFactor < 0 || darkenFactor > 1.0)
                {
                    darkenFactor = 0.5;
                    textBox3.Text = "0.5";
                }
            }
            else
            {
                scanlinesEnabled = false;
                // Scanlines are disabled
                darkenFactor = 0.0;
                textBox3.Text = "0.0";
            }
        }
    }
}