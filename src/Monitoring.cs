using System;
using System.Globalization;
using System.IO;

namespace CodexAutoResume
{
    internal sealed class MonitorPulse
    {
        DateTime last;
        public MonitorPulse(DateTime now) { last = now; }
        public void Reset(DateTime now) { last = now; }
        public bool Due(DateTime now, TimeSpan interval)
        {
            if (now < last) { last = now; return false; }
            if (now - last < interval) return false;
            last = now; return true;
        }
    }

    internal sealed class MonitorActivity
    {
        public long Reads, Completed, NewContent, Cached, Errors;
        public DateTime LastAttempt, LastSample, LastContent;
        public string LastIcon = "尚未识别";
        long contentSequence = -1;
        bool controls;
        long controlReads;
        public void ResetSource()
        { LastSample = LastContent = DateTime.MinValue; LastIcon = "尚未识别"; contentSequence = -1; controls = false; }
        public void Start(DateTime now) { Reads++; LastAttempt = now; }
        public void Background(WindowFrame frame)
        {
            controls = false;
            Completed++; LastSample = frame.CapturedAt; LastContent = frame.ContentAt;
            if (contentSequence != frame.ContentSequence) { NewContent++; contentSequence = frame.ContentSequence; }
            else Cached++;
        }
        public void Foreground(DateTime now) { controls = false; Completed++; NewContent++; LastSample = LastContent = now; }
        public void Control(DateTime now) { controls = true; Completed++; controlReads++; LastSample = now; }
        public void Recognized(ButtonState state) { LastIcon = Portable.StateName(state); }
        internal static string Age(DateTime stamp, DateTime now)
        { return stamp == DateTime.MinValue ? "尚无" : Math.Max(0, (now - stamp).TotalSeconds).ToString("F0", CultureInfo.InvariantCulture) + "秒"; }
        public string Summary(DateTime now, bool enabled, bool pending, bool background, int candidates, int attempts, int verified)
        {
            if (controls) return (enabled ? "监测中" : "已停止") + " · 后台控件；最近状态=" + LastIcon + "；控件读取 " + controlReads +
                "；上次核验距今 " + Age(LastSample, now) + "；尝试 " + attempts + "/确认 " + verified + "；控件观察时间不是系统新帧时间";
            string freshness = LastContent == DateTime.MinValue ? "尚无画面" : (now - LastContent).TotalSeconds > 5 ? "画面静止，保留最近观察" : "最近取样有效";
            return String.Format("{0} · {1} · {2}；{15}；最近图标={3}；读取 {4}/完成 {5}/新帧取样 {6}/缓存 {7}/异常 {8}；上次取样距今 {9}；上次{10}距今 {11}；候选 {12}；尝试 {13}/确认 {14}",
                enabled ? "监测中" : "已停止", background ? "后台" : "前台", pending ? "等待本次处理返回" : "等待下一轮", LastIcon,
                Reads, Completed, NewContent, Cached, Errors, Age(LastSample, now), background ? "系统新帧" : "屏幕取样", Age(LastContent, now), candidates, attempts, verified, freshness);
        }
    }

    // Fixed-size local logs; no images, OCR text or chat titles. Failure to write
    // a log must not halt monitoring or create a repeated dialog.
    internal sealed class MonitorLog
    {
        readonly object sync = new object();
        readonly string directory;
        readonly long limit;
        public bool WriteFailed { get; private set; }
        public MonitorLog(string directory, long limit = 8 * 1024 * 1024) { this.directory = directory; this.limit = limit; }
        public void Write(string fileName, string category, string message, DateTime now)
        {
            lock (sync)
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, fileName);
                if (File.Exists(path) && new FileInfo(path).Length >= limit)
                {
                    string previous = path + ".1";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                string line = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + category + "] " + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine;
                File.AppendAllText(path, line); WriteFailed = false;
            }
            catch { WriteFailed = true; }
        }
        internal RecentDiagnosticLogs Recent(DateTime now)
        {
            var result = new RecentDiagnosticLogs { FromUtc = now.ToUniversalTime().AddMinutes(-30), UntilUtc = now.ToUniversalTime() };
            lock (sync)
            foreach (string name in new[] { "diagnostics.log.1", "diagnostics.log", "events.log.1", "events.log" })
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) result.Files[name] = RecentDiagnosticLogs.Filter(File.ReadAllText(path), result.FromUtc.ToLocalTime(), result.UntilUtc.ToLocalTime());
            }
            return result;
        }
    }
}
