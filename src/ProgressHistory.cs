using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAutoResume
{
    // Keep the reader's viewport and selection when new progress arrives.
    internal sealed class ProgressHistory : TextBox
    {
        bool followLatest = true;
        [StructLayout(LayoutKind.Sequential)]
        struct ScrollInfo { public uint Size, Mask; public int Min, Max; public uint Page; public int Position, Track; }
        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal int FirstVisibleLine { get { return (int)SendMessage(Handle, 0xCE, IntPtr.Zero, IntPtr.Zero); } }
        internal bool AtBottom
        {
            get
            {
                ScrollInfo info = new ScrollInfo { Size = (uint)Marshal.SizeOf(typeof(ScrollInfo)), Mask = 0x17 };
                return !GetScrollInfo(Handle, 1, ref info) || info.Position >= info.Max - (int)info.Page;
            }
        }
        internal void UpdateHistory(string content, bool replace)
        {
            bool follow = !IsHandleCreated || AtBottom;
            int first = IsHandleCreated ? FirstVisibleLine : 0;
            int start = SelectionStart, length = SelectionLength;
            if (IsHandleCreated) SendMessage(Handle, 11, IntPtr.Zero, IntPtr.Zero);
            try
            {
                if (replace) Text = content; else AppendText((TextLength == 0 ? "" : Environment.NewLine) + content);
                if (follow) { Select(TextLength, 0); ScrollToCaret(); SendMessage(Handle, 0x115, new IntPtr(7), IntPtr.Zero); }
                else
                {
                    Select(Math.Min(start, TextLength), Math.Min(length, Math.Max(0, TextLength - start)));
                    SendMessage(Handle, 0xB6, IntPtr.Zero, new IntPtr(first - FirstVisibleLine));
                }
            }
            finally { followLatest = follow; if (IsHandleCreated) { SendMessage(Handle, 11, new IntPtr(1), IntPtr.Zero); Invalidate(); } }
        }
        internal void RestoreView(int first, int start, int length)
        {
            Select(Math.Min(start, TextLength), Math.Min(length, Math.Max(0, TextLength - start)));
            if (IsHandleCreated) SendMessage(Handle, 0xB6, IntPtr.Zero, new IntPtr(first - FirstVisibleLine));
            followLatest = false;
        }
        void FollowAfterLayout()
        { if (IsHandleCreated && followLatest) { Select(TextLength, 0); ScrollToCaret(); SendMessage(Handle, 0x115, new IntPtr(7), IntPtr.Zero); } }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FollowAfterLayout(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FollowAfterLayout(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x115 || message.Msg == 0x20A || message.Msg == 0x101 || message.Msg == 0x202)
                followLatest = AtBottom;
        }
    }
}
