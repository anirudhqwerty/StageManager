using System.Runtime.InteropServices;

namespace StageManager.Native.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;

        // BUG FIX: [MarshalAs(UnmanagedType.Bool, SizeConst = 4)] was wrong.
        // SizeConst is only valid for array types (ByValArray / ByValTStr).
        // On a Bool field it is silently ignored by the runtime but is misleading
        // and will cause compiler warnings in stricter analysis tools.
        // The correct marshal type for a Win32 BOOL (4-byte int) is simply UnmanagedType.Bool.
        [MarshalAs(UnmanagedType.Bool)]
        public bool fVisible;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fSourceClientAreaOnly;
    }
}
