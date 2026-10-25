using System;
using System.Drawing;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("CodexAutoResume")]
[assembly: AssemblyDescription("Portable Codex resume button monitor")]
[assembly: AssemblyProduct("CodexAutoResume")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]
[assembly: AssemblyCopyright("Copyright (c) 2026 CodexAutoResume contributors. MIT License.")]

namespace CodexAutoResume
{
    internal static class Portable
    {
        public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAutoResume");
        public static string StateName(ButtonState state)
        {
            switch (state)
            {
                case ButtonState.Resume: return "向右三角形 · 可恢复";
                case ButtonState.Running: return "白色方块 · 运行中";
                case ButtonState.Send: return "向上箭头 · 发送";
                case ButtonState.Voice: return "声波图标 · 语音";
                default: return "未知图形";
            }
        }
        // Release test: copy ONLY the EXE to an unrelated directory and execute this check.
        public static int SelfCheck(string reportPath)
        {
            try
            {
                File.WriteAllText(reportPath, BuildReport()); return 0;
            }
            catch (Exception e) { File.WriteAllText(reportPath, "FAIL: " + e); return 1; }
        }
        public static string BuildReport()
        {
                Native.ValidatePlatform();
                Recognizer recognizer = new Recognizer();
                string[] names = { "resume", "running", "send", "send-current", "voice" };
                ButtonState[] expected = { ButtonState.Resume, ButtonState.Running, ButtonState.Send, ButtonState.Send, ButtonState.Voice };
                string report = "CodexAutoResume 1.5.0 portable self-check" + Environment.NewLine;
                report += "Windows Graphics Capture available: " + WindowCapture.Supported + Environment.NewLine;
                try { report += "Offline question OCR: " + QuestionDetector.PlatformSummary() + Environment.NewLine; }
                catch { report += "Offline question OCR: unavailable; icon monitoring remains available" + Environment.NewLine; }
                if (DesktopIdentity.CurrentId() != DesktopIdentity.AppId) throw new InvalidOperationException("Taskbar identity was not set.");
                report += "PASS: independent taskbar AppUserModelID = " + DesktopIdentity.CurrentId() + Environment.NewLine;
                using (Icon icon = DesktopIdentity.LoadIcon()) if (icon.Width < 16) throw new InvalidOperationException("Embedded icon invalid.");
                string license = DesktopIdentity.LicenseText();
                if (!license.Contains("MIT License") || !license.Contains("Permission is hereby granted")) throw new InvalidOperationException("MIT license missing.");
                report += "PASS: application icon and MIT license embedded." + Environment.NewLine;
                report += "Windows: " + Environment.OSVersion.VersionString + "; process: x64; CLR: " + Environment.Version + Environment.NewLine;
                report += "PASS: required Win32 exports available; INPUT size = 40 bytes." + Environment.NewLine;
                Assembly assembly = Assembly.GetExecutingAssembly();
                for (int i = 0; i < names.Length; i++)
                using (Stream stream = assembly.GetManifestResourceStream(names[i] + "-reference.png"))
                using (Bitmap bitmap = new Bitmap(stream))
                {
                    Detection d = recognizer.Classify(bitmap);
                    if (d.State != expected[i]) throw new InvalidOperationException(names[i] + " misclassified: " + d);
                    report += names[i] + ": " + d.State + Environment.NewLine;
                }
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name != "mscorlib" && reference.Name != "System" && !reference.Name.StartsWith("System.", StringComparison.Ordinal) && reference.Name != "Windows.Graphics" && reference.Name != "Windows.Foundation" && reference.Name != "Windows.Media" && reference.Name != "Windows.Storage" && reference.Name != "Windows.Globalization" && reference.Name != "WindowsBase" && reference.Name != "UIAutomationClient" && reference.Name != "UIAutomationTypes")
                        throw new InvalidOperationException("Unexpected external assembly: " + reference.Name);
                    report += "Framework: " + reference.Name + Environment.NewLine;
                }
                using (System.Windows.Forms.Form form = new System.Windows.Forms.Form())
                {
                    form.Controls.Add(new System.Windows.Forms.Button { Text = "自检" });
                    if (form.Handle == IntPtr.Zero) throw new InvalidOperationException("Cannot create a WinForms window.");
                    DesktopIdentity.ApplyToWindow(form.Handle);
                    if (DesktopIdentity.WindowId(form.Handle) != DesktopIdentity.AppId) throw new InvalidOperationException("Window taskbar identity mismatch.");
                    DesktopIdentity.ClearWindow(form.Handle);
                }
                report += "PASS: WinForms window and control creation." + Environment.NewLine;
                report += "PASS: window-level taskbar identity write/read/cleanup." + Environment.NewLine;
                Directory.CreateDirectory(DataDirectory);
                string probe = Path.Combine(DataDirectory, "write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
                try { File.WriteAllText(probe, "CodexAutoResume"); if (File.ReadAllText(probe) != "CodexAutoResume") throw new IOException("User data readback failed."); }
                finally { if (File.Exists(probe)) File.Delete(probe); }
                report += "PASS: current user's data directory read/write." + Environment.NewLine;
                report += "PASS: all five embedded images loaded; only Windows/.NET Framework dependencies." + Environment.NewLine;
                return report;
        }
    }
}
