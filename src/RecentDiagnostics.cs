using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CodexAutoResume
{
    internal sealed class RecentDiagnosticLogs
    {
        internal readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        internal DateTime FromUtc, UntilUtc;
        internal string Text()
        {
            var result = new StringBuilder("CodexAutoResume " + Portable.Version + " | recent 30 minutes | " + FromUtc.ToString("O") + " .. " + UntilUtc.ToString("O") + Environment.NewLine);
            foreach (var file in Files) result.AppendLine("--- " + file.Key + " ---").Append(file.Value);
            return result.ToString();
        }
        internal static string Filter(string text, DateTime fromLocal, DateTime untilLocal)
        {
            // Text records have millisecond precision, including cutoff records.
            fromLocal = new DateTime(fromLocal.Ticks - fromLocal.Ticks % TimeSpan.TicksPerMillisecond, fromLocal.Kind);
            var result = new StringBuilder();
            using (var reader = new StringReader(text ?? ""))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    DateTime timestamp;
                    if (line.Length >= 23 && DateTime.TryParseExact(line.Substring(0, 23), "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) && timestamp >= fromLocal && timestamp <= untilLocal)
                        result.AppendLine(line);
                }
            }
            return result.ToString();
        }
        internal static void WriteEntry(ZipArchive archive, string name, string text)
        { using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
        internal void Export(string destination, CaptureEvidence evidence)
        {
            // CreateNew prevents a failed export from destroying an existing file.
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "README.txt", "CodexAutoResume " + Portable.Version + "; OS=" + Environment.OSVersion.VersionString + "; runtime=" + Environment.Version + "; x64=" + Environment.Is64BitProcess + "; utc-offset=" + DateTimeOffset.Now.Offset + "\r\nWindow: " + FromUtc.ToString("O") + " .. " + UntilUtc.ToString("O") +
                    "\r\nLogs use the originating PC's local display time. Images are opt-in, local, bounded and may cover less than 30 minutes.\r\nImages can contain private chats; review before sharing. No settings, credentials or instruction-store files are included.\r\n");
                foreach (var file in Files) WriteEntry(archive, "logs/" + file.Key, file.Value);
                if (evidence != null) evidence.ExportRecent(archive, FromUtc, UntilUtc);
            }
        }
    }
}
