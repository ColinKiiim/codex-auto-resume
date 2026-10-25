using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace CodexAutoResume
{
    // Independent WinRT/D3D11 interop. No third-party binary or injected code.
    // Keep one live session. Static windows need not emit duplicate dirty frames.
    internal sealed class WindowFrame : IDisposable
    {
        public Bitmap Image;
        public Rectangle Bounds;
        public DateTime CapturedAt;
        public DateTime ContentAt;
        public long ContentSequence;
        public long Sequence;
        public bool ContentFresh(DateTime now) { return ContentAt != DateTime.MinValue && (now - ContentAt).TotalSeconds >= -1 && (now - ContentAt).TotalSeconds <= 5; }
        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    internal static class WindowCapture
    {
        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ItemInterop
        {
            IntPtr CreateForWindow(IntPtr window, ref Guid iid);
            IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
        }
        [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface DxgiAccess { IntPtr GetInterface(ref Guid iid); }

        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string value, int length, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ItemInterop factory);
        [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdk, out IntPtr device, out uint level, out IntPtr context);
        [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr result);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out Native.RECT rect, int size);

        [StructLayout(LayoutKind.Sequential)] struct TextureDesc
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }
        [StructLayout(LayoutKind.Sequential)] struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDesc(IntPtr self, out TextureDesc desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture(IntPtr self, ref TextureDesc desc, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResource(IntPtr self, IntPtr target, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Map(IntPtr self, IntPtr resource, uint subresource, uint map, uint flags, out Mapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Unmap(IntPtr self, IntPtr resource, uint subresource);

        static T Method<T>(IntPtr obj, int slot) where T : class
        { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), typeof(T)) as T; }
        static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }
        static void CloseCom(object value)
        {
            if (value == null) return;
            try { IDisposable close = value as IDisposable; if (close != null) close.Dispose(); }
            finally { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        }
        static long sequence;
        static readonly object captureLock = new object();
        static D3dDevice sharedDevice;
        static IntPtr activeWindow;
        static GraphicsCaptureItem activeItem;
        static Direct3D11CaptureFramePool activePool;
        static GraphicsCaptureSession activeSession;
        static Size activeSize;
        static Bitmap latestImage;
        static DateTime latestContentAt;
        static long contentSequence;
        static volatile bool streamClosed;
        internal static event Action FrameAvailable;
        static bool hasClosedHandler;
        static bool hasFrameHandler;
        static Direct3D11CaptureFrame pendingFrame;
        static long requestSerial, stopSerial;
        static int stopQueued;
        internal static long SessionStarts;
        internal static long PoolRefreshes;
        static DateTime nextSessionAttempt;
        static IntPtr delayedWindow;
        static int consecutiveTimeouts;

        static void CloseStream()
        {
            GraphicsCaptureSession session = activeSession; Direct3D11CaptureFramePool pool = activePool; GraphicsCaptureItem item = activeItem;
            bool subscribed = hasClosedHandler; hasClosedHandler = false;
            if (hasFrameHandler && pool != null) pool.FrameArrived -= OnFrameArrived;
            hasFrameHandler = false;
            CloseCom(pendingFrame); pendingFrame = null;
            activeWindow = IntPtr.Zero; activeSession = null; activePool = null; activeItem = null; activeSize = Size.Empty;
            if (latestImage != null) { latestImage.Dispose(); latestImage = null; }
            try { CloseCom(session); }
            finally { try { CloseCom(pool); } finally { if (item != null) { if (subscribed) item.Closed -= OnClosed; if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); } } }
        }
        static void OnClosed(GraphicsCaptureItem sender, object args) { if (Object.ReferenceEquals(sender, activeItem)) streamClosed = true; }
        static void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            // Drain continuously so a full two-buffer pool cannot strand an old
            // image while the application changes underneath an opaque cover.
            // Never wait here during teardown; Snapshot also drains the queue.
            if (!Monitor.TryEnter(captureLock)) return;
            try
            {
                if (!Object.ReferenceEquals(sender, activePool)) return;
                for (int i = 0; i < 8; i++)
                { Direct3D11CaptureFrame next = sender.TryGetNextFrame(); if (next == null) break; CloseCom(pendingFrame); pendingFrame = next; }
            }
            catch { streamClosed = true; }
            finally { Monitor.Exit(captureLock); }
            Action available = FrameAvailable;
            if (available != null) available();
        }


        public static void StopCapture()
        {
            Interlocked.Exchange(ref stopSerial, Interlocked.Read(ref requestSerial));
            if (Interlocked.Exchange(ref stopQueued, 1) != 0) return;
            Task.Run(delegate
            {
                try { lock (captureLock) { if (Interlocked.Read(ref requestSerial) == Interlocked.Read(ref stopSerial)) CloseStream(); } }
                catch { /* OS may already have closed the capture item. */ }
                finally { Interlocked.Exchange(ref stopQueued, 0); }
            });
        }

        static void EnsureStream(IntPtr window, Size size)
        {
            if (delayedWindow != window) consecutiveTimeouts = 0;
            if (activeSession == null && delayedWindow == window && DateTime.UtcNow < nextSessionAttempt) throw new InvalidOperationException("等待捕获重连间隔，监测继续");
            if (activeWindow == window && activeSession != null && !streamClosed)
            {
                if (activeSize != size)
                { CloseCom(pendingFrame); pendingFrame = null; if (latestImage != null) { latestImage.Dispose(); latestImage = null; } activePool.Recreate(sharedDevice.Runtime, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, new Windows.Graphics.SizeInt32 { Width = size.Width, Height = size.Height }); activeSize = size; Interlocked.Increment(ref PoolRefreshes); }
                return;
            }
            CloseStream();
            IntPtr name = IntPtr.Zero, itemPtr = IntPtr.Zero; ItemInterop factory = null;
            try
            {
                string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                Check(WindowsCreateString(className, className.Length, out name)); Guid factoryId = typeof(ItemInterop).GUID;
                Check(RoGetActivationFactory(name, ref factoryId, out factory));
                Guid itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                itemPtr = factory.CreateForWindow(window, ref itemId);
                activeItem = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
                streamClosed = false; activeItem.Closed += OnClosed; hasClosedHandler = true;
                if (sharedDevice == null) sharedDevice = new D3dDevice();
                activePool = Direct3D11CaptureFramePool.CreateFreeThreaded(sharedDevice.Runtime, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, activeItem.Size);
                activePool.FrameArrived += OnFrameArrived; hasFrameHandler = true;
                activeSession = activePool.CreateCaptureSession(activeItem);
                activeSession.StartCapture(); activeWindow = window; activeSize = size; Interlocked.Increment(ref SessionStarts);
            }
            catch { CloseStream(); throw; }
            finally { if (factory != null) Marshal.ReleaseComObject(factory); Release(ref itemPtr); if (name != IntPtr.Zero) WindowsDeleteString(name); }
        }

        sealed class D3dDevice : IDisposable
        {
            public IntPtr Device, Context;
            public IDirect3DDevice Runtime;
            public D3dDevice()
            {
                IntPtr dxgi = IntPtr.Zero, projected = IntPtr.Zero;
                try
                {
                    uint level;
                    int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out Device, out level, out Context);
                    if (hr < 0) { Release(ref Device); Release(ref Context); Check(D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out Device, out level, out Context)); }
                    Guid dxgiId = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                    Check(Marshal.QueryInterface(Device, ref dxgiId, out dxgi));
                    Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
                    Runtime = (IDirect3DDevice)Marshal.GetObjectForIUnknown(projected);
                }
                catch { Dispose(); throw; }
                finally { Release(ref projected); Release(ref dxgi); }
            }
            public void Dispose()
            { try { CloseCom(Runtime); Runtime = null; } finally { Release(ref Context); Release(ref Device); } }
        }
        public static bool Supported { get { try { return GraphicsCaptureSession.IsSupported(); } catch { return false; } } }

        public static Rectangle Bounds(IntPtr window)
        {
            Native.RECT rect;
            if (DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf(typeof(Native.RECT))) < 0 && !Native.GetWindowRect(window, out rect))
                throw new InvalidOperationException("无法读取窗口捕获边界");
            return rect.Rectangle;
        }

        public static WindowFrame Snapshot(IntPtr window)
        { return Snapshot(window, false); }

        internal static WindowFrame ActionSnapshot(IntPtr window)
        { return Snapshot(window, true); }

        static WindowFrame Snapshot(IntPtr window, bool independent)
        {
            Interlocked.Increment(ref requestSerial);
            // Device/context operations are serialized, including when Task.Run changes threads.
            lock (captureLock)
            {
                try
                {
                    // Recreate alone cannot make a static window emit a frame.
                    // A separate session gives action preflight a new acquisition;
                    // it runs under the sampler's deadline and worker quarantine.
                    if (independent) CloseStream();
                    return SnapshotCore(window);
                }
                catch (COMException)
                {
                    CloseStream();
                    D3dDevice old = sharedDevice; sharedDevice = null;
                    if (old != null) old.Dispose();
                    throw;
                }
                catch (TimeoutException) { CloseStream(); delayedWindow = window; consecutiveTimeouts++; nextSessionAttempt = DateTime.UtcNow.AddSeconds(Math.Min(10, Math.Max(0, (consecutiveTimeouts - 1) * 2))); throw; }
            }
        }

        static WindowFrame SnapshotCore(IntPtr window)
        {
            if (!Supported) throw new InvalidOperationException("此系统不支持 Windows Graphics Capture（需要 Windows 10 1903+）");
            if (!Native.IsWindow(window) || Native.IsIconic(window) || !Native.IsWindowVisible(window)) { CloseStream(); throw new InvalidOperationException("窗口已关闭、隐藏或最小化，等待恢复后继续监控"); }
            Rectangle before = Bounds(window);
                EnsureStream(window, before.Size);
                DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    if (streamClosed) throw new InvalidOperationException("系统捕获会话已关闭，等待重新连接");
                    Direct3D11CaptureFrame frame = pendingFrame; pendingFrame = null;
                    // Discard superseded queued frames and read the most recent.
                    for (int i = 0; i < 8; i++)
                    { Direct3D11CaptureFrame next = activePool.TryGetNextFrame(); if (next == null) break; CloseCom(frame); frame = next; }
                    try
                    {
                        if (frame != null)
                        {
                            if (Native.IsIconic(window) || !Native.IsWindow(window) || !Native.IsWindowVisible(window) || before != Bounds(window) ||
                                frame.ContentSize.Width != before.Width || frame.ContentSize.Height != before.Height)
                                throw new InvalidOperationException("窗口大小或位置正在变化，等待新捕获帧");
                            IDirect3DSurface surface = frame.Surface;
                            Bitmap bitmap;
                            try { bitmap = Read(surface, sharedDevice.Device, sharedDevice.Context, frame.ContentSize.Width, frame.ContentSize.Height); }
                            finally { if (surface != null && Marshal.IsComObject(surface)) Marshal.ReleaseComObject(surface); }
                            if (latestImage != null) latestImage.Dispose(); latestImage = bitmap;
                            consecutiveTimeouts = 0;
                            double ageTicks = Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency) - frame.SystemRelativeTime.Ticks;
                            latestContentAt = DateTime.UtcNow.AddTicks(-(long)Math.Max(0, ageTicks)); Interlocked.Increment(ref contentSequence);
                        }
                    }
                    finally { CloseCom(frame); }
                    if (latestImage != null)
                    {
                        if (before != Bounds(window) || !Native.IsWindow(window) || Native.IsIconic(window) || streamClosed) throw new InvalidOperationException("窗口状态变化，等待重新定位");
                        // CapturedAt is the live sample time. ContentAt retains the
                        // actual pixel timestamp; unchanged pixels are never relabelled.
                        return new WindowFrame { Image = new Bitmap(latestImage), Bounds = before, CapturedAt = DateTime.UtcNow,
                            ContentAt = latestContentAt, ContentSequence = contentSequence, Sequence = Interlocked.Increment(ref sequence) };
                    }
                    Thread.Sleep(15);
                }
                throw new TimeoutException("后台捕获未收到新帧；不会使用旧图点击");
        }

        static Bitmap Read(IDirect3DSurface surface, IntPtr device, IntPtr context, int width, int height)
        {
            Guid textureId = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
            IntPtr texture = IntPtr.Zero, staging = IntPtr.Zero;
            try
            {
                texture = ((DxgiAccess)surface).GetInterface(ref textureId);
                TextureDesc desc; Method<GetDesc>(texture, 10)(texture, out desc);
                if (width <= 0 || height <= 0 || width > desc.Width || height > desc.Height)
                { streamClosed = true; throw new InvalidOperationException("捕获表面尺寸尚未更新，等待重新连接"); }
                desc.Usage = 3; desc.BindFlags = 0; desc.CPUAccessFlags = 0x20000; desc.MiscFlags = 0;
                Check(Method<CreateTexture>(device, 5)(device, ref desc, IntPtr.Zero, out staging));
                Method<CopyResource>(context, 47)(context, staging, texture);
                Mapped data; Check(Method<Map>(context, 14)(context, staging, 0, 1, 0, out data));
                try
                {
                    Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                    try
                    {
                        BitmapData pixels = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        try
                        {
                            byte[] row = new byte[width * 4];
                            for (int y = 0; y < height; y++)
                            { Marshal.Copy(IntPtr.Add(data.Data, checked(y * (int)data.RowPitch)), row, 0, row.Length); Marshal.Copy(row, 0, IntPtr.Add(pixels.Scan0, y * pixels.Stride), row.Length); }
                        }
                        finally { bitmap.UnlockBits(pixels); }
                        return bitmap;
                    }
                    catch { bitmap.Dispose(); throw; }
                }
                finally { Method<Unmap>(context, 15)(context, staging, 0); }
            }
            finally { Release(ref staging); Release(ref texture); }
        }
    }
}
