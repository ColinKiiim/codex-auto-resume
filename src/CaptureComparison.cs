using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CodexAutoResume
{
    internal static class CaptureComparison
    {
        internal static async Task RunAsync(Target target, CaptureSampler sampler, string directory)
        {
            Directory.CreateDirectory(directory);
            StringBuilder report = new StringBuilder("Read-only capture comparison; " + DateTimeOffset.Now.ToString("O") + Environment.NewLine);
            Recognizer recognizer = new Recognizer();
            for (int i = 0; i < 3; i++)
            {
                string reason;
                if (!target.ValidateIdentity(out reason)) throw new InvalidOperationException(reason);
                using (WindowFrame frame = i == 1 ? await sampler.ReadActionAsync(target.Window) : await sampler.ReadAsync(target.Window))
                {
                    if (!target.ValidateIdentity(out reason) || WindowCapture.Bounds(target.Window) != frame.Bounds)
                        throw new InvalidOperationException("目标在捕获期间变化，已取消");
                    string name = i == 1 ? "02-independent" : i == 0 ? "01-session" : "03-session-after";
                    string path = Path.Combine(directory, name + ".png"); frame.Image.Save(path, ImageFormat.Png);
                    string digest;
                    using (SHA256 hash = SHA256.Create()) using (Stream input = File.OpenRead(path))
                        digest = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
                    report.AppendLine(name + "; source=" + frame.Source + "; captured=" + frame.CapturedAt.ToString("O") +
                        "; content=" + frame.ContentAt.ToString("O") + "; content-sequence=" + frame.ContentSequence +
                        "; bounds=" + frame.Bounds + "; foreground=" + (Native.GetForegroundWindow() == target.Window) + "; sha256=" + digest);
                    Rectangle crop;
                    if (target.LocateBackground(frame, recognizer, null, out crop, out reason, false))
                    {
                        crop.Offset(-frame.Bounds.X, -frame.Bounds.Y);
                        using (Bitmap badge = frame.Image.Clone(crop, PixelFormat.Format32bppRgb))
                        {
                            badge.Save(Path.Combine(directory, name + "-badge.png"), ImageFormat.Png);
                            report.AppendLine("crop=" + crop + "; state=" + recognizer.Classify(badge).State + "; pixels=" + CaptureWatchdog.Signature(badge));
                        }
                    }
                    else report.AppendLine("no selected crop: " + reason);
                    File.WriteAllText(Path.Combine(directory, "comparison.txt"), report.ToString(), new UTF8Encoding(true));
                }
                await Task.Delay(200);
            }
            report.AppendLine("No clicks, pointer movement, activation or target configuration changes were requested.");
            File.WriteAllText(Path.Combine(directory, "comparison.txt"), report.ToString(), new UTF8Encoding(true));
        }
    }
}
