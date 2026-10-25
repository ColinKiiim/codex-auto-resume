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
        sealed class Evidence { internal bool Waiting; internal Rectangle Bounds; }
        Task<Evidence> pending;
        DateTime nextCheck;
        int epoch;
        public bool Waiting { get; private set; }
        internal Rectangle PreviewBounds { get; private set; }
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
            bool action = bottomRight.Contains("跳过") || bottomRight.Contains("提交") || bottomRight.Contains("继续") || bottomRight.Contains("发送") || bottomRight.Contains("skip") || bottomRight.Contains("submit") || bottomRight.Contains("continue") || bottomRight.Contains("send");
            bool waiting = text.Contains("等待你的回答") || text.Contains("等待您的回答") || text.Contains("waitingforyouranswer") || text.Contains("waitingforyourresponse");
            bool pages = Regex.IsMatch(text, @"\d{1,2}[o0]f\d{1,2}");
            bool asking = text.Contains("正在询问问题") || text.Contains("askingquestions");
            return (asking && (waiting || pages)) || (action && (waiting || pages));
        }

        public void Reset() { epoch++; Waiting = false; PreviewBounds = Rectangle.Empty; nextCheck = DateTime.MinValue; }

        public async Task<bool> CheckAsync(Bitmap image)
        {
            if (DateTime.UtcNow < nextCheck) return Waiting;
            int revision = epoch;
            Task<Evidence> work;
            int left = (int)(image.Width * .30), top = (int)(image.Height * .40);
            lock (sync)
            {
                if (pending != null) return Waiting;
                Bitmap crop = image.Clone(new Rectangle(left, top, image.Width - left, image.Height - top), PixelFormat.Format32bppArgb);
                work = Task.Run(async delegate { using (crop) return await RecognizeAsync(crop); }); pending = work;
            }
            // Do not block the monitor indefinitely, or dispatch another OCR
            // operation behind an unresponsive Windows language service.
            Task winner = await Task.WhenAny(work, Task.Delay(2500));
            if (winner != work)
            {
                Task cleanup = work.ContinueWith(delegate(Task<Evidence> late) { if (late.IsFaulted) { var observed = late.Exception; } lock (sync) if (pending == late) pending = null; });
                nextCheck = DateTime.UtcNow.AddSeconds(3); return Waiting;
            }
            try
            {
                Evidence result = await work;
                if (revision == epoch)
                {
                    Waiting = result.Waiting; Rectangle bounds = result.Bounds;
                    if (!bounds.IsEmpty) bounds.Offset(left, top);
                    PreviewBounds = bounds;
                }
            }
            catch { if (revision == epoch) { Waiting = false; PreviewBounds = Rectangle.Empty; } Availability = "本机文字识别暂不可用，继续图标监测"; }
            finally { lock (sync) if (pending == work) pending = null; nextCheck = DateTime.UtcNow.AddSeconds(2); }
            return Waiting;
        }

        async Task<Evidence> RecognizeAsync(Bitmap crop)
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Availability = "未安装 Windows OCR 语言，继续图标监测"; return new Evidence(); }
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
                        bool waiting = Matches(result.Text, action.ToString());
                        Rectangle bounds = Rectangle.Empty;
                        bool bottomSeen = false;
                        if (waiting)
                        {
                            foreach (OcrLine line in result.Lines)
                            {
                                string text = Regex.Replace(line.Text, @"\s+", "").ToLowerInvariant();
                                bool signal = text.Contains("等待你的回答") || text.Contains("等待您的回答") || text.Contains("正在询问问题") || text.Contains("waitingforyour") || text.Contains("askingquestions") || Regex.IsMatch(text, @"\d{1,2}[o0]f\d{1,2}");
                                foreach (OcrWord word in line.Words)
                                {
                                    Windows.Foundation.Rect r = word.BoundingRect;
                                    bool bottom = r.X > crop.Width * .65 && r.Y > crop.Height * .70;
                                    bottomSeen |= bottom;
                                    if (!signal && !bottom) continue;
                                    Rectangle part = Rectangle.FromLTRB((int)Math.Floor(r.X), (int)Math.Floor(r.Y), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
                                    bounds = bounds.IsEmpty ? part : Rectangle.Union(bounds, part);
                                }
                            }
                            // OCR can miss a short action label. Keep the expected
                            // action corner in a confirmed question preview anyway.
                            if (!bottomSeen)
                            {
                                Rectangle actions = Rectangle.FromLTRB((int)(crop.Width * .65), (int)(crop.Height * .70), crop.Width, crop.Height);
                                bounds = bounds.IsEmpty ? actions : Rectangle.Union(bounds, actions);
                            }
                            if (!bounds.IsEmpty) { bounds.Inflate(12, 12); bounds.Intersect(new Rectangle(Point.Empty, crop.Size)); }
                        }
                        return new Evidence { Waiting = waiting, Bounds = bounds };
                    }
                    finally { operation.Close(); }
                }
            }
        }
    }
}
