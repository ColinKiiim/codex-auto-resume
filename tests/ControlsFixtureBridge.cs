using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
class ControlsFixtureBridge
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc c, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder b, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder name,int length);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput value);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint size, tick; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int l,t,r,b; }
    static void Print(object value) { using(var w=new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))){w.WriteLine(new JavaScriptSerializer().Serialize(value));} }
    [STAThread] static int Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        if(args[0]=="locate")
        {
            IntPtr window=IntPtr.Zero;
            EnumWindows(delegate(IntPtr h,IntPtr ignored){var s=new StringBuilder(512);GetWindowText(h,s,512);if(s.ToString()==args[1]){if(window!=IntPtr.Zero)throw new Exception("ambiguous fixture");window=h;}return true;},IntPtr.Zero);
            if(window==IntPtr.Zero){Print(new {found=false});return 0;}
            uint pid;GetWindowThreadProcessId(window,out pid);using(var p=Process.GetProcessById((int)pid)) Print(new {found=true,Window=window.ToInt64(),ProcessId=p.Id,ProcessStart=p.StartTime.ToUniversalTime().Ticks.ToString(),ProcessPath=p.MainModule.FileName});return 0;
        }
        if(args[0]=="state")
        {
            Point cursor;GetCursorPos(out cursor);var input=new LastInput{size=(uint)Marshal.SizeOf(typeof(LastInput))};GetLastInputInfo(ref input);
            bool held=false;for(int n=1;n<256;n++)if(n!=3&&(GetAsyncKeyState(n)&0x8000)!=0){held=true;break;}
            uint foregroundPid;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);var css=new StringBuilder(256);GetClassName(GetForegroundWindow(),css,256);
            Print(new {foreground=GetForegroundWindow().ToInt64(),foregroundPid,foregroundClass=css.ToString(),minimized=IsIconic(new IntPtr(long.Parse(args[1]))),x=cursor.X,y=cursor.Y,idle=unchecked((uint)Environment.TickCount-input.tick),tick=input.tick,held});return 0;
        }
        if(args[0]=="minimize") { ShowWindow(new IntPtr(long.Parse(args[1])),6);Print(new { minimized=IsIconic(new IntPtr(long.Parse(args[1]))) });return 0; }
        if(args[0]=="discover")
        {
            var assembly=System.Reflection.Assembly.LoadFrom(args[1]);
            var method=assembly.GetType("CodexAutoResume.Native").GetMethod("TopWindows",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            bool found=false;foreach(IntPtr window in (System.Collections.IEnumerable)method.Invoke(null,null))if(window.ToInt64()==long.Parse(args[2]))found=true;
            Print(new { found });return 0;
        }
        if(args[0]=="cover")
        {
            Rect r;GetWindowRect(new IntPtr(long.Parse(args[1])),out r);using(var form=new Form{Text="CAR owned control test cover",TopMost=true,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(r.l-10,r.t-10,r.r-r.l+20,r.b-r.t+20),BackColor=Color.Magenta})
            using(var timer=new Timer{Interval=100})
            {DateTime deadline=DateTime.UtcNow.AddSeconds(90);timer.Tick+=delegate{if(File.Exists(args[2])||DateTime.UtcNow>deadline)form.Close();};form.Shown+=delegate{form.Activate();timer.Start();};Application.Run(form);}return 0;
        }
        return 1;
    }
}
