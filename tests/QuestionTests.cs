using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal static class QuestionTests
    {
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                foreach (string path in args)
                using (Bitmap image = new Bitmap(path))
                {
                    QuestionDetector detector = new QuestionDetector();
                    bool result = detector.CheckAsync(image).GetAwaiter().GetResult();
                    Console.WriteLine("Question screenshot detected: " + result + "; " + detector.Availability);
                    if (!result) return 1;
                }
                if (args.Length == 0)
                {
                    foreach (string action in new[] { "跳过", "提交" })
                    using (Bitmap image = new Bitmap(1600, 1184))
                    using (Graphics g = Graphics.FromImage(image))
                    using (Font font = new Font("Microsoft YaHei UI", 18))
                    {
                        g.Clear(Color.FromArgb(24,24,24));
                        g.FillRectangle(Brushes.DimGray, 545, 823, 920, 338);
                        g.DrawString("正在等待你的回答", font, Brushes.White, 575, 745);
                        g.DrawString("正在询问问题", font, Brushes.White, 575, 690);
                        g.DrawString("测试问题", font, Brushes.White, 565, 840);
                        g.DrawString("3 of 3", font, Brushes.White, 1330, 840);
                        g.DrawString("1  选项一（推荐）", font, Brushes.White, 565, 900);
                        g.DrawString(action, font, Brushes.White, 1390, 1110);
                        QuestionDetector detector = new QuestionDetector();
                        if (!detector.CheckAsync(image).GetAwaiter().GetResult()) throw new Exception("Question fixture " + action + " not detected: " + detector.Availability);
                        Console.WriteLine("PASS: OCR question fixture " + action);
                        detector.Reset();
                        g.Clear(Color.FromArgb(24,24,24));
                        IconButton.Draw(g, new Rectangle(1410, 1080, 64, 64), "三角形");
                        if (detector.CheckAsync(image).GetAwaiter().GetResult()) throw new Exception("Question mode did not clear after panel disappeared");
                        Console.WriteLine("PASS: disappearance clears question state");
                    }
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
