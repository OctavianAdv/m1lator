using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace M1lator.Boost
{
    /// <summary>
    /// Runtime tweaks applied at the very start of HD-Player.exe (call injected by the patcher).
    /// Settings: HKLM\SOFTWARE\BlueStacks_bgp64\m1lator (all optional).
    ///   Priority         REG_SZ    High | AboveNormal | Normal   (default High)
    ///   PCoresOnly       REG_DWORD 1 = schedule only on performance cores (hybrid CPUs)   (default 1)
    ///   TimerResolution  REG_DWORD 1 = request 1 ms system timer                          (default 1)
    ///   NoPowerThrottle  REG_DWORD 1 = opt out of EcoQoS / timer throttling               (default 1)
    ///   MmcssInputThread REG_DWORD 1 = register the input/window thread with MMCSS "Games" (default 1)
    /// </summary>
    public static class Booster
    {
        const string KeyPath = @"SOFTWARE\BlueStacks_bgp64\m1lator";
        static readonly List<string> Log = new List<string>();

        public static void Apply()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
                {
                    if (GetInt(key, "TimerResolution", 1) == 1) Try("timer 1ms", () => timeBeginPeriod(1) == 0);
                    if (GetInt(key, "NoPowerThrottle", 1) == 1) Try("no power throttling", DisablePowerThrottling);
                    Try("priority", () => SetPriority(GetString(key, "Priority", "High")));
                    if (GetInt(key, "PCoresOnly", 1) == 1) Try("P-cores only", RestrictToPerformanceCores);
                    // Apply() runs on HD-Player's main thread = the window/input message thread
                    if (GetInt(key, "MmcssInputThread", 1) == 1) Try("MMCSS Games (input thread)", RegisterInputThreadWithMmcss);
                }
            }
            catch (Exception ex)
            {
                Log.Add("fatal: " + ex.Message);
            }
            WriteLog();
        }

        static void Try(string name, Func<bool> action)
        {
            try { Log.Add(name + ": " + (action() ? "ok" : "skipped")); }
            catch (Exception ex) { Log.Add(name + ": " + ex.GetType().Name + " " + ex.Message); }
        }

        // Multimedia Class Scheduler "Games" task: the thread is scheduled ahead of normal work while it is runnable.
        // The handle is intentionally never reverted; it lives as long as the thread.
        static bool RegisterInputThreadWithMmcss()
        {
            uint taskIndex = 0;
            IntPtr h = AvSetMmThreadCharacteristics("Games", ref taskIndex);
            if (h == IntPtr.Zero) return false;
            AvSetMmThreadPriority(h, AVRT_PRIORITY_HIGH);
            return true;
        }

        static bool SetPriority(string value)
        {
            ProcessPriorityClass cls;
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "normal": return false;
                case "abovenormal": cls = ProcessPriorityClass.AboveNormal; break;
                default: cls = ProcessPriorityClass.High; break;
            }
            Process.GetCurrentProcess().PriorityClass = cls;
            Log.Add("priority = " + cls);
            return true;
        }

        // PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION, state 0 = never throttle
        static bool DisablePowerThrottling()
        {
            var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0x1 | 0x4, StateMask = 0 };
            if (SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf(state)))
                return true;
            // Older Windows 10 builds reject IGNORE_TIMER_RESOLUTION
            state.ControlMask = 0x1;
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf(state));
        }

        // Uses CPU sets (soft affinity): threads stay on the highest efficiency class (P-cores).
        static unsafe bool RestrictToPerformanceCores()
        {
            int len;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out len, IntPtr.Zero, 0);
            if (len <= 0) return false;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return false;
                var sets = new List<KeyValuePair<uint, byte>>();
                byte maxClass = 0, minClass = 255;
                for (int off = 0; off < len;)
                {
                    byte* p = (byte*)buf + off;
                    int size = *(int*)p;
                    if (size <= 0) break;
                    if (*(int*)(p + 4) == 0) // CpuSetInformation
                    {
                        uint id = *(uint*)(p + 8);
                        byte eff = p[18];
                        sets.Add(new KeyValuePair<uint, byte>(id, eff));
                        if (eff > maxClass) maxClass = eff;
                        if (eff < minClass) minClass = eff;
                    }
                    off += size;
                }
                if (sets.Count == 0 || maxClass == minClass) return false; // not a hybrid CPU
                var ids = new List<uint>();
                foreach (var s in sets) if (s.Value == maxClass) ids.Add(s.Key);
                Log.Add("P-core cpu sets: " + ids.Count + "/" + sets.Count);
                return SetProcessDefaultCpuSets(GetCurrentProcess(), ids.ToArray(), (uint)ids.Count);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static int GetInt(RegistryKey key, string name, int def)
        {
            object v = key == null ? null : key.GetValue(name);
            return v is int ? (int)v : def;
        }

        static string GetString(RegistryKey key, string name, string def)
        {
            object v = key == null ? null : key.GetValue(name);
            return v as string ?? def;
        }

        static void WriteLog()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"BlueStacks_bgp64\Logs");
                File.AppendAllText(Path.Combine(dir, "m1lator.log"),
                    DateTime.Now.ToString("s") + " HD-Player pid " + Process.GetCurrentProcess().Id + ": " + string.Join("; ", Log.ToArray()) + Environment.NewLine);
            }
            catch { }
        }

        const int ProcessPowerThrottling = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        const int AVRT_PRIORITY_HIGH = 1;
        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
        [DllImport("avrt.dll", SetLastError = true)]
        static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, int priority);

        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemCpuSetInformation(IntPtr info, int bufferLength, out int returnedLength, IntPtr process, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessDefaultCpuSets(IntPtr process, uint[] cpuSetIds, uint count);
    }
}
