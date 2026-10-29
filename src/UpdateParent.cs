using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexAutoResume
{
    // Kernel-owned process information, not module/PEB memory, stays queryable
    // during teardown. Keep one handle from identity verification through wait
    // so a reused PID can never change which process we are waiting for.
    internal static class UpdateParent
    {
        internal sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public Handle() : base(true) { }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern Handle OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(Handle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(Handle handle, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(Handle handle, uint flags, StringBuilder path, ref int length);
        static Handle Open(UpdateJob job)
        {
            if (job.ParentId <= 0 || job.ParentStart <= 0) throw new InvalidDataException("更新原进程身份无效");
            Handle handle = OpenProcess(0x00100000 | 0x1000, false, job.ParentId); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            if (!handle.IsInvalid) return handle;
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error == 87) return null; // No such PID. Access denied is NOT proof of exit.
            throw new Win32Exception(error);
        }
        static bool Exited(Handle handle)
        {
            uint result = WaitForSingleObject(handle, 0);
            if (result == 0) return true;
            if (result == 258) return false;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        static void Identity(Handle handle, UpdateJob job)
        {
            long created, exited, kernel, user;
            if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (DateTime.FromFileTimeUtc(created).Ticks != job.ParentStart) throw new InvalidDataException("待更新进程创建时间不匹配");
        }
        internal static void Wait(UpdateJob job, int milliseconds, Action validated = null)
        {
            using (Handle handle = Open(job))
            {
                if (handle == null) { if (validated != null) validated(); return; }
                Identity(handle, job);
                if (Exited(handle)) { if (validated != null) validated(); return; }
                var image = new StringBuilder(32768); int length = image.Capacity;
                if (!QueryFullProcessImageName(handle, 0, image, ref length))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (Exited(handle)) { if (validated != null) validated(); return; } // Same verified process exited during the query.
                    throw new Win32Exception(error);
                }
                if (!String.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(job.Destination), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("待更新进程路径不匹配");
                if (validated != null) validated();
                uint result = WaitForSingleObject(handle, (uint)milliseconds);
                if (result == 258) throw new IOException("程序未及时退出，已取消替换");
                if (result != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        internal static bool Gone(UpdateJob job)
        {
            try { using (Handle handle = Open(job)) { if (handle == null) return true; Identity(handle, job); return Exited(handle); } }
            catch { return false; } // Unknown identity/access must not trigger a second instance.
        }
    }
}
