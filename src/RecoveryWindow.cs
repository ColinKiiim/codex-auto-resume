using System;
using System.Drawing;

namespace CodexAutoResume
{
    internal struct InputSnapshot
    {
        internal bool Valid, Held;
        internal uint Tick, IdleMilliseconds;
    }

    internal sealed class RecoveryWindow
    {
        internal const int QuietMilliseconds = 1500;
        DateTime firstResume;
        internal void Reset() { firstResume = DateTime.MinValue; }
        internal void Observe(ButtonState state, DateTime now)
        {
            if (state != ButtonState.Resume) Reset();
            else if (firstResume == DateTime.MinValue || now < firstResume) firstResume = now;
        }
        internal bool Ready(InputSnapshot input, DateTime now)
        {
            return firstResume != DateTime.MinValue && (now - firstResume).TotalMilliseconds >= QuietMilliseconds &&
                input.Valid && !input.Held && input.IdleMilliseconds >= QuietMilliseconds;
        }
        internal bool Unchanged(InputSnapshot before, InputSnapshot after, DateTime now)
        { return Ready(after, now) && before.Valid && before.Tick == after.Tick; }
    }

    internal sealed class CaptureWatchdog
    {
        internal const int IntervalSeconds = 12;
        DateTime unchangedSince, nextProbe;
        string signature;
        internal string LastSignature { get { return signature ?? "none"; } }
        internal void Reset() { signature = null; unchangedSince = nextProbe = DateTime.MinValue; }
        internal void Observe(string value, DateTime now)
        {
            if (signature != value || unchangedSince == DateTime.MinValue || now < unchangedSince)
            { signature = value; unchangedSince = now; }
        }
        internal bool Due(DateTime now, DateTime contentAt)
        {
            if (now < nextProbe) return false;
            return (unchangedSince != DateTime.MinValue && (now - unchangedSince).TotalSeconds >= IntervalSeconds) ||
                (contentAt != DateTime.MinValue && (now - contentAt).TotalSeconds >= IntervalSeconds);
        }
        internal void Started(DateTime now) { nextProbe = now.AddSeconds(IntervalSeconds); }
        internal static string Signature(Bitmap image)
        {
            // Small badge signatures detect semantic image changes even when the
            // compositor keeps delivering fresh timestamps for identical pixels.
            uint hash = 2166136261;
            for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 24))
                for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 24))
                    hash = unchecked((hash ^ (uint)image.GetPixel(x, y).ToArgb()) * 16777619);
            return image.Width + "x" + image.Height + ":" + hash.ToString("X8");
        }
    }
}
