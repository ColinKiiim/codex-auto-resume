using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CodexAutoResume
{
    internal static class DesktopIdentity
    {
        public const string AppId = "CodexAutoResume.Desktop";
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
        [DllImport("shell32.dll", ExactSpelling = true)]
        static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);
        [DllImport("shell32.dll", ExactSpelling = true)]
        static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid format; public uint id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant
        { [FieldOffset(0)] public ushort type; [FieldOffset(8)] public IntPtr pointer; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }
        static PropertyKey WindowIdKey()
        { return new PropertyKey { format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), id = 5 }; }
        static IPropertyStore WindowStore(IntPtr window)
        {
            Guid iid = typeof(IPropertyStore).GUID; IPropertyStore store;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out store)); return store;
        }
        public static void ApplyToWindow(IntPtr window)
        {
            IPropertyStore store = WindowStore(window);
            PropVariant value = new PropVariant { type = 31, pointer = Marshal.StringToCoTaskMemUni(AppId) };
            try { PropertyKey key = WindowIdKey(); Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }
        public static string WindowId(IntPtr window)
        {
            IPropertyStore store = WindowStore(window); PropVariant value = new PropVariant();
            try
            {
                PropertyKey key = WindowIdKey(); Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
                return value.type == 31 ? Marshal.PtrToStringUni(value.pointer) : null;
            }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }
        public static void ClearWindow(IntPtr window)
        {
            IPropertyStore store = WindowStore(window); PropVariant empty = new PropVariant();
            try { PropertyKey key = WindowIdKey(); Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref empty)); }
            finally { Marshal.ReleaseComObject(store); }
        }

        // Must run before any UI is created, including startup error dialogs.
        public static void Initialize()
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
        }
        public static string CurrentId()
        {
            IntPtr pointer;
            Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out pointer));
            try { return Marshal.PtrToStringUni(pointer); }
            finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
        }
        public static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded application icon.");
                using (Icon icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
        }
        public static string LicenseText()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded MIT license.");
                using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }
    }
}
