using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class WindowUi
    {
        internal static void Center(Form dialog, Form owner)
        {
            if (owner == null || owner.IsDisposed) return;
            Rectangle area = Screen.FromControl(owner).WorkingArea;
            // Respect the available work area even when the settings minimum
            // would exceed a small display. Its content remains scrollable.
            dialog.MinimumSize = new Size(Math.Min(dialog.MinimumSize.Width, area.Width), Math.Min(dialog.MinimumSize.Height, area.Height));
            dialog.Size = new Size(Math.Min(dialog.Width, area.Width), Math.Min(dialog.Height, area.Height));
            int x = owner.Left + (owner.Width - dialog.Width) / 2;
            int y = owner.Top + (owner.Height - dialog.Height) / 2;
            dialog.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - dialog.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - dialog.Height)));
        }
        internal static void Show(Form dialog, Form owner)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.ShowInTaskbar = false;
            Center(dialog, owner);
            dialog.Shown += delegate { Center(dialog, owner); };
            dialog.Show(owner);
        }
        internal static DialogResult Modal(Form dialog, Form owner)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.ShowInTaskbar = false;
            Center(dialog, owner);
            dialog.Shown += delegate { Center(dialog, owner); };
            return dialog.ShowDialog(owner);
        }
        internal static void ScrollContent(TabPage page, FlowLayoutPanel content)
        {
            var viewport = new Panel { Name = "SettingsViewport", Dock = DockStyle.Fill, AutoScroll = true };
            content.Dock = DockStyle.Top;
            content.AutoScroll = false;
            content.AutoSize = true;
            content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            content.Padding = new Padding(content.Padding.Left, content.Padding.Top, content.Padding.Right, 24);
            bool layingOut = false;
            Action fit = delegate {
                if (layingOut || viewport.IsDisposed) return;
                layingOut = true;
                try
                {
                    // Reserve the scrollbar width up front, avoiding a width /
                    // wrapping oscillation when the vertical scrollbar appears.
                    int width = Math.Max(100, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                    content.MinimumSize = new Size(width, 0); content.MaximumSize = new Size(width, 0);
                    int usable = Math.Max(60, width - content.Padding.Horizontal - 8);
                    foreach (Control child in content.Controls)
                    {
                        FitText(child, usable);
                        if (child is TextBox) child.Width = usable;
                        if (child is DateTimePicker || child is ListBox || child is ComboBox && child.Name == "ScopeChoices") child.Width = usable;
                        var row = child as FlowLayoutPanel;
                        if (row != null) { row.MaximumSize = new Size(usable, 0); row.WrapContents = true; FitChildren(row, usable); }
                    }
                    content.PerformLayout();
                }
                finally { layingOut = false; }
            };
            viewport.Controls.Add(content); page.Controls.Add(viewport);
            ObserveText(content, fit);
            viewport.SizeChanged += delegate { fit(); };
            content.ControlAdded += delegate { fit(); };
            page.VisibleChanged += delegate { fit(); };
            content.FontChanged += delegate { fit(); };
            fit();
        }
        internal static void FitChildren(Control parent, int width)
        {
            int usable = Math.Max(60, width - parent.Padding.Horizontal - 8);
            foreach (Control child in parent.Controls)
            {
                FitText(child, usable);
                var row = child as FlowLayoutPanel;
                if (row != null) { row.MaximumSize = new Size(usable, 0); row.WrapContents = true; FitChildren(row, usable); }
            }
        }
        internal static void ObserveText(Control parent, Action fit)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Label || child is CheckBox) { child.TextChanged += delegate { fit(); }; child.FontChanged += delegate { fit(); }; }
                if (child.HasChildren) ObserveText(child, fit);
            }
        }
        static void FitText(Control child, int width)
        {
            if (child is Label) child.MaximumSize = new Size(width, 0);
            var option = child as CheckBox;
            if (option == null) return;
            // Native checkbox AutoSize measures a single line even with MaximumSize.
            // Explicitly measure wrapped text, leaving room for the check glyph.
            option.AutoSize = false;
            option.Width = width;
            option.Height = Math.Max(24, TextRenderer.MeasureText(option.Text, option.Font, new Size(Math.Max(30, width - 26), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 8);
        }
        internal static Label Guide(string text, int minimumHeight, int padding)
        {
            var label = new Label { Text = text, Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, minimumHeight), Padding = new Padding(padding) };
            label.ParentChanged += delegate { if (label.Parent != null) { Action fit = delegate { label.MaximumSize = new Size(Math.Max(100, label.Parent.ClientSize.Width), 0); }; label.Parent.SizeChanged += delegate { fit(); }; fit(); } };
            return label;
        }
    }
}
