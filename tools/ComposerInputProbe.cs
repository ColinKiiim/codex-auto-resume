using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Manual, opt-in diagnostic, separate from the CAR monitoring loop. The only
// activated window is our own cover; never activates Codex or sends a message.
internal static class ComposerInputProbe
{
    static volatile bool cancelled;
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct GUIINFO
    { public int Size; public uint Flags; public IntPtr Active, Focus, Capture, Menu, Move, Caret; public RECT CaretBounds; }
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUT { public uint Size, Tick; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUIINFO info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT bounds);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUT info);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    sealed class InputState
    {
        internal IntPtr Foreground, Focus; internal POINT Cursor; internal uint Tick;
        internal static InputState Read()
        {
            GUIINFO gui = new GUIINFO { Size = Marshal.SizeOf(typeof(GUIINFO)) };
            LASTINPUT input = new LASTINPUT { Size = (uint)Marshal.SizeOf(typeof(LASTINPUT)) }; POINT cursor;
            if (!GetGUIThreadInfo(0, ref gui) || !GetCursorPos(out cursor) || !GetLastInputInfo(ref input))
                throw new InvalidOperationException("Cannot read foreground/focus/input evidence");
            return new InputState { Foreground = GetForegroundWindow(), Focus = gui.Focus, Cursor = cursor, Tick = input.Tick };
        }
        internal bool Same(InputState other)
        { return Foreground == other.Foreground && Focus == other.Focus && Cursor.X == other.Cursor.X && Cursor.Y == other.Cursor.Y && Tick == other.Tick; }
        internal string Difference(InputState other)
        { return "ForegroundSame=" + (Foreground == other.Foreground) + "; FocusSame=" + (Focus == other.Focus) + "; PointerSame=" + (Cursor.X == other.Cursor.X && Cursor.Y == other.Cursor.Y) + "; LastInputSame=" + (Tick == other.Tick); }
    }

    static bool WaitFor(Func<bool> condition)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do { if (condition()) return true; Thread.Sleep(80); } while (clock.ElapsedMilliseconds < 3000);
        return false;
    }

    static void VerifyTarget(Process process, IntPtr window, long started, IntPtr cover)
    {
        process.Refresh(); RECT targetBounds, coverBounds;
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != started || process.MainWindowHandle != window || IsIconic(window) ||
            !GetWindowRect(window, out targetBounds) || !GetWindowRect(cover, out coverBounds) ||
            coverBounds.L > targetBounds.L || coverBounds.T > targetBounds.T || coverBounds.R < targetBounds.R || coverBounds.B < targetBounds.B)
            throw new InvalidOperationException("Target changed, minimized or not fully covered");
    }

    static void RoundTrip(Process process, IntPtr window, long started, IntPtr cover, int port)
    {
        using (ComposerDebugInput editor = new ComposerDebugInput(port))
        {
            if (Convert.ToInt32(editor.State()["textLength"]) != 0) throw new InvalidOperationException("Composer has a draft; refusing to write");
            for (int i = 0; i < 40; i++)
            {
                InputState idle = InputState.Read();
                if (cancelled || idle.Foreground != cover) throw new InvalidOperationException("Cover lost foreground");
                if (unchecked((uint)Environment.TickCount - idle.Tick) >= 1500) break;
                if (i == 39) throw new InvalidOperationException("Input did not become idle");
                Thread.Sleep(100);
            }
            InputState before = InputState.Read(); bool attempted = false;
            string sample = "CAR 无焦点写入验证 " + Guid.NewGuid().ToString("N").Substring(0, 8) + "（不发送）中文 Unicode";
            try
            {
                VerifyTarget(process, window, started, cover);
                if (cancelled || before.Foreground != cover || !before.Same(InputState.Read())) throw new InvalidOperationException("Input changed before write");
                attempted = true; editor.Write(sample);
                Console.WriteLine("AfterCdpInput: " + before.Difference(InputState.Read()));
                if (!WaitFor(delegate { return editor.Matches(sample); })) throw new InvalidOperationException("Editor DOM did not match the temporary draft");
                Console.WriteLine("EditorDomReadback=True");
                for (int i = 0; i < 5; i++)
                {
                    Thread.Sleep(200); InputState after = InputState.Read();
                    if (cancelled || !before.Same(after)) throw new InvalidOperationException("Input changed: " + before.Difference(after));
                }
            }
            finally
            {
                if (attempted)
                {
                    if (editor.Matches(sample))
                    {
                        editor.Clear(sample);
                        if (!WaitFor(delegate { return Convert.ToInt32(editor.State()["textLength"]) == 0; })) throw new InvalidOperationException("Clearing was not confirmed");
                        Console.WriteLine("TestDraftCleared=True");
                    }
                    else throw new InvalidOperationException("Draft outcome changed or is unknown; not overwritten. Inspect composer");
                }
            }
            InputState cleaned = InputState.Read(); Console.WriteLine("AfterCleanup: " + before.Difference(cleaned));
            if (!before.Same(cleaned)) throw new InvalidOperationException("Input changed during cleanup");
            Console.WriteLine("PASS: background CDP input; OpaqueCover=True; no focus change or chat submission");
        }
    }

    [STAThread] static int Main(string[] args)
    {
        try
        {
            int port = 0; bool write = false;
            foreach (string arg in args)
                if (arg.StartsWith("--debug-port=", StringComparison.Ordinal)) port = Int32.Parse(arg.Substring(13));
                else if (arg == "--write-and-clear") write = true;
                else throw new ArgumentException("Unknown option");
            if (port < 1 || port > 65535) throw new ArgumentException("An already enabled Codex loopback debug port is required");
            Process chosen = null;
            foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            {
                if (process.MainWindowHandle == IntPtr.Zero || process.MainModule.FileName.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) < 0)
                { process.Dispose(); continue; }
                if (chosen != null) throw new InvalidOperationException("Multiple installed Codex windows");
                chosen = process;
            }
            if (chosen == null) throw new InvalidOperationException("No installed Codex window");
            using (chosen)
            {
                if (!write)
                { using (var editor = new ComposerDebugInput(port)) Console.WriteLine(new JavaScriptSerializer().Serialize(editor.State())); return 0; }
                IntPtr window = chosen.MainWindowHandle; long started = chosen.StartTime.ToUniversalTime().Ticks;
                SetProcessDPIAware(); Application.EnableVisualStyles(); RECT bounds;
                if (!GetWindowRect(window, out bounds) || IsIconic(window)) throw new InvalidOperationException("Cannot cover target");
                Exception failure = null;
                using (Form cover = new Form { Text = "CAR 后台输入验证（不会发送）", StartPosition = FormStartPosition.Manual,
                    Bounds = Rectangle.FromLTRB(bounds.L, bounds.T, bounds.R, bounds.B), TopMost = true, BackColor = Color.White })
                {
                    cover.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 16),
                        Text = "CAR 无焦点后台输入验证\n\n请暂时不要操作鼠标键盘\n仅写入并清除临时文字，不发送消息\n完成后此遮挡窗口自动关闭" });
                    bool working = true;
                    cover.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (working) { cancelled = true; e.Cancel = true; } };
                    cover.Shown += async delegate
                    {
                        IntPtr coverHandle = cover.Handle;
                        try { cover.Activate(); await Task.Delay(800); await Task.Run(delegate { RoundTrip(chosen, window, started, coverHandle, port); }); }
                        catch (Exception e) { failure = e; }
                        finally { working = false; cover.Close(); }
                    };
                    Application.Run(cover);
                }
                if (failure != null) throw failure;
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e.GetType().Name + ": " + e.Message); return 1; }
    }
}
