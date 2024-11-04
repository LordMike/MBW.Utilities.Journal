using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace MBW.Utilities.Journal.SparseJournal;

internal static class SparseStreamHelper
{
    public static void MakeStreamSparse(FileStream stream)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            WindowsMakeSparse(stream.SafeFileHandle);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static void WindowsMakeSparse(SafeFileHandle handle)
    {
        int bytesReturned = 0;
        bool result;
        unsafe
        {
            result = Windows.Win32.PInvoke.DeviceIoControl(
                handle,
                Windows.Win32.PInvoke.FSCTL_SET_SPARSE,
                null,
                0,
                null,
                0,
                (uint*)&bytesReturned,
                null);

            Debug.Assert(result);
        }
    }
}