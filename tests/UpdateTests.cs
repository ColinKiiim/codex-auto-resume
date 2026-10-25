using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexAutoResume
{
    internal static class UpdateTests
    {
        static bool Rejects(Action action) { try { action(); return false; } catch { return true; } }
        internal static void Run(Action<bool, string> check, string root)
        {
            string version = "1.1.99", tag = "v" + version, asset = "CodexAutoResume-" + tag + "-windows-x64.exe";
            string url = "https://github.com/" + Updates.Repository + "/releases/download/" + tag + "/";
            string json = new JavaScriptSerializer().Serialize(new { tag_name = tag, draft = false, prerelease = false, body = "测试更新", assets = new[] { new { name = asset, browser_download_url = url + asset }, new { name = "SHA256SUMS.txt", browser_download_url = url + "SHA256SUMS.txt" } } });
            check(Updates.ParseRelease(json, "1.1.9").Version == version, "numeric version comparison handles 9 to 99");
            check(Updates.ParseRelease(json, version) == null && Updates.ParseRelease(json, "2.0.0") == null, "same version and downgrade produce no update");
            check(Updates.ParseRelease(json.Replace("\"draft\":false", "\"draft\":true"), "1.1.0") == null, "draft release ignored");
            check(Updates.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true"), "1.1.0") == null, "prerelease ignored");
            check(Rejects(delegate { Updates.ParseRelease(json.Replace("github.com/", "example.org/"), "1.1.0"); }), "foreign download source rejected");
            check(Rejects(delegate { Updates.ParseRelease(json.Replace("SHA256SUMS.txt", "missing.txt"), "1.1.0"); }), "incomplete release cannot update");
            check(Rejects(delegate { Updates.ParseVersion("1.1.8-preview"); }) && Rejects(delegate { Updates.ParseVersion("1.1"); }), "only stable three-part versions accepted");
            string hash = new string('a', 64), sum = hash + "  " + asset;
            check(Updates.ExpectedHash(sum + "\r\n", asset) == hash, "checksum parser selects exact EXE");
            check(Rejects(delegate { Updates.ExpectedHash(sum + "\n" + sum, asset); }), "duplicate checksum rejected");
            check(Rejects(delegate { Updates.ExpectedHash(hash + "  ../" + asset, asset); }), "checksum path traversal rejected");
            using (MemoryStream input = new MemoryStream(new byte[100]))
            using (MemoryStream output = new MemoryStream())
                check(Rejects(delegate { Updates.CopyBounded(input, output, 20, CancellationToken.None).GetAwaiter().GetResult(); }), "oversized update response rejected");
            using (MemoryStream input = new MemoryStream(new byte[100]))
            using (MemoryStream output = new MemoryStream())
                check(Rejects(delegate { Updates.CopyBounded(input, output, 1000, new CancellationToken(true)).GetAwaiter().GetResult(); }), "cancelled download cannot produce executable");
            string directory = Path.Combine(Path.GetTempPath(), "CodexAutoResume-update-unit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                string original = Path.Combine(directory, "便携工具 & user's.exe"), staged = Path.Combine(directory, "new.tmp"), backup = Path.Combine(directory, "old.bak");
                string built = Path.Combine(root, "dist", "CodexAutoResume.exe");
                File.WriteAllText(original, "old executable fixture"); File.Copy(built, staged);
                UpdateJob job = new UpdateJob { Destination = original, Staged = staged, Backup = backup, OldHash = Updates.Hash(original), NewHash = Updates.Hash(staged), Version = Portable.Version };
                string oldHash = job.OldHash;
                check(Rejects(delegate { Updates.ValidateBinary(staged, hash, Portable.Version); }), "corrupt checksum rejected before execution");
                check(Rejects(delegate { Updates.ValidateBinary(staged, job.NewHash, "1.1.0"); }), "EXE metadata must match offered version");
                Updates.Install(job);
                check(Updates.Hash(original) == job.NewHash && Updates.Hash(backup) == oldHash, "atomic installation keeps exact old EXE backup");
                Updates.RollBack(job);
                check(Updates.Hash(original) == oldHash && !File.Exists(backup), "rollback restores exact original executable");
                File.Copy(built, staged); File.AppendAllText(original, "external edit");
                check(Rejects(delegate { Updates.Install(job); }) && File.ReadAllText(original).Contains("external edit"), "concurrent external change prevents replacement");
                check(Rejects(delegate { Updates.LoadJob(Path.Combine(directory, "job.json")); }), "helper rejects jobs outside its owned update root");
            }
            finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }

        }
    }
}
