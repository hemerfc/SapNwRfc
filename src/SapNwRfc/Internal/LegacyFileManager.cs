using System;
using System.Runtime.InteropServices;

namespace SapNwRfc.Internal
{
    public static class LegacyFileManager
    {
        #if RUNTIME_OSX
            private const string CLib = "libc.so";
        #elif RUNTIME_LINUX
            private const string CLib = "libc.so";
        #else // RUNTIME_WINDOWS
            private const string CLib = "msvcrt.dll";
        #endif

#pragma warning disable SA1300
            [DllImport(CLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern IntPtr fopen(string filename, string mode);

            [DllImport(CLib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]

            public static extern int fclose(IntPtr file);
#pragma warning restore SA1300
    }
}
