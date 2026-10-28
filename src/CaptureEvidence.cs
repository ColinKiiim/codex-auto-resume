using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    // Observational only: never obtains extra captures, changes focus or feeds
    // evidence back into the recovery gate. At most one owned bitmap is queued.
    internal sealed class CaptureEvidence
    {
        readonly object sync = new object(), io = new object();
        readonly string directory;
        readonly MonitorLog journal;
        readonly int maxRecords;
        readonly long maxBytes;
        Task pending = Task.FromResult(0);
        DateTime until, nextSample;
        string session;
        int serial;
        internal long Dropped;
        internal string LastError;
        internal CaptureEvidence(string logs, MonitorLog journal, int maxRecords = 180, long maxBytes = 128L * 1024 * 1024)
        { directory = Path.Combine(logs, "capture-evidence"); this.journal = journal; this.maxRecords = maxRecords; this.maxBytes = maxBytes; }
        internal bool Enabled { get { lock (sync) return until > DateTime.UtcNow; } }
        internal Task Pending { get { lock (sync) return pending; } }
        internal void Enable(bool value)
        {
            lock (sync)
            {
                until = value ? DateTime.UtcNow.AddHours(1) : DateTime.MinValue;
                if (value) { session = DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); nextSample = DateTime.MinValue; }
            }
            journal.Write("diagnostics.log", "evidence-mode", "enabled=" + value + "; session=" + session + "; expires-utc=" + until.ToString("O") + "; max-records=" + maxRecords + "; max-bytes=" + maxBytes, DateTime.UtcNow);
        }
        internal static string Hash(string value)
        { return Hash(Encoding.UTF8.GetBytes(value ?? "")); }
        internal static string Hash(byte[] bytes)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string TargetInfo(Target target, int generation)
        {
            try
            {
                if (target == null) return "target=none; generation=" + generation;
                IntPtr window = target.Window;
                Native.RECT rect; bool hasRect = Native.GetWindowRect(window, out rect);
                return "hwnd=" + window.ToInt64().ToString("X") + "; pid=" + target.ProcessId + "; process-start=" + target.ProcessStart +
                    "; title-sha256=" + Hash(Native.Title(window)) + "; generation=" + generation + "; valid-window=" + Native.IsWindow(window) +
                    "; visible=" + Native.IsWindowVisible(window) + "; minimized=" + Native.IsIconic(window) + "; foreground=" + (Native.GetForegroundWindow() == window) +
                    "; foreground-hwnd=" + Native.GetForegroundWindow().ToInt64().ToString("X") + "; dpi=" + Native.Dpi(window) + "; native-bounds=" + (hasRect ? rect.Rectangle.ToString() : "unavailable");
            }
            catch { return "target-metadata=unavailable; generation=" + generation; }
        }
        internal static string FrameInfo(WindowFrame frame)
        {
            return "source=" + frame.Source + "; captured-utc=" + frame.CapturedAt.ToString("O") + "; system-content-utc=" + frame.ContentAt.ToString("O") +
                "; read-sequence=" + frame.Sequence + "; content-sequence=" + frame.ContentSequence + "; capture-bounds=" + frame.Bounds +
                "; pixels=" + frame.Image.Width + "x" + frame.Image.Height + "; system-time-is-business-proof=False";
        }
        internal void Record(WindowFrame frame, string role, string context, Rectangle? crop = null, bool force = false)
        {
            Bitmap owned = null;
            lock (sync)
            {
                if (until <= DateTime.UtcNow || !force && DateTime.UtcNow < nextSample) return;
                if (!pending.IsCompleted) { Dropped++; return; }
                try
                {
                    owned = new Bitmap(frame.Image);
                    string id = session + "-" + (++serial).ToString("D6");
                    string info = FrameInfo(frame); DateTime now = DateTime.UtcNow;
                    nextSample = now.AddSeconds(15);
                    Bitmap work = owned;
                    pending = Task.Run(delegate
                    {
                        using (work)
                        try
                        {
                            lock (io) Save(id, now, work, crop, role, context, info);
                            LastError = null;
                        }
                        catch (Exception error)
                        { LastError = error.GetType().Name; journal.Write("diagnostics.log", "evidence-error", "operation=save; error=" + LastError, DateTime.UtcNow); }
                    });
                }
                catch (Exception error) { if (owned != null) owned.Dispose(); LastError = error.GetType().Name; }
            }
        }
        void Save(string id, DateTime now, Bitmap image, Rectangle? crop, string role, string context, string frameInfo)
        {
            Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, id);
            try
            {
                byte[] png;
                using (var data = new MemoryStream()) { image.Save(data, ImageFormat.Png); png = data.ToArray(); }
                if (png.LongLength > maxBytes) { journal.Write("diagnostics.log", "evidence-skip", "reason=single-frame-too-large", now); return; }
                string fullHash = Hash(png), badgeHash = null;
                File.WriteAllBytes(prefix + "-frame.png", png);
                if (crop.HasValue)
                {
                    Rectangle bounds = crop.Value;
                    bounds.Intersect(new Rectangle(Point.Empty, image.Size));
                    if (!bounds.IsEmpty) using (var badge = image.Clone(bounds, PixelFormat.Format32bppArgb))
                    using (var data = new MemoryStream())
                    { badge.Save(data, ImageFormat.Png); byte[] bytes = data.ToArray(); badgeHash = Hash(bytes); File.WriteAllBytes(prefix + "-badge.png", bytes); }
                }
                File.WriteAllText(prefix + ".json", new JavaScriptSerializer().Serialize(new { schema = 1, version = Portable.Version, id, utc = now.ToString("O"), role, context, frame = frameInfo,
                    fullPngSha256 = fullHash, badgePngSha256 = badgeHash, crop = crop.HasValue ? crop.Value.ToString() : null, originalPixels = true }), new UTF8Encoding(false));
                Trim();
                journal.Write("diagnostics.log", "evidence-saved", "id=" + id + "; role=" + role + "; full-png-sha256=" + fullHash + "; badge-png-sha256=" + badgeHash + "; " + frameInfo, now);
            }
            catch
            {
                // Only files belonging to this generated record are removed.
                foreach (string suffix in new[] { "-frame.png", "-badge.png", ".json" }) try { File.Delete(prefix + suffix); } catch { }
                throw;
            }
        }
        static bool Owned(string name)
        { return Regex.IsMatch(name, @"^\d{8}T\d{6}-[0-9a-f]{8}-\d{6}(\.json|-frame\.png|-badge\.png)$"); }
        void Trim()
        {
            var all = new DirectoryInfo(directory).GetFiles().Where(f => Owned(f.Name)).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
            long size = all.Sum(f => f.Length); int count = all.Count(f => f.Extension == ".json");
            foreach (var group in all.GroupBy(f => Regex.Match(f.Name, @"^\d{8}T\d{6}-[0-9a-f]{8}-\d{6}").Value))
            {
                if (size <= maxBytes && count <= maxRecords) break;
                foreach (var file in group) { long length = file.Length; file.Delete(); size -= length; if (file.Extension == ".json") count--; }
            }
        }
        internal void ExportRecent(ZipArchive archive, DateTime fromUtc, DateTime untilUtc)
        {
            lock (io)
            {
                if (!Directory.Exists(directory)) return;
                foreach (var file in new DirectoryInfo(directory).GetFiles("*.json").Where(f => Owned(f.Name)).OrderBy(f => f.Name))
                {
                    var metadata = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file.FullName));
                    DateTime utc = DateTime.Parse((string)metadata["utc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                    if (utc < fromUtc || utc > untilUtc) continue;
                    string prefix = Path.GetFileNameWithoutExtension(file.Name);
                    foreach (string suffix in new[] { ".json", "-frame.png", "-badge.png" })
                    {
                        string source = Path.Combine(directory, prefix + suffix);
                        if (File.Exists(source)) ZipFileExtensions.CreateEntryFromFile(archive, source, "capture-evidence/" + prefix + suffix, CompressionLevel.Fastest);
                    }
                }
            }
        }
    }
}
