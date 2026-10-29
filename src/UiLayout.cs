using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class UiStyle
    {
        internal static Color Background { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(248, 249, 251); } }
        internal static Color Secondary { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(86, 96, 110); } }
        internal static Color Accent { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(28, 98, 185); } }
        internal static int Px(Control control, int value)
        { using (Graphics graphics = control.CreateGraphics()) return (int)Math.Round(value * graphics.DpiX / 96f); }
        internal static Label Section(string text)
        { return new SectionLabel { Text = text, AutoSize = true, Margin = new Padding(0, 16, 0, 8), ForeColor = SystemColors.ControlText }; }
        internal static Label Help(string text)
        { return new Label { Text = text, AutoSize = true, ForeColor = Secondary, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false }; }
        internal static FlowLayoutPanel Content()
        { return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20, 0, 20, 20), Margin = Padding.Empty }; }
        internal static FlowLayoutPanel Actions()
        { return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 4, 0, 8) }; }
    }

    internal sealed class SectionLabel : Label
    {
        Control observed;
        Font ownedFont;
        void MatchFont(object sender, EventArgs e)
        {
            if (observed == null) return;
            Font previous = ownedFont;
            ownedFont = new Font(observed.Font, FontStyle.Bold); Font = ownedFont;
            if (previous != null) previous.Dispose();
        }
        protected override void OnParentChanged(EventArgs e)
        {
            if (observed != null) observed.FontChanged -= MatchFont;
            base.OnParentChanged(e); observed = Parent;
            if (observed != null) { observed.FontChanged += MatchFont; MatchFont(null, EventArgs.Empty); }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (observed != null) observed.FontChanged -= MatchFont; if (ownedFont != null) ownedFont.Dispose(); } base.Dispose(disposing); }
    }

    // A checkbox and its threshold are one field. Do not stretch each fragment
    // independently, which can separate a value from its label or unit.
    internal sealed class ThresholdRow : Panel
    {
        readonly CheckBox option;
        readonly FlowLayoutPanel value;
        internal ThresholdRow(CheckBox option, NumericUpDown number)
        {
            this.option = option; Name = number.Name + "Row"; Margin = new Padding(0, 0, 0, 8);
            value = UiStyle.Actions(); value.WrapContents = false; value.Margin = Padding.Empty;
            value.Controls.Add(new Label { Text = "剩余 ≤", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
            number.Margin = new Padding(0, 1, 6, 0); value.Controls.Add(number);
            value.Controls.Add(new Label { Text = "% 时暂停", AutoSize = true, Margin = new Padding(0, 5, 0, 0) });
            option.Margin = Padding.Empty; Controls.Add(option); Controls.Add(value);
        }
        internal void Fit(int width)
        {
            Width = width; option.AutoSize = true;
            Size label = option.GetPreferredSize(Size.Empty), editor = value.GetPreferredSize(Size.Empty);
            int gap = UiStyle.Px(this, 16);
            bool stacked = label.Width + gap + editor.Width > width;
            option.SetBounds(0, 0, label.Width, label.Height);
            value.SetBounds(stacked ? UiStyle.Px(this, 24) : label.Width + gap, stacked ? label.Height + 4 : 0, editor.Width, editor.Height);
            Height = Math.Max(option.Bottom, value.Bottom) + 2;
        }
    }

    internal sealed class EditorTextBox : TextBox
    {
        [StructLayout(LayoutKind.Sequential)] struct ScrollInfo { public uint Size, Mask; public int Min, Max; public uint Page; public int Position, Track; }
        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x20A)
            {
                ScrollInfo info = new ScrollInfo { Size = (uint)Marshal.SizeOf(typeof(ScrollInfo)), Mask = 0x17 };
                int delta = (short)((long)message.WParam >> 16);
                bool exists = GetScrollInfo(Handle, 1, ref info);
                if (!exists || (delta > 0 ? info.Position <= info.Min : info.Position >= info.Max - (int)info.Page + 1))
                    for (Control parent = Parent; parent != null; parent = parent.Parent)
                    {
                        var scroll = parent as ScrollableControl;
                        if (scroll != null && scroll.AutoScroll && scroll.VerticalScroll.Visible)
                        { SendMessage(scroll.Handle, message.Msg, message.WParam, message.LParam); return; }
                    }
            }
            base.WndProc(ref message);
        }
    }
}
