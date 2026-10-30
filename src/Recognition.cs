using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexAutoResume
{
    public enum ButtonState { Unknown, Resume, Running, Send, Voice, VoiceClose }

    public sealed class Detection
    {
        public ButtonState State;
        public double ResumeScore, RunningScore, SendScore, VoiceScore, VoiceCloseScore;
        public int IconParts;
        public Rectangle ButtonBounds;
        public string Detail;
        public override string ToString()
        {
            return String.Format("{0}; resume={1:F3}; running={2:F3}; send={3:F3}; voice={4:F3}; {5}", State, ResumeScore, RunningScore, SendScore, VoiceScore, Detail);
        }
    }

    public sealed class LocatedButton
    {
        public Rectangle Crop;
        public Rectangle Badge;
        public Detection Detection;
    }

    // Deliberately restricted to the blue circular button in the supplied samples.
    // Segment the blue badge first, then compare only the white icon, not the UI text.
    public sealed class Recognizer
    {
        const int Grid = 32;
        readonly bool[] resumeTemplate, runningTemplate, sendTemplate, sendCurrentTemplate, voiceTemplate, voiceCloseTemplate;
        public Recognizer()
        {
            resumeTemplate = LoadTemplate("resume-reference.png");
            runningTemplate = LoadTemplate("running-reference.png");
            sendTemplate = LoadTemplate("send-reference.png");
            sendCurrentTemplate = LoadTemplate("send-current-reference.png");
            voiceTemplate = LoadTemplate("voice-reference.png");
            voiceCloseTemplate = LoadTemplate("voice-close-reference.png");
        }

        static bool[] LoadTemplate(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded sample: " + name);
                using (Bitmap b = new Bitmap(s))
                {
                    Rectangle badge;
                    string detail;
                    int parts;
                    bool relaxed = name.StartsWith("send", StringComparison.Ordinal) || name.StartsWith("voice", StringComparison.Ordinal);
                    bool[] shape = Extract(b, out badge, out detail, out parts, relaxed);
                    if (shape == null) throw new InvalidOperationException("Invalid sample: " + name + ": " + detail);
                    return shape;
                }
            }
        }

        public Detection Classify(Bitmap image)
        {
            Rectangle badge;
            string detail;
            int parts;
            bool[] shape = Extract(image, out badge, out detail, out parts, false);
            Detection d = new Detection { State = ButtonState.Unknown, ButtonBounds = badge, Detail = detail, IconParts = parts };
            if (shape != null)
            {
                d.ResumeScore = Similarity(shape, resumeTemplate); d.RunningScore = Similarity(shape, runningTemplate);
                double strictSend = Math.Max(Similarity(shape, sendTemplate), Similarity(shape, sendCurrentTemplate));
                if (parts == 1 && d.ResumeScore >= .80 && d.ResumeScore - Math.Max(d.RunningScore, strictSend) >= .15) d.State = ButtonState.Resume;
                else if (parts == 1 && d.RunningScore >= .83 && d.RunningScore - Math.Max(d.ResumeScore, strictSend) >= .15) d.State = ButtonState.Running;
                if (d.State != ButtonState.Unknown) return d;
            }
            // Lower-contrast antialiased strokes are used ONLY for non-clickable states.
            shape = Extract(image, out badge, out detail, out parts, true);
            d.ButtonBounds = badge; d.Detail = detail; d.IconParts = parts;
            if (shape == null) return d;
            d.SendScore = parts <= 2 ? Math.Max(StrokeSimilarity(shape, sendTemplate), StrokeSimilarity(shape, sendCurrentTemplate)) : 0;
            d.VoiceScore = parts >= 3 && VoiceStrokes(shape) ? StrokeSimilarity(shape, voiceTemplate) : 0;
            d.VoiceCloseScore = parts == 1 ? StrokeSimilarity(shape, voiceCloseTemplate) : 0;
            double other = Math.Max(Similarity(shape, resumeTemplate), Similarity(shape, runningTemplate));
            if (parts == 1 && d.VoiceCloseScore >= .88 && d.VoiceCloseScore - Math.Max(other, Math.Max(d.SendScore, d.VoiceScore)) >= .12)
            { d.State = ButtonState.VoiceClose; d.Detail += String.Format("; auxiliary voice-close {0:F3}", d.VoiceCloseScore); }
            else if (parts <= 2 && TopFraction(shape) < .50 && d.SendScore >= .80 && d.SendScore - Math.Max(other, d.VoiceScore) >= .12) d.State = ButtonState.Send;
            else if (parts >= 3 && parts <= 6 && d.VoiceScore >= .80 && d.VoiceScore - Math.Max(other, d.SendScore) >= .12) d.State = ButtonState.Voice;
            return d;
        }

        public List<LocatedButton> FindButtons(Bitmap image)
        {
            // Scan in bulk instead of GetPixel for large windows (including 4K screens).
            int w = image.Width, h = image.Height;
            bool[,] blue = new bool[w, h];
            Rectangle imageRect = new Rectangle(0, 0, w, h);
            BitmapData data = image.LockBits(imageRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4, b = row[i], g = row[i + 1], r = row[i + 2];
                        blue[x, y] = b >= 130 && b - r >= 65 && b - g >= 30 && g >= 45 && g <= 180;
                    }
                }
            }
            finally { image.UnlockBits(data); }
            List<LocatedButton> found = new List<LocatedButton>();
            List<LocatedButton> auxiliary = new List<LocatedButton>();
            foreach (Component component in Components(blue))
            {
                Rectangle badge = component.Bounds;
                double aspect = badge.Width / (double)badge.Height;
                double fill = component.Pixels.Count / (double)(badge.Width * badge.Height);
                if (badge.Width < 14 || badge.Height < 14 || badge.Width > 160 || badge.Height > 160 || aspect < .83 || aspect > 1.2 || fill < .48 || fill > .86) continue;
                int side = Math.Min(256, Math.Max(48, Math.Max(badge.Width, badge.Height) + 24));
                Rectangle crop = new Rectangle(badge.Left + badge.Width / 2 - side / 2, badge.Top + badge.Height / 2 - side / 2, side, side);
                crop = Rectangle.Intersect(crop, imageRect);
                if (crop.Width < badge.Width + 2 || crop.Height < badge.Height + 2) continue;
                using (Bitmap local = image.Clone(crop, PixelFormat.Format32bppArgb))
                {
                    Detection detection = Classify(local);
                    // Blue citation badges also contain white glyphs. Only known button
                    // icons may enter automatic selection; unknown crops remain observable
                    // through manual calibration without blocking a real button.
                    if (detection.State == ButtonState.Unknown || detection.ButtonBounds.IsEmpty || detection.IconParts == 0) continue;
                    var candidate = new LocatedButton { Crop = crop, Badge = badge, Detection = detection };
                    if (detection.State == ButtonState.VoiceClose) auxiliary.Add(candidate); else found.Add(candidate);
                }
            }
            // A voice close icon is observable only when no primary button exists.
            // It must not make an otherwise unique resume candidate ambiguous.
            if (found.Count == 0) found = auxiliary;
            found.Sort(delegate(LocatedButton a, LocatedButton b) { int byY = a.Badge.Y.CompareTo(b.Badge.Y); return byY == 0 ? a.Badge.X.CompareTo(b.Badge.X) : byY; });
            return found;
        }

        static bool Blue(Color c)
        {
            return c.B >= 130 && c.B - c.R >= 65 && c.B - c.G >= 30 && c.G >= 45 && c.G <= 180;
        }
        static bool White(Color c)
        {
            return Math.Min(c.R, Math.Min(c.G, c.B)) >= 155 &&
                Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 65;
        }

        sealed class Component
        {
            public List<Point> Pixels = new List<Point>();
            public Rectangle Bounds;
        }

        static List<Component> Components(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            bool[,] seen = new bool[w, h];
            List<Component> result = new List<Component>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (!mask[x, y] || seen[x, y]) continue;
                Component c = new Component();
                Queue<Point> queue = new Queue<Point>();
                queue.Enqueue(new Point(x, y)); seen[x, y] = true;
                int minX = x, maxX = x, minY = y, maxY = y;
                while (queue.Count > 0)
                {
                    Point p = queue.Dequeue(); c.Pixels.Add(p);
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = p.X + dx, ny = p.Y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny] && !seen[nx, ny])
                        { seen[nx, ny] = true; queue.Enqueue(new Point(nx, ny)); }
                    }
                }
                c.Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                result.Add(c);
            }
            result.Sort(delegate(Component a, Component b) { return b.Pixels.Count.CompareTo(a.Pixels.Count); });
            return result;
        }

        static bool[] Extract(Bitmap image, out Rectangle badge, out string detail, out int partCount, bool relaxed)
        {
            badge = Rectangle.Empty; detail = "no centered blue circle"; partCount = 0;
            int w = image.Width, h = image.Height;
            if (w < 16 || h < 16 || w > 256 || h > 256) { detail = "crop must be 16..256 pixels"; return null; }
            bool[,] blue = new bool[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) blue[x, y] = Blue(image.GetPixel(x, y));
            foreach (Component c in Components(blue))
            {
                Rectangle r = c.Bounds;
                double ratio = (double)r.Width / r.Height, fill = (double)c.Pixels.Count / (r.Width * r.Height);
                if (r.Width < 14 || r.Height < 14 || ratio < .83 || ratio > 1.2 || fill < .48 || fill > .86) continue;
                if (r.Left < 1 || r.Top < 1 || r.Right >= w || r.Bottom >= h) continue;
                if (Math.Abs(r.Left + r.Width / 2.0 - w / 2.0) > w * .20 ||
                    Math.Abs(r.Top + r.Height / 2.0 - h / 2.0) > h * .20) continue;
                badge = r; break;
            }
            if (badge.IsEmpty) return null;
            bool[,] icon = new bool[w, h];
            double cx = badge.Left + (badge.Width - 1) / 2.0, cy = badge.Top + (badge.Height - 1) / 2.0;
            double rx = badge.Width * .37, ry = badge.Height * .37;
            for (int y = badge.Top; y < badge.Bottom; y++) for (int x = badge.Left; x < badge.Right; x++)
                if (Math.Abs(x - cx) <= rx && Math.Abs(y - cy) <= ry)
                {
                    Color c = image.GetPixel(x, y);
                    icon[x, y] = relaxed ? c.R >= 110 && c.G >= 110 && c.B >= 155 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 115 : White(c);
                }
            List<Component> parts = Components(icon);
            partCount = parts.Count;
            if (parts.Count == 0 || parts.Count > 6) { detail = "white icon missing or noisy"; return null; }
            Rectangle box = parts[0].Bounds;
            int pixelCount = 0;
            foreach (Component component in parts) { box = Rectangle.Union(box, component.Bounds); pixelCount += component.Pixels.Count; }
            double ar = (double)box.Width / box.Height;
            if (pixelCount < (relaxed ? 6 : 12) || box.Width < badge.Width * .15 || box.Height < badge.Height * .15 ||
                box.Width > badge.Width * .60 || box.Height > badge.Height * .60 || ar < .5 || ar > 1.4 ||
                Math.Abs(box.Left + box.Width / 2.0 - cx) > badge.Width * .17 ||
                Math.Abs(box.Top + box.Height / 2.0 - cy) > badge.Height * .17)
            { detail = String.Format("icon geometry mismatch: pixels={0}; box={1}; badge={2}; ar={3:F2}", pixelCount, box, badge, ar); return null; }
            bool[] normalized = new bool[Grid * Grid];
            for (int y = 0; y < Grid; y++) for (int x = 0; x < Grid; x++)
            {
                int sx = box.Left + Math.Min(box.Width - 1, (int)((x + .5) * box.Width / Grid));
                int sy = box.Top + Math.Min(box.Height - 1, (int)((y + .5) * box.Height / Grid));
                normalized[y * Grid + x] = icon[sx, sy];
            }
            detail = String.Format("blue badge {0}x{1}; icon {2}x{3}; parts {4}", badge.Width, badge.Height, box.Width, box.Height, partCount);
            return normalized;
        }

        static double Similarity(bool[] a, bool[] b)
        {
            int intersection = 0, union = 0;
            for (int i = 0; i < a.Length; i++) { if (a[i] && b[i]) intersection++; if (a[i] || b[i]) union++; }
            return union == 0 ? 0 : (double)intersection / union;
        }
        // Thin strokes vary substantially under DPI resampling. Use a symmetric
        // distance-tolerant F-score for informational Send/Voice labels only.
        // Resume and Running retain the original strict IoU thresholds.
        static double StrokeSimilarity(bool[] a, bool[] b)
        {
            double ab = CoveredFraction(a, b), ba = CoveredFraction(b, a);
            return ab + ba == 0 ? 0 : 2 * ab * ba / (ab + ba);
        }
        static bool VoiceStrokes(bool[] shape)
        {
            bool[,] mask = new bool[Grid, Grid];
            for (int y = 0; y < Grid; y++) for (int x = 0; x < Grid; x++) mask[x, y] = shape[y * Grid + x];
            // A waveform is separate narrow vertical bars. Resampled digits can
            // split into several parts too, but retain wide loops or curves.
            foreach (Component part in Components(mask))
                if (part.Bounds.Width > Grid / 4 || part.Bounds.Height < part.Bounds.Width) return false;
            return true;
        }
        static double TopFraction(bool[] shape)
        {
            int pixels = 0;
            for (int y = 0; y < 4; y++) for (int x = 0; x < Grid; x++) if (shape[y * Grid + x]) pixels++;
            return pixels / (4.0 * Grid);
        }
        static double CoveredFraction(bool[] a, bool[] b)
        {
            int total = 0, matched = 0;
            for (int y = 0; y < Grid; y++) for (int x = 0; x < Grid; x++)
            {
                if (!a[y * Grid + x]) continue;
                total++; bool found = false;
                for (int dy = -2; dy <= 2 && !found; dy++) for (int dx = -2; dx <= 2 && !found; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < Grid && ny < Grid && b[ny * Grid + nx]) found = true;
                }
                if (found) matched++;
            }
            return total == 0 ? 0 : (double)matched / total;
        }
    }

    // Continuous monitoring: rate-limit retries, never latch a capture timeout.
    public sealed class ResumeGate
    {
        int resumeFrames, runningFrames;
        bool pendingVerification;
        public bool AwaitingRunning { get; private set; }
        internal bool PendingVerification { get { return pendingVerification; } }
        public int Attempts { get; private set; }
        public int Verified { get; private set; }
        DateTime attemptedAt, firstResumeAt, lastVerified = DateTime.MinValue;
        internal bool ObserveCaptured(ButtonState state, DateTime now, DateTime contentAt)
        {
            // Re-reading a cached square must never acknowledge a later click.
            if (state == ButtonState.Running && pendingVerification &&
                (contentAt <= attemptedAt || (now - contentAt).TotalSeconds > 5 || contentAt > now.AddSeconds(1)))
                state = ButtonState.Unknown;
            return Observe(state, now);
        }
        public bool Observe(ButtonState state, DateTime now)
        {
            if (state == ButtonState.Resume && resumeFrames == 0) firstResumeAt = now;
            if (state == ButtonState.Running) { runningFrames++; resumeFrames = 0; }
            else { runningFrames = 0; resumeFrames = state == ButtonState.Resume ? resumeFrames + 1 : 0; }
            if (pendingVerification && runningFrames >= 1)
            { AwaitingRunning = false; pendingVerification = false; Verified++; lastVerified = now; }
            if (AwaitingRunning && (now - attemptedAt).TotalSeconds >= 5) AwaitingRunning = false;
            return !AwaitingRunning && resumeFrames >= 4 && (now - firstResumeAt).TotalMilliseconds >= 900 && (now - attemptedAt).TotalSeconds >= 5 && (now - lastVerified).TotalSeconds >= 5;
        }
        public void MarkAttempt(DateTime now)
        {
            if (AwaitingRunning) throw new InvalidOperationException("Attempt already latched");
            AwaitingRunning = true; pendingVerification = true; attemptedAt = now; resumeFrames = 0; runningFrames = 0; Attempts++;
        }
        public void BreakStreak() { resumeFrames = 0; runningFrames = 0; }
        internal void CancelPending() { AwaitingRunning = pendingVerification = false; BreakStreak(); }
    }
}
