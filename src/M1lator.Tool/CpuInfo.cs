using System;
using System.Runtime.InteropServices;

namespace M1lator.Tool;

static class CpuInfo
{
    /// <summary>
    /// Logical processors in the highest efficiency class (P-cores on Intel 12th gen+ hybrid CPUs).
    /// On non-hybrid CPUs every processor shares one class, so this is simply the logical processor count.
    /// </summary>
    public static int PerformanceLogicalProcessors()
    {
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out int len, IntPtr.Zero, 0);
        if (len <= 0) return Environment.ProcessorCount;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return Environment.ProcessorCount;
            int maxClass = -1, count = 0;
            for (int off = 0; off < len;)
            {
                int size = Marshal.ReadInt32(buf, off);
                if (size <= 0) break;
                if (Marshal.ReadInt32(buf, off + 4) == 0) // CpuSetInformation
                {
                    int eff = Marshal.ReadByte(buf, off + 18);
                    if (eff > maxClass) { maxClass = eff; count = 1; }
                    else if (eff == maxClass) count++;
                }
                off += size;
            }
            return count > 0 ? count : Environment.ProcessorCount;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(IntPtr info, int bufferLength, out int returnedLength, IntPtr process, uint flags);
}
