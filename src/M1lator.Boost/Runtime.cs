using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Win32;

namespace M1lator.Boost
{
    /// <summary>
    /// BlueStacks' Logger (HD-Common) flushes the log file after every single line, under a global lock, on whatever
    /// thread logs - input, sensors, renderer callbacks. The patched Logger.Print calls this instead of Flush():
    /// errors/warnings are written immediately, everything else at most twice a second (StreamWriter buffers it).
    /// </summary>
    public static class LogPolicy
    {
        static long lastFlushTicks;
        static TextWriter lastWriter;
        static int exitHooked;

        public static void Flush(TextWriter writer, string level)
        {
            lastWriter = writer;
            if (Interlocked.Exchange(ref exitHooked, 1) == 0)
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { lastWriter?.Flush(); } catch { } };
            long now = DateTime.UtcNow.Ticks;
            bool important = level == "ERROR" || level == "FATAL" || level == "WARNING";
            if (important || now - lastFlushTicks > TimeSpan.TicksPerMillisecond * 500)
            {
                lastFlushTicks = now;
                writer.Flush();
            }
        }
    }

    /// <summary>
    /// Once per instance, after Android has booted: ADB on temporarily (BlueStacks' own connectHost guest API, like
    /// HD-Common AdbCommandRunner), Android animations off and background bloat disabled, ADB off again.
    /// m1lator\AndroidTune REG_DWORD 1 = on (default), 0 = undo (re-enables the apps, animations back to 1x).
    /// Done state per instance: m1lator\AndroidTuned_&lt;vm&gt; = tuning revision.
    /// </summary>
    public static class AndroidTune
    {
        const int Revision = 1;
        const string KeyPath = @"SOFTWARE\BlueStacks_bgp64\m1lator";

        // background apps nobody needs for gaming. Kept on purpose: BS-Services (com.bluestacks.home),
        // BstCommandProcessor, Google Play services/Store/Games (login), camera, Guidance Screen (controls overlay).
        static readonly string[] Bloat =
        {
            "com.bluestacks.appmart",                       // BlueStacks app store
            "com.uncube.account",                           // BlueStacks account
            "com.google.android.feedback",                  // Market Feedback Agent
            "com.android.calendar", "com.google.android.syncadapters.calendar",
            "com.android.printspooler", "com.android.printservice.recommendation",
            "com.svox.pico",                                // text-to-speech engine
            "com.android.wallpaper.livepicker",
            "com.android.deskclock", "com.android.dialer",
        };

        public static void StartLater(string vm, List<string> log)
        {
            var t = new Thread(() => Run(vm, log)) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        static void Run(string vm, List<string> log)
        {
            try
            {
                int wanted = ReadInt("AndroidTune", 1);
                int done = ReadInt("AndroidTuned_" + vm, 0);
                if (wanted == 1 && done >= Revision) return;
                if (wanted == 0 && done == 0) return;

                int guestPort = GuestInt(vm, @"Config", "BstAndroidPort", vm == "Android" ? 9999 : 0);
                int adbPort = GuestInt(vm, @"Network\Redirect", "tcp/5555", 5555);
                if (guestPort == 0) return;

                // wait for Android's command server (up to 10 minutes after HD-Player start)
                bool connected = false;
                for (int i = 0; i < 60 && !connected; i++)
                {
                    Thread.Sleep(10000);
                    connected = Guest(guestPort, vm, "connectHost");
                }
                if (!connected) { Write(vm, "android tune: guest not reachable"); return; }
                Thread.Sleep(3000);

                string adb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HD-Adb.exe");
                string target = "127.0.0.1:" + adbPort;
                Adb(adb, "connect " + target);
                bool apply = wanted == 1;
                string scale = apply ? "0" : "1";
                foreach (var s in new[] { "window_animation_scale", "transition_animation_scale", "animator_duration_scale" })
                    Adb(adb, $"-s {target} shell settings put global {s} {scale}");
                int ok = 0;
                foreach (var pkg in Bloat)
                    if (Adb(adb, $"-s {target} shell pm {(apply ? "disable-user --user 0" : "enable")} {pkg}")) ok++;
                Adb(adb, "disconnect " + target);
                Guest(guestPort, vm, "disconnectHost");

                WriteDone(vm, apply ? Revision : 0);
                Write(vm, $"android tune: {(apply ? "applied" : "undone")} (animations {scale}x, {ok}/{Bloat.Length} apps {(apply ? "disabled" : "enabled")})");
            }
            catch (Exception ex) { Write(vm, "android tune: " + ex.GetType().Name + " " + ex.Message); }
        }

        // Android's local command server, called like HD-Common HTTPUtils.SendRequestToGuest (GET + local API token).
        // Tracking headers BlueStacks also sends (machine id, ad id, e-mail) are deliberately left out.
        static bool Guest(int port, string vm, string api)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_bgp64"))
                        wc.Headers.Add("x_api_token", k?.GetValue("ApiToken") as string ?? "");
                    wc.Headers.Add("x_oem", "bgp64");
                    wc.Headers.Add("vmname", vm);
                    wc.Headers.Add("vmid", vm == "Android" ? "0" : vm.Substring(vm.IndexOf('_') + 1));
                    string res = wc.DownloadString($"http://127.0.0.1:{port}/{api}");
                    return res.IndexOf("\"ok\"", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        static bool Adb(string exe, string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return false; }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        static int GuestInt(string vm, string sub, string name, int fallback)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_bgp64\Guests\" + vm + "\\" + sub))
            {
                object v = k?.GetValue(name);
                if (v is int i) return i;
                return v is string s && int.TryParse(s, out int j) ? j : fallback;
            }
        }

        static int ReadInt(string name, int fallback)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(KeyPath))
                return k?.GetValue(name) is int v ? v : fallback;
        }

        static void WriteDone(string vm, int value)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(KeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree,
                    System.Security.AccessControl.RegistryRights.QueryValues | System.Security.AccessControl.RegistryRights.SetValue))
                    k?.SetValue("AndroidTuned_" + vm, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        internal static void Write(string vm, string line) => Booster.AppendLog($"{vm}: {line}");
    }

    /// <summary>
    /// Local performance sampling (nothing leaves the PC): every 60 s the 5 busiest HD-Player threads and the process
    /// CPU share go to m1lator.log, so Diagnose.bat shows where the time goes (vCPU, render, input threads).
    /// m1lator\PerfLog REG_DWORD 1 = on (default)
    /// </summary>
    public static class PerfSampler
    {
        public static void Start()
        {
            var t = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Lowest };
            t.Start();
        }

        static void Loop()
        {
            var last = new Dictionary<int, TimeSpan>();
            DateTime lastWall = DateTime.UtcNow;
            TimeSpan lastTotal = TimeSpan.Zero;
            while (true)
            {
                Thread.Sleep(60000);
                try
                {
                    var proc = Process.GetCurrentProcess();
                    proc.Refresh();
                    double wallMs = (DateTime.UtcNow - lastWall).TotalMilliseconds;
                    lastWall = DateTime.UtcNow;
                    var now = new Dictionary<int, TimeSpan>();
                    var usage = new List<KeyValuePair<int, double>>();
                    foreach (ProcessThread th in proc.Threads)
                    {
                        try
                        {
                            TimeSpan cpu = th.TotalProcessorTime;
                            now[th.Id] = cpu;
                            if (last.TryGetValue(th.Id, out TimeSpan prev))
                                usage.Add(new KeyValuePair<int, double>(th.Id, (cpu - prev).TotalMilliseconds / wallMs * 100));
                        }
                        catch { }
                    }
                    TimeSpan total = proc.TotalProcessorTime;
                    double procPct = lastTotal == TimeSpan.Zero ? 0 : (total - lastTotal).TotalMilliseconds / wallMs * 100;
                    lastTotal = total;
                    last = now;
                    if (usage.Count == 0) continue;
                    var top = usage.OrderByDescending(u => u.Value).Take(5).Select(u => $"t{u.Key}={u.Value:0}%");
                    Booster.AppendLog($"perf: process {procPct:0}% of one core ({Environment.ProcessorCount} logical), threads {proc.Threads.Count}, busiest: {string.Join(" ", top.ToArray())}");
                }
                catch { }
            }
        }
    }
}
