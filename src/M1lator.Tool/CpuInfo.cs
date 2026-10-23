using System;
using System.Runtime.InteropServices;

namespace M1lator.Tool;

static class CpuInfo
{
    /// <summary>
    /// Logical processors in the highest efficiency class (P-cores on Intel 12th gen+ hybrid CPUs).
    /// On non-hybrid CPUs every processor shares one class, so this is simply the logical processor count.
    /// </summary>
    public static int PerformanceLogicalProcessors() => Count(physical: false);

    /// <summary>Physical P-cores (Hyper-Threading siblings share a CoreIndex).</summary>
    public static int PerformancePhysicalCores() => Count(physical: true);

    static int Count(bool physical)
    {
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out int len, IntPtr.Zero, 0);
        if (len <= 0) return Environment.ProcessorCount;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return Environment.ProcessorCount;
            // pass 1: highest efficiency class (P-cores); pass 2: count logical processors / distinct cores in it
            int maxClass = -1;
            for (int off = 0; off < len;)
            {
                int size = Marshal.ReadInt32(buf, off);
                if (size <= 0) break;
                if (Marshal.ReadInt32(buf, off + 4) == 0) maxClass = Math.Max(maxClass, Marshal.ReadByte(buf, off + 18));
                off += size;
            }
            var cores = new System.Collections.Generic.HashSet<int>();
            int logical = 0;
            for (int off = 0; off < len;)
            {
                int size = Marshal.ReadInt32(buf, off);
                if (size <= 0) break;
                if (Marshal.ReadInt32(buf, off + 4) == 0 && Marshal.ReadByte(buf, off + 18) == maxClass)
                {
                    logical++;
                    // group (ushort @12) + CoreIndex (byte @15) identify a physical core
                    cores.Add(Marshal.ReadInt16(buf, off + 12) << 8 | Marshal.ReadByte(buf, off + 15));
                }
                off += size;
            }
            int count = physical ? cores.Count : logical;
            return count > 0 ? count : Environment.ProcessorCount;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(IntPtr info, int bufferLength, out int returnedLength, IntPtr process, uint flags);
}
