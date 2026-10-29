using System;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace CodexAutoResume
{
    public sealed class UpdateOutcome
    {
        public DateTime RecordedUtc { get; set; }
        public string Stage { get; set; }
        public string ErrorType { get; set; }
        public int? NativeError { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public string RecoveryError { get; set; }
        public string RecoveryMessage { get; set; }
        public bool Restored { get; set; }
        public bool OldVerified { get; set; }
        public bool NewVerified { get; set; }
        public bool BackupVerified { get; set; }
    }
    internal static partial class Updates
    {
        internal static bool Matches(string path, string hash)
        { try { return File.Exists(path) && Hash(path) == hash; } catch { return false; } }
        static void RecordOutcome(UpdateJob job, string stage, Exception error, Exception recovery, bool restored)
        {
            var result = new UpdateOutcome { RecordedUtc = DateTime.UtcNow, Stage = stage, ErrorType = error == null ? null : error.GetType().FullName,
                NativeError = error is Win32Exception ? (int?)((Win32Exception)error).NativeErrorCode : null, Error = error == null ? null : error.ToString(),
                Message = error == null ? null : error.Message, RecoveryError = recovery == null ? null : recovery.ToString(), RecoveryMessage = recovery == null ? null : recovery.Message, Restored = restored,
                OldVerified = Matches(job.Destination, job.OldHash), NewVerified = Matches(job.Destination, job.NewHash), BackupVerified = Matches(job.Backup, job.OldHash) };
            try { JsonFile.Write(Path.Combine(job.Directory, "outcome.json"), result); } catch { }
            if (error != null) try { File.WriteAllText(Path.Combine(job.Directory, "failure.txt"), "stage=" + stage + Environment.NewLine + result.Error +
                (recovery == null ? "" : Environment.NewLine + "recovery=" + result.RecoveryError), Encoding.UTF8); } catch { }
            new MonitorLog(Path.Combine(Portable.DataDirectory, "logs")).Write("diagnostics.log", "update-result", "job=" + Path.GetFileName(job.Directory) + "; version=" + job.Version + "; stage=" + stage + "; error=" + result.ErrorType +
                "; native-error=" + result.NativeError + "; recovery-error=" + (recovery == null ? "" : recovery.GetType().Name) + "; restored=" + restored +
                "; old-verified=" + result.OldVerified + "; new-verified=" + result.NewVerified + "; backup-verified=" + result.BackupVerified, result.RecordedUtc);
        }
        internal static string FailureSummary(UpdateJob job)
        {
            if (Matches(job.Destination, job.OldHash)) return UiText.Get("更新未完成，已核验当前 EXE 为原版本");
            if (Matches(job.Destination, job.NewHash)) return UiText.Get("更新未完成，当前 EXE 仍为新版本，请手动核对");
            return UiText.Get("更新未完成，无法核验当前 EXE，请手动核对并保留备份");
        }
        internal static string FailureDetail(UpdateJob job)
        {
            try
            {
                var outcome = JsonFile.Read<UpdateOutcome>(Path.Combine(job.Directory, "outcome.json"));
                if (outcome != null) return "stage=" + outcome.Stage + Environment.NewLine + outcome.Message +
                    (outcome.RecoveryMessage == null ? "" : Environment.NewLine + outcome.RecoveryMessage);
            }
            catch { }
            try { string text = File.ReadAllText(Path.Combine(job.Directory, "failure.txt")); return text.Length <= 2048 ? text : text.Substring(0, 2048); }
            catch { return UiText.Get("请稍后重试"); }
        }
    }
}
