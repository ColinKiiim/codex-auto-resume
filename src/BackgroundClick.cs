using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexAutoResume
{
    internal static class BackgroundClick
    {
        public static string LastRoute;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr window, ref Native.POINT point);
        [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Native.POINT point, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Native.RECT rect);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        // Window-message input only. Never activates a window or moves the pointer.
        public static bool Send(IntPtr window, Rectangle expectedBounds, Point center, Func<bool> validate)
        {
            if (!Native.IsWindow(window) || Native.IsIconic(window) || !Native.IsWindowVisible(window) || !IsWindowEnabled(window) ||
                WindowCapture.Bounds(window) != expectedBounds || !validate()) return false;
            IntPtr child = window;
            Native.POINT point = new Native.POINT { X = center.X, Y = center.Y };
            for (int depth = 0; depth < 16; depth++)
            {
                point = new Native.POINT { X = center.X, Y = center.Y };
                if (!ScreenToClient(child, ref point)) return false;
                IntPtr next = ChildWindowFromPointEx(child, point, 7);
                if (next == IntPtr.Zero || next == child) break;
                child = next;
            }
            if (Native.GetAncestor(child, 2) != window || !IsWindowEnabled(child)) return false;
            point = new Native.POINT { X = center.X, Y = center.Y };
            Native.RECT rect;
            if (!ScreenToClient(child, ref point) || !GetClientRect(child, out rect) || !rect.Rectangle.Contains(point.X, point.Y) ||
                point.X < 0 || point.X > 32767 || point.Y < 0 || point.Y > 32767 ||
                Native.IsIconic(window) || WindowCapture.Bounds(window) != expectedBounds || !validate()) return false;
            StringBuilder className = new StringBuilder(256); GetClassName(child, className, className.Capacity);
            if (className.ToString() == "Button" || className.ToString().StartsWith("WindowsForms10.BUTTON.", StringComparison.Ordinal))
            {
                LastRoute = "BM_CLICK";
                if (!PostMessage(child, 0x00F5, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("后台按钮消息被拒绝");
                return true;
            }
            LastRoute = "WM mouse messages";
            IntPtr packed = new IntPtr((point.Y << 16) | (point.X & 0xffff));
            if (!PostMessage(child, 0x0200, IntPtr.Zero, packed)) throw new InvalidOperationException("后台消息被拒绝（可能权限等级不同）；监控已停止");
            if (!PostMessage(child, 0x0201, new IntPtr(1), packed)) throw new InvalidOperationException("后台按下消息被拒绝；监控已停止");
            if (!PostMessage(child, 0x0202, IntPtr.Zero, packed))
            {
                PostMessage(child, 0x0202, IntPtr.Zero, packed);
                throw new InvalidOperationException("后台释放消息投递失败；不会重试点击");
            }
            return true; // Delivery only; caller must verify new square frames.
        }

    }
}
