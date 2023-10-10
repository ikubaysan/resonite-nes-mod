using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResoniteNESApp;
using System.Drawing;
using System.Windows.Forms;



namespace ResoniteNESApp.Tests
{
    [TestClass]
    public class FrameDataTests
    {

        public static void ShowBitmap(Bitmap bitmap)
        {
            Form form = new Form();
            /*
            form.Width = bitmap.Width;
            form.Height = bitmap.Height;
            */

            form.Width = 800;
            form.Height = 600;

            PictureBox pictureBox = new PictureBox();
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.Image = bitmap;

            form.Controls.Add(pictureBox);
            form.ShowDialog();
        }


        [TestMethod]
        public void Test_GetColorFromIndex()
        {
            int index = 16711680;
            Color expectedColor = Color.FromArgb(255, 0, 0);

            Color resultColor = FrameData.GetColorFromIndex(index);

            Assert.AreEqual(expectedColor, resultColor);
        }

        [TestMethod]
        public void Test_GetIndexFromColor()
        {
            Color color = Color.FromArgb(255, 0, 0);
            int expectedIndex = 16711680;

            int resultIndex = FrameData.GetIndexFromColor(color);

            Assert.AreEqual(expectedIndex, resultIndex);
        }

        [TestMethod]
        public void TestBitmapDisplay()
        {
            Bitmap bitmap = new Bitmap(100, 100);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Red);
            }
            ShowBitmap(bitmap);
        }
    }
}