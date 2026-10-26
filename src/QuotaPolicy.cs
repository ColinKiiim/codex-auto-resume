using System;
using System.Linq;

namespace CodexAutoResume
{
    internal enum QuotaLevel { Unknown, Low, Healthy }
    internal enum QuotaPhase { None, Stopping, Waiting, Resuming, Uncertain }

    // Pure decision layer: a missing selected window can never release a pause.
    internal static class QuotaPolicy
    {
        internal static QuotaLevel Evaluate(UsageSnapshot snapshot, UsageError error, UserPreferences options, DateTime now)
        {
            if (snapshot == null || error != UsageError.None || snapshot.FetchedUtc > now ||
                (now - snapshot.FetchedUtc).TotalSeconds > 30 || (!options.QuotaFiveHour && !options.QuotaWeekly)) return QuotaLevel.Unknown;
            bool unknown = false, low = false;
            foreach (int minutes in new[] { 300, 10080 })
            {
                if (minutes == 300 ? !options.QuotaFiveHour : !options.QuotaWeekly) continue;
                var windows = snapshot.Windows.Where(w => w.Minutes == minutes).ToArray();
                if (windows.Length != 1 || !windows[0].Remaining.HasValue || Double.IsNaN(windows[0].Remaining.Value) ||
                    Double.IsInfinity(windows[0].Remaining.Value) || windows[0].Remaining < 0 || windows[0].Remaining > 100) { unknown = true; continue; }
                if (windows[0].Remaining.Value <= (minutes == 300 ? options.FiveHourThreshold : options.WeeklyThreshold)) low = true;
            }
            return low ? QuotaLevel.Low : unknown ? QuotaLevel.Unknown : QuotaLevel.Healthy;
        }
    }

    internal sealed class QuotaPause
    {
        internal QuotaPhase Phase;
        internal ComposerContext Context;
        internal string AccountKey;
        internal DateTime ActionAt, NextSample;
        internal bool ManualOverride, ContinuationAttempted;
        internal bool Owned { get { return Phase != QuotaPhase.None; } }
        internal void Reset()
        { Phase = QuotaPhase.None; Context = null; AccountKey = null; ActionAt = NextSample = DateTime.MinValue; ManualOverride = ContinuationAttempted = false; }
        internal void StopSent(DateTime now) { Phase = QuotaPhase.Stopping; ActionAt = now; }
        internal void Observe(ButtonState state, DateTime contentAt, DateTime now)
        {
            if (!Owned) return;
            if (contentAt > ActionAt && Phase == QuotaPhase.Stopping && (state == ButtonState.Resume || state == ButtonState.Send))
            { Phase = QuotaPhase.Waiting; NextSample = now.AddMinutes(1); }
            else if (contentAt > ActionAt && Phase == QuotaPhase.Waiting && state == ButtonState.Running)
            { Reset(); ManualOverride = true; }
            else if (contentAt > ActionAt && Phase == QuotaPhase.Resuming && state == ButtonState.Running) Reset();
            if ((Phase == QuotaPhase.Stopping || Phase == QuotaPhase.Resuming) && (now - ActionAt).TotalSeconds > 15) Phase = QuotaPhase.Uncertain;
        }
        internal bool Due(QuotaLevel level, DateTime now)
        { return Phase != QuotaPhase.Waiting || level == QuotaLevel.Healthy || now >= NextSample; }
    }
}
