using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

namespace CodexAutoResume
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Rectangle { get { return System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT
        { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
        { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint size, time; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        internal static List<IntPtr> TopWindows()
        {
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr unused)
            { RECT r; if (IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero && GetWindowRect(h, out r) && (IsIconic(h) || r.Right - r.Left >= 250 && r.Bottom - r.Top >= 160)) windows.Add(h); return true; }, IntPtr.Zero);
            return windows;
        }
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder b, int max);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal static void ValidatePlatform()
        {
            if (!Environment.Is64BitProcess || Marshal.SizeOf(typeof(INPUT)) != 40) throw new InvalidOperationException("The release requires Windows x64; INPUT layout must be 40 bytes.");
            IntPtr module = GetModuleHandle("user32.dll");
            foreach (string name in new string[] { "GetForegroundWindow", "WindowFromPoint", "GetAncestor", "GetCursorPos", "SetCursorPos", "GetWindowRect", "IsWindow", "IsIconic", "GetWindowThreadProcessId", "GetWindowTextW", "RegisterHotKey", "UnregisterHotKey", "SendInput", "GetAsyncKeyState", "GetLastInputInfo", "SetProcessDPIAware" })
                if (module == IntPtr.Zero || GetProcAddress(module, name) == IntPtr.Zero) throw new InvalidOperationException("Missing Windows API: " + name);
        }

        public static void SetDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }
        public static uint Dpi(IntPtr h) { try { return GetDpiForWindow(h); } catch { return 96; } }
        public static string Title(IntPtr h) { StringBuilder s = new StringBuilder(512); GetWindowText(h, s, s.Capacity); return s.ToString(); }
        public static int Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }
        internal static InputSnapshot ReadInput()
        {
            LASTINPUTINFO info = new LASTINPUTINFO { size = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return new InputSnapshot();
            bool held = false;
            for (int key = 1; key < 256; key++)
                if (key != 3 && (GetAsyncKeyState(key) & 0x8000) != 0) { held = true; break; }
            uint age = unchecked((uint)Environment.TickCount - info.time);
            return new InputSnapshot { Valid = age < 0x80000000u, Tick = info.time, IdleMilliseconds = age, Held = held };
        }
        public static bool UserIdle()
        { InputSnapshot input = ReadInput(); return input.Valid && !input.Held && input.IdleMilliseconds >= RecoveryWindow.QuietMilliseconds; }
        public static bool Uncovered(IntPtr h, Rectangle r)
        {
            if (GetForegroundWindow() != h || IsIconic(h)) return false;
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                POINT p = new POINT { X = r.Left + 1 + x * (r.Width - 3) / 2, Y = r.Top + 1 + y * (r.Height - 3) / 2 };
                if (GetAncestor(WindowFromPoint(p), 2) != h) return false;
            }
            return true;
        }
        public static Bitmap Capture(Rectangle r)
        {
            Bitmap b = new Bitmap(r.Width, r.Height);
            try { using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size); return b; }
            catch { b.Dispose(); throw; }
        }

    }

    internal sealed class Target
    {
        public IntPtr Window;
        public int ProcessId;
        public long ProcessStart;
        public string ProcessPath;
        public int Width, Height, OffsetX, OffsetY, CropSize;
        public uint Dpi;
        public bool Demo;
        public bool Automatic;
        internal readonly RegionSelection selection = new RegionSelection();
        internal int CandidateCount = -1;
        internal void RequestRegionChoice() { selection.RequestChoice(); }
        internal void CancelRegionChoice() { selection.CancelRequest(); }
        static bool Allowed(Process proc, string path, IntPtr h, bool allowDemo, out bool demo)
        {
            demo = allowDemo && proc.Id == Process.GetCurrentProcess().Id && Native.Title(h) == DemoForm.WindowTitle;
            if (demo) return true;
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return IsCodexIdentity(proc.ProcessName, path, info.CompanyName, info.ProductName);
        }
        internal static bool IsCodexIdentity(string processName, string path, string company, string product)
        {
            if (!String.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase) && !String.Equals(processName, "Codex", StringComparison.OrdinalIgnoreCase)) return false;
            path = (path ?? "").Replace('/', '\\'); company = company ?? ""; product = product ?? "";
            bool knownInstall = path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("OpenAI.ChatGPT_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("\\OpenAI\\ChatGPT\\", StringComparison.OrdinalIgnoreCase) >= 0;
            bool metadata = company.IndexOf("OpenAI", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (product.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0 || product.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0);
            return knownInstall || metadata;
        }
        public static Target AttachForeground(bool allowDemo)
        {
            return FromWindow(Native.GetForegroundWindow(), allowDemo);
        }
        internal static List<Target> FindAvailable(bool allowDemo)
        {
            List<Target> found = new List<Target>();
            foreach (IntPtr window in Native.TopWindows())
            {
                try
                {
                    using (Process process = Process.GetProcessById(Native.Pid(window)))
                    {
                        bool ownDemo = allowDemo && process.Id == Process.GetCurrentProcess().Id && Native.Title(window) == DemoForm.WindowTitle;
                        if (!ownDemo && !String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase) && !String.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    found.Add(FromWindow(window, allowDemo));
                }
                catch { /* A window may close or be inaccessible during discovery. */ }
            }
            return found;
        }
        internal static Target FromWindow(IntPtr h, bool allowDemo)
        {
            using (Process proc = Process.GetProcessById(Native.Pid(h)))
            {
                string path = proc.MainModule.FileName; bool demo;
                if (!Allowed(proc, path, h, allowDemo, out demo)) throw new InvalidOperationException("请先将 Codex 窗口置于前台，再按自动定位快捷键。");
                return new Target { Window = h, ProcessId = proc.Id, ProcessStart = proc.StartTime.ToUniversalTime().Ticks,
                    ProcessPath = path, Demo = demo, Automatic = true };
            }
        }
        public static Target Calibrate(bool allowDemo, int cropSize)
        {
            Native.POINT p; if (!Native.GetCursorPos(out p)) throw new InvalidOperationException("Cannot read cursor.");
            IntPtr h = Native.GetAncestor(Native.WindowFromPoint(p), 2);
            if (h != Native.GetForegroundWindow()) throw new InvalidOperationException("请先切换到 Codex，再标定。");
            using (Process proc = Process.GetProcessById(Native.Pid(h)))
            {
                string path = proc.MainModule.FileName;
                bool demo;
                if (!Allowed(proc, path, h, allowDemo, out demo)) throw new InvalidOperationException("鼠标必须位于已安装的 Codex 窗口（或本工具演示窗口）内。");
                Native.RECT r; if (!Native.GetWindowRect(h, out r)) throw new InvalidOperationException("Cannot read target bounds.");
                Rectangle crop = new Rectangle(p.X - cropSize / 2, p.Y - cropSize / 2, cropSize, cropSize);
                if (!r.Rectangle.Contains(crop) || !Native.Uncovered(h, crop)) throw new InvalidOperationException("按钮区域不完整或被遮挡，请重新标定。");
                return new Target { Window = h, ProcessId = proc.Id, ProcessStart = proc.StartTime.ToUniversalTime().Ticks,
                    ProcessPath = path, Width = r.Right - r.Left, Height = r.Bottom - r.Top, Dpi = Native.Dpi(h),
                    OffsetX = p.X - r.Left, OffsetY = p.Y - r.Top, CropSize = cropSize, Demo = demo };
            }
        }
        public bool Locate(Recognizer recognizer, Func<Bitmap, List<LocatedButton>, int> choose, out Rectangle crop, out string reason)
        {
            crop = Rectangle.Empty; reason = "";
            if (!Native.IsWindow(Window)) { reason = "原窗口已关闭，请重新标定"; return false; }
            try
            {
                using (Process p = Process.GetProcessById(ProcessId))
                    if (Native.Pid(Window) != ProcessId || p.StartTime.ToUniversalTime().Ticks != ProcessStart || p.MainModule.FileName != ProcessPath)
                    { reason = "进程已变化，请重新标定"; return false; }
            }
            catch { reason = "无法核实原进程，请重新标定"; return false; }
            Native.RECT r;
            if (!Native.GetWindowRect(Window, out r)) { reason = "无法读取窗口位置"; return false; }
            if (Native.GetForegroundWindow() != Window || Native.IsIconic(Window))
            { reason = "等待 Codex 在前台且按钮未被遮挡"; return false; }
            if (Automatic)
            {
                // Fresh coordinates every frame: moving/resizing the window never reuses old pixels.
                Rectangle screen = System.Windows.Forms.SystemInformation.VirtualScreen;
                Rectangle visible = Rectangle.Intersect(r.Rectangle, screen);
                int top = Math.Max(visible.Top, r.Bottom - Math.Max(200, (int)(r.Rectangle.Height * .45)));
                Rectangle search = Rectangle.FromLTRB(visible.Left, top, visible.Right, visible.Bottom);
                if (search.Width < 64 || search.Height < 64) { reason = "窗口可见区域过小"; return false; }
                using (Bitmap scan = Native.Capture(search))
                {
                    List<LocatedButton> buttons = recognizer.FindButtons(scan);
                    buttons.RemoveAll(delegate(LocatedButton b) { Rectangle region = b.Crop; region.Offset(search.Location); return !Native.Uncovered(Window, region); });
                    CandidateCount = buttons.Count;
                    Native.RECT after;
                    if (!Native.GetWindowRect(Window, out after) || after.Rectangle != r.Rectangle || Native.GetForegroundWindow() != Window)
                    { reason = "窗口正在变化，等待重新定位"; return false; }
                    int index = selection.Resolve(buttons, scan.Size);
                    if (index == -2) { reason = "正在自动寻找蓝色圆按钮；找不到时可用手动标定备用"; return false; }
                    if (index == -1)
                    {
                        if (choose == null) { reason = "候选区域需要用户确认"; return false; }
                        index = choose(scan, buttons);
                        if (index < 0) { selection.CancelRequest(); reason = "已取消区域选择"; return false; }
                        selection.Confirm(index, buttons, scan.Size);
                        // The dialog changed focus. Require another fresh scan after user returns.
                        reason = "区域已确认，请切回 Codex 继续监控"; return false;
                    }
                    crop = buttons[index].Crop; crop.Offset(search.Location); return true;
                }
            }
            if (r.Right - r.Left != Width || r.Bottom - r.Top != Height || Native.Dpi(Window) != Dpi)
            { reason = "窗口大小或缩放已变化，请重新标定"; return false; }
            crop = new Rectangle(r.Left + OffsetX - CropSize / 2, r.Top + OffsetY - CropSize / 2, CropSize, CropSize);
            if (!Native.Uncovered(Window, crop)) { reason = "等待 Codex 在前台且按钮未被遮挡"; return false; }
            return true;
        }

        internal bool ValidateIdentity(out string reason)
        {
            if (!ValidateProcessIdentity(out reason)) return false;
            if (Native.IsIconic(Window)) { reason = "Codex 已最小化，等待还原，监测继续"; return false; }
            if (!Native.IsWindowVisible(Window)) { reason = "Codex 窗口已隐藏，等待重新显示，监测继续"; return false; }
            return true;
        }
        internal bool ValidateProcessIdentity(out string reason)
        {
            reason = "";
            if (!Native.IsWindow(Window)) { reason = "原窗口已关闭，请重新连接"; return false; }
            try
            {
                using (Process p = Process.GetProcessById(ProcessId))
                    if (Native.Pid(Window) != ProcessId || p.StartTime.ToUniversalTime().Ticks != ProcessStart || p.MainModule.FileName != ProcessPath)
                    { reason = "进程已变化，请重新连接"; return false; }
            }
            catch { reason = "无法核实原进程，请重新连接"; return false; }
            return true;
        }

        internal bool LocateBackground(WindowFrame frame, Recognizer recognizer, Func<Bitmap, List<LocatedButton>, int> choose, out Rectangle crop, out string reason, bool requireFresh = true)
        {
            crop = Rectangle.Empty;
            if (!ValidateIdentity(out reason)) return false;
            if ((DateTime.UtcNow - frame.CapturedAt).TotalSeconds > 2 || WindowCapture.Bounds(Window) != frame.Bounds)
            { reason = "捕获帧已过期或窗口已移动，等待新帧"; return false; }
            if (requireFresh && !frame.ContentFresh(DateTime.UtcNow)) { reason = "操作前需要重新获取画面"; return false; }
            if (!Automatic)
            {
                Native.RECT rect; if (!Native.GetWindowRect(Window, out rect)) return false;
                if (rect.Right - rect.Left != Width || rect.Bottom - rect.Top != Height || Native.Dpi(Window) != Dpi)
                { reason = "窗口大小或缩放已变化，请重新标定"; return false; }
                crop = new Rectangle(rect.Left + OffsetX - CropSize / 2, rect.Top + OffsetY - CropSize / 2, CropSize, CropSize);
                if (!frame.Bounds.Contains(crop)) { reason = "标定区域超出捕获边界"; return false; }
                return true;
            }
            int top = Math.Max(0, frame.Image.Height - Math.Max(200, (int)(frame.Image.Height * .45)));
            Rectangle search = new Rectangle(0, top, frame.Image.Width, frame.Image.Height - top);
            using (Bitmap scan = frame.Image.Clone(search, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                List<LocatedButton> buttons = recognizer.FindButtons(scan);
                CandidateCount = buttons.Count;
                int index = selection.Resolve(buttons, scan.Size);
                if (index == -2) { reason = "后台捕获正常，正在寻找蓝色圆按钮"; return false; }
                if (index == -1)
                {
                    if (choose == null) { reason = "候选区域需要用户确认"; return false; }
                    index = choose(scan, buttons);
                    if (index < 0) { selection.CancelRequest(); reason = "已取消区域选择"; return false; }
                    selection.Confirm(index, buttons, scan.Size);
                    reason = "区域已确认，等待新的后台帧"; return false;
                }
                crop = buttons[index].Crop;
                crop.Offset(frame.Bounds.Left, frame.Bounds.Top + top);
                return true;
            }
        }
    }
}
