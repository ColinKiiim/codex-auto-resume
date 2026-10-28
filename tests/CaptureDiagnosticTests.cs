using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal static class CaptureDiagnosticTests
    {
        internal static void Run(Action<bool, string> check)
        {
            string root = Path.Combine(Path.GetTempPath(), "CAR-diagnostics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            DateTime now = DateTime.UtcNow; string stamp = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
            var journal = new MonitorLog(root);
            journal.Write("events.log", "state", "old", now.AddMinutes(-31));
            journal.Write("events.log", "state", "in-window", now.AddMinutes(-10));
            journal.Write("events.log", "state", "future", now.AddMinutes(1));
            journal.Write("diagnostics.log", "state", "exact-cutoff", now.AddMinutes(-30));
            journal.Write("diagnostics.log", "state", "exact-end", now);
            File.WriteAllText(Path.Combine(root, "settings.json"), "private-sentinel");
            var recent = journal.Recent(now);
            check(recent.Text().Contains("in-window") && recent.Text().Contains("exact-cutoff") && recent.Text().Contains("exact-end") && !recent.Text().Contains("[state] old") && !recent.Text().Contains("[state] future"), "30-minute filter includes boundaries and rejects older/future lines");
            var evidence = new CaptureEvidence(root, journal, 2, 1024 * 1024);
            using (var image = new Bitmap(32, 32))
            using (var frame = new WindowFrame { Image = image, Bounds = new Rectangle(0, 0, 32, 32), ContentAt = now, CapturedAt = now, ContentSequence = 1, Sequence = 1, Source = "session" })
            {
                evidence.Record(frame, "disabled", "fixture");
                check(!Directory.Exists(Path.Combine(root, "capture-evidence")), "raw frames are opt-in");
                evidence.Enable(true);
                for (int n = 0; n < 3; n++)
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(n == 0 ? Color.Red : Color.Blue);
                    frame.Sequence++; frame.ContentSequence++; frame.ContentAt = DateTime.UtcNow;
                    evidence.Record(frame, n == 0 ? "session" : "independent", "fixture-only", new Rectangle(8, 8, 16, 16), true);
                    check(evidence.Pending.Wait(5000) && evidence.LastError == null, "owned evidence worker completes without fault");
                }
                evidence.Enable(false);
            }
            string dir = Path.Combine(root, "capture-evidence");
            check(Directory.GetFiles(dir, "*.json").Length == 2 && Directory.GetFiles(dir, "*-frame.png").Length == 2 && Directory.GetFiles(dir, "*-badge.png").Length == 2, "rotation removes entire old records and keeps paired frames/badges");
            var metadataPath = Directory.GetFiles(dir, "*.json").OrderBy(x => x).First();
            var json = new JavaScriptSerializer(); var metadata = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(metadataPath));
            string prefix = Path.Combine(dir, Path.GetFileNameWithoutExtension(metadataPath));
            check((string)metadata["fullPngSha256"] == CaptureEvidence.Hash(File.ReadAllBytes(prefix + "-frame.png")), "raw frame digest matches unscaled PNG bytes");
            metadata["utc"] = now.AddMinutes(-31).ToString("O"); File.WriteAllText(metadataPath, json.Serialize(metadata));
            string zipPath = Path.Combine(root, "recent.zip"); journal.Recent(DateTime.UtcNow).Export(zipPath, evidence);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                check(zip.Entries.Count(e => e.FullName.EndsWith("-frame.png")) == 1 && !zip.Entries.Any(e => e.FullName.Contains("settings")), "export includes only recent evidence and allowlisted logs");
                check(zip.GetEntry("README.txt") != null && zip.Entries.Any(e => e.FullName == "logs/events.log"), "export carries environment and time window with text logs");
            }
            string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "not-a-directory");
            var broken = new CaptureEvidence(blocked, journal); broken.Enable(true);
            using (var frame = new WindowFrame { Image = new Bitmap(8, 8), CapturedAt = now, ContentAt = now })
            { broken.Record(frame, "failure", "fixture", null, true); check(broken.Pending.Wait(5000) && broken.LastError != null, "disk failure is reported without faulting monitoring"); }
            check((string)DiagnosticStateWorker.Inspect(new ComposerRequest { Operation = "submit" })["result"] == "blocked-target", "read-only diagnostic worker rejects mutation operations");
            check((string)DiagnosticStateWorker.Inspect(null)["result"] == "blocked-target", "read-only diagnostic worker rejects missing identity");
        }
    }
}
