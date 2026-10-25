using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CodexAutoResume
{
    // Local Windows OCR only. Never retain question text, select an answer, or
    // submit a form. A single worker prevents slow OCR calls from accumulating.
    internal sealed class QuestionDetector
    {
        readonly object sync = new object();
        Task<bool> pending;
        DateTime nextCheck;
        int epoch;
        public bool Waiting { get; private set; }
        public string Availability { get; private set; }
        public static string PlatformSummary()
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            return engine == null ? "unavailable (no installed Windows OCR language); icon monitoring remains available" : "available (" + engine.RecognizerLanguage.LanguageTag + ")";
        }

        internal static bool Matches(string text, string bottomRight)
        {
            text = Regex.Replace(text ?? "", @"\s+", "").ToLowerInvariant();
            bottomRight = Regex.Replace(bottomRight ?? "", @"\s+", "").ToLowerInvariant();
            bool action = bottomRight.Contains("跳过") || bottomRight.Contains("提交") || bottomRight.Contains("skip") || bottomRight.Contains("submit");
            bool waiting = text.Contains("等待你的回答") || text.Contains("等待您的回答") || text.Contains("waitingforyouranswer") || text.Contains("waitingforyourresponse");
            bool pages = Regex.IsMatch(text, @"\d{1,2}[o0]f\d{1,2}");
            bool asking = text.Contains("正在询问问题") || text.Contains("askingquestions");
            return (asking && (waiting || pages)) || (action && (waiting || pages));
        }

        public void Reset() { epoch++; Waiting = false; nextCheck = DateTime.MinValue; }

        public async Task<bool> CheckAsync(Bitmap image)
        {
            if (DateTime.UtcNow < nextCheck) return Waiting;
            int revision = epoch;
            Task<bool> work;
            lock (sync)
            {
                if (pending != null) return Waiting;
                int left = (int)(image.Width * .30), top = (int)(image.Height * .40);
                Bitmap crop = image.Clone(new Rectangle(left, top, image.Width - left, image.Height - top), PixelFormat.Format32bppArgb);
                work = Task.Run(async delegate { using (crop) return await RecognizeAsync(crop); }); pending = work;
            }
            // Do not block the monitor indefinitely, or dispatch another OCR
            // operation behind an unresponsive Windows language service.
            Task winner = await Task.WhenAny(work, Task.Delay(2500));
            if (winner != work)
            {
                Task cleanup = work.ContinueWith(delegate(Task<bool> late) { if (late.IsFaulted) { var observed = late.Exception; } lock (sync) if (pending == late) pending = null; });
                nextCheck = DateTime.UtcNow.AddSeconds(3); return Waiting;
            }
            try { bool result = await work; if (revision == epoch) Waiting = result; }
            catch { if (revision == epoch) Waiting = false; Availability = "本机文字识别暂不可用，继续图标监测"; }
            finally { lock (sync) if (pending == work) pending = null; nextCheck = DateTime.UtcNow.AddSeconds(2); }
            return Waiting;
        }

        async Task<bool> RecognizeAsync(Bitmap crop)
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Availability = "未安装 Windows OCR 语言，继续图标监测"; return false; }
            Availability = "系统离线文字识别可用: " + engine.RecognizerLanguage.LanguageTag;
            BitmapData pixels = crop.LockBits(new Rectangle(Point.Empty, crop.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[crop.Width * crop.Height * 4];
            try { for (int y = 0; y < crop.Height; y++) Marshal.Copy(IntPtr.Add(pixels.Scan0, y * pixels.Stride), bytes, y * crop.Width * 4, crop.Width * 4); }
            finally { crop.UnlockBits(pixels); }
            using (DataWriter writer = new DataWriter())
            {
                writer.WriteBytes(bytes);
                using (SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Ignore))
                {
                    var operation = engine.RecognizeAsync(bitmap);
                    try
                    {
                        while (operation.Status == Windows.Foundation.AsyncStatus.Started) await Task.Delay(25).ConfigureAwait(false);
                        OcrResult result = operation.GetResults();
                        StringBuilder action = new StringBuilder();
                        foreach (OcrLine line in result.Lines) foreach (OcrWord word in line.Words)
                            if (word.BoundingRect.X > crop.Width * .65 && word.BoundingRect.Y > crop.Height * .70) action.Append(word.Text);
                        return Matches(result.Text, action.ToString());
                    }
                    finally { operation.Close(); }
                }
            }
        }
    }
}
