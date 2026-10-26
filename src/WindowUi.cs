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
                        if (child is Label) child.MaximumSize = new Size(usable, 0);
                        if (child is TextBox) child.Width = usable;
                        if (child is DateTimePicker) child.Width = usable;
                    }
                    content.PerformLayout();
                }
                finally { layingOut = false; }
            };
            viewport.Controls.Add(content); page.Controls.Add(viewport);
            viewport.SizeChanged += delegate { fit(); };
            content.ControlAdded += delegate { fit(); };
            page.VisibleChanged += delegate { fit(); };
            fit();
        }
    }
}
