using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace M1lator.Tool;

/// <summary>
/// Applies all m1lator IL patches. Input is a directory of pristine (backed-up) assemblies laid out as
///   ProgramFiles\{HD-Common.dll, HD-Player.exe, HD-Agent.exe}
///   Client\{HD-Common.dll, Bluestacks.exe}
/// and output mirrors that layout. Every patch asserts that its target exists, so a BlueStacks
/// version change fails loudly instead of silently producing a half-patched build.
/// </summary>
static class Patcher
{
    public const int MaxFps = 1000;

    public static int Run(string originalDir, string outDir, string boostDll)
    {
        var boost = ModuleDefMD.Load(File.ReadAllBytes(boostDll));
        var applyMethod = boost.Find("M1lator.Boost.Booster", false)?.FindMethod("Apply")
            ?? throw new Exception("Booster.Apply not found in " + boostDll);
        var stretchMethod = boost.Find("M1lator.Boost.Display", false)?.FindMethod("TryStretch")
            ?? throw new Exception("Display.TryStretch not found in " + boostDll);
        string clientDll = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(boostDll))!, "M1lator.Client.dll");
        var brand = ModuleDefMD.Load(File.ReadAllBytes(clientDll)).Find("M1lator.Client.Brand", false)
            ?? throw new Exception("M1lator.Client.Brand not found in " + clientDll);

        var logFlush = boost.Find("M1lator.Boost.LogPolicy", false)?.FindMethod("Flush")
            ?? throw new Exception("LogPolicy.Flush not found in " + boostDll);
        foreach (var dir in new[] { "ProgramFiles", "Client" })
            PatchFile(originalDir, outDir, dir, "HD-Common.dll", m => { PatchHdCommon(m); PatchLogger(m, logFlush); });
        PatchFile(originalDir, outDir, "Client", "Bluestacks.exe", m => PatchClient(m, brand));
        PatchFile(originalDir, outDir, "ProgramFiles", "HD-Agent.exe", PatchAgent);
        PatchFile(originalDir, outDir, "ProgramFiles", "HD-Player.exe", m => PatchPlayer(m, applyMethod, stretchMethod));

        File.Copy(boostDll, Path.Combine(outDir, "ProgramFiles", "M1lator.Boost.dll"), true);
        File.Copy(boostDll, Path.Combine(outDir, "Client", "M1lator.Boost.dll"), true);       // client's HD-Common uses LogPolicy
        File.Copy(clientDll, Path.Combine(outDir, "Client", "M1lator.Client.dll"), true);
        Console.WriteLine("all patches applied");
        return 0;
    }

    static void PatchFile(string originalDir, string outDir, string sub, string name, Action<ModuleDefMD> patch)
    {
        string src = Path.Combine(originalDir, sub, name);
        string dst = Path.Combine(outDir, sub, name);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        var module = ModuleDefMD.Load(File.ReadAllBytes(src), new ModuleCreationOptions { TryToLoadPdbFromDisk = false });
        Console.WriteLine($"[{sub}\\{name}]");
        patch(module);
        var opts = new ModuleWriterOptions(module);
        opts.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
        module.Write(dst, opts);
    }

    // ---------------------------------------------------------------- HD-Common.dll

    // Logger.Print(level, tag, fmt, args) flushes the file after every line; route that through LogPolicy.Flush
    // (errors/warnings immediately, the rest at most twice a second).
    static void PatchLogger(ModuleDefMD m, MethodDef logFlush)
    {
        var print = Type(m, "BlueStacks.Common.Logger").Methods
            .Single(x => x.Name == "Print" && x.Parameters.Count == 4 && x.Parameters[0].Type.FullName == "System.String");
        var flushes = print.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is IMethod im && im.Name == "Flush" && im.DeclaringType.FullName.EndsWith("Writer"))
            .ToList();
        Expect(flushes.Count == 1, "Logger.Print writer flush");
        var ins = print.Body.Instructions;
        int at = ins.IndexOf(flushes[0]);
        flushes[0].OpCode = OpCodes.Ldarg_0;                                  // stack: writer, level
        flushes[0].Operand = null;
        ins.Insert(at + 1, OpCodes.Call.ToInstruction(m.Import(logFlush)));   // LogPolicy.Flush(writer, level)
        Console.WriteLine("  Logger.Print -> buffered log writes (errors flushed at once)");
    }

    static void PatchHdCommon(ModuleDefMD m)
    {
        // 1. High framerate mode: slider maximum 240 -> 1000 (slider binds to MaxFPS).
        //    FPS goes back to 30 / high framerate off when the emulator closes (M1lator.Boost, HD-Player exit).
        var setter = Method(m, "BlueStacks.Common.EngineSettingBaseViewModel", "set_EnableHighFrameRates");
        int hits = 0;
        foreach (var ins in setter.Body.Instructions)
            if (ins.IsLdcI4() && ins.GetLdcI4Value() == 240) { ins.OpCode = OpCodes.Ldc_I4; ins.Operand = MaxFps; hits++; }
        Expect(hits == 1, "MaxFPS = 240 constant");
        Console.WriteLine($"  high-fps cap 240 -> {MaxFps}");

        // 2. Telemetry: every cloud stats sender becomes a no-op (FrontendStatusUpdate talks to the local agent, keep it)
        StubAll(m, "BlueStacks.Common.Stats", md => md.Name.StartsWith("Send") && md.Name != "SendFrontendStatusUpdate");

        // 3. bst_config: never pull feature flags from the cloud, only load the local file
        var fm = Type(m, "BlueStacks.Common.FeatureManager");
        var download = Method(fm, "DownloadFile");
        var filePath = fm.FindField("sFilePath") ?? throw new Exception("FeatureManager.sFilePath missing");
        var loadFile = fm.FindMethod("LoadFile") ?? throw new Exception("FeatureManager.LoadFile missing");
        var fileExists = CorlibFileExists(m);
        var body = new CilBody();
        var ret = OpCodes.Ret.ToInstruction();
        body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(filePath));
        body.Instructions.Add(OpCodes.Call.ToInstruction(fileExists));
        body.Instructions.Add(OpCodes.Brfalse_S.ToInstruction(ret));
        body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(filePath));
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(loadFile));
        body.Instructions.Add(ret);
        download.Body = body;
        Console.WriteLine("  FeatureManager.DownloadFile -> local only");

        // 4. Notifications: desktop popups (Android app + cloud), rich popups and in-window toasts never show
        StubAll(m, "BlueStacks.Common.NotificationWindow", md => md.Name.String is "AddAlert" or "ForceShowAlert");
        Stub(Method(m, "BlueStacks.Common.RichNotificationPopup", "ShowWindow"));
        Stub(Method(m, "BlueStacks.Common.CustomToastPopupControl", "ShowPopup"));

        // 5. Memory trimming: the client and HD-Agent force a loop every 15 s (blocking gen-2 GC + EmptyWorkingSet,
        //    which pages the whole UI out -> input lag on the next click). The Android-side trim drops guest caches.
        StubAll(m, "BlueStacks.Common.MemoryManager", md => md.Name.String is "TrimMemory" or "CheckAndTrimAndroidMemory");
    }

    // ---------------------------------------------------------------- Bluestacks.exe (client UI)

    static void PatchClient(ModuleDefMD m, TypeDef brand)
    {
        // Branding (M1lator.Client.dll next to Bluestacks.exe): global font, RGB "M1LATOR" top bar, m1lator version in About
        var main = Method(m, "BlueStacks.BlueStacksUI.App", "Main");
        main.Body.Instructions.Insert(0, OpCodes.Call.ToInstruction(m.Import(brand.FindMethod("Init"))));
        CallAtEndOfConstructor(m, "BlueStacks.BlueStacksUI.TopBar", m.Import(brand.FindMethod("ApplyTopBar")));
        CallAtEndOfConstructor(m, "BlueStacks.BlueStacksUI.AboutSettingsControl", m.Import(brand.FindMethod("ApplyAbout")));
        Console.WriteLine("  branding: font, M1LATOR top bar, About version");

        // Settings window: hidden tabs + macOS-style look
        var settings = Type(m, "BlueStacks.BlueStacksUI.SettingsWindow");
        var filter = m.Import(brand.FindMethod("FilterSettingsTabs"));
        var map = m.Import(brand.FindMethod("MapSettingsTab"));
        var create = Method(settings, "CreateAllButtons");          // this, string startUpTab
        create.Body.Instructions.Insert(0, OpCodes.Ldarg_0.ToInstruction());
        create.Body.Instructions.Insert(1, OpCodes.Call.ToInstruction(filter));
        InsertMapArg(create, 1, map, 2);
        InsertMapArg(Method(settings, "ChangeSettingsTab"), 2, map, 0);   // this, MainWindow window, string tab
        CallAtEndOfConstructor(m, "BlueStacks.BlueStacksUI.SettingsWindow", m.Import(brand.FindMethod("StyleSettings")));
        Console.WriteLine("  settings: hidden tabs, macOS style");

        // Remaining telemetry: per-app usage report, hidden Firebase notification browser (extra Chromium page)
        Stub(Method(m, "BlueStacks.BlueStacksUI.PromotionManager", "SendAppUsageStats"));
        Stub(Method(m, "BlueStacks.BlueStacksUI.MainWindow", "CreateFirebaseBrowserControl"));

        StubAll(m, "BlueStacks.BlueStacksUI.ClientStats", md => md.Name.StartsWith("Send"));

        // Updater: always report "no update" (blocks forced silent 'hard' updates that would wipe the mod)
        var updater = Type(m, "BlueStacks.BlueStacksUI.BlueStacksUpdater");
        var check = Method(updater, "CheckForUpdate");
        var dataCtor = Type(m, "BlueStacks.BlueStacksUI.BlueStacksUpdateData").FindDefaultConstructor()
            ?? throw new Exception("BlueStacksUpdateData..ctor missing");
        check.Body = new CilBody();
        check.Body.Instructions.Add(OpCodes.Newobj.ToInstruction(dataCtor));
        check.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        Console.WriteLine("  BlueStacksUpdater.CheckForUpdate -> no update");

        // SecurityMetrics: process blacklist scan + Root.vdi MD5 + daily cloud report
        Stub(Method(m, "BlueStacks.BlueStacksUI.SecurityMetrics", "Init"));

        // Notifications: cloud pushes (ads, red dots, quit popups, remote method calls) and the
        // "press F11 to exit full screen" toast
        Stub(Method(m, "BlueStacks.BlueStacksUI.CloudNotificationManager", "HandleCloudNotification"));
        Stub(Method(m, "BlueStacks.BlueStacksUI.MainWindow", "ToggleFullScreenToastVisibility"));

        // Home page: local m1lator home (Library only, no Game center / cloud store), when installed.
        //   string p = Path.Combine(RegistryManager.Instance.ClientInstallDir, "m1lator-home\\index.html");
        //   if (File.Exists(p)) return p;   ...else stock cloud URL
        var homeUrl = Method(m, "BlueStacks.BlueStacksUI.BlueStacksUIUtils", "GetHtmlHomeUrl");
        var getInstance = FindCalled(m, "BlueStacks.Common.RegistryManager", "get_Instance");
        var getClientDir = FindCalled(m, "BlueStacks.Common.RegistryManager", "get_ClientInstallDir");
        var combine = new MemberRefUser(m, "Combine",
            MethodSig.CreateStatic(m.CorLibTypes.String, m.CorLibTypes.String, m.CorLibTypes.String),
            new TypeRefUser(m, "System.IO", "Path", m.CorLibTypes.AssemblyRef));
        var ins = homeUrl.Body.Instructions;
        var stock = ins[0];
        var local = new[]
        {
            OpCodes.Call.ToInstruction(getInstance),
            OpCodes.Callvirt.ToInstruction(getClientDir),
            OpCodes.Ldstr.ToInstruction("m1lator-home\\index.html"),
            OpCodes.Call.ToInstruction(combine),
        };
        int at = 0;
        foreach (var x in local) ins.Insert(at++, x);
        ins.Insert(at++, OpCodes.Call.ToInstruction(CorlibFileExists(m)));
        ins.Insert(at++, OpCodes.Brfalse.ToInstruction(stock));
        foreach (var x in local) ins.Insert(at++, new Instruction(x.OpCode, x.Operand));
        ins.Insert(at, OpCodes.Ret.ToInstruction());
        Console.WriteLine("  BlueStacksUIUtils.GetHtmlHomeUrl -> local m1lator home");
    }

    // At instruction index `at`: arg = map(this, arg)   (arg index counts `this` as 0)
    static void InsertMapArg(MethodDef md, int argIndex, IMethod map, int at)
    {
        var p = md.Parameters[argIndex];
        var ins = md.Body.Instructions;
        ins.Insert(at, OpCodes.Ldarg_0.ToInstruction());
        ins.Insert(at + 1, OpCodes.Ldarg.ToInstruction(p));
        ins.Insert(at + 2, OpCodes.Call.ToInstruction(map));
        ins.Insert(at + 3, OpCodes.Starg.ToInstruction(p));
    }

    // Inserts `hook(this)` before every `ret` of the type's (single) instance constructor.
    static void CallAtEndOfConstructor(ModuleDef m, string type, IMethod hook)
    {
        var ctors = Type(m, type).Methods.Where(x => x.IsInstanceConstructor).ToList();
        Expect(ctors.Count == 1, type + " constructor");
        var ins = ctors[0].Body.Instructions;
        foreach (var r in ins.Where(i => i.OpCode == OpCodes.Ret).ToList())
        {
            // reuse the ret instruction as `ldarg.0` so branches that targeted it still run the hook
            r.OpCode = OpCodes.Ldarg_0;
            r.Operand = null;
            int at = ins.IndexOf(r) + 1;
            ins.Insert(at, OpCodes.Call.ToInstruction(hook));
            ins.Insert(at + 1, OpCodes.Ret.ToInstruction());
        }
    }

    // The method a module already calls for a given type/name (e.g. a cross-assembly property getter).
    static IMethod FindCalled(ModuleDef m, string declaringType, string name)
    {
        foreach (var t in m.GetTypes())
            foreach (var md in t.Methods)
                if (md.HasBody)
                    foreach (var ins in md.Body.Instructions)
                        if (ins.Operand is IMethod im && im.Name == name && im.DeclaringType?.FullName == declaringType)
                            return im;
        throw new Exception($"no call to {declaringType}::{name} found");
    }

    // ---------------------------------------------------------------- HD-Agent.exe

    static void PatchAgent(ModuleDefMD m)
    {
        // Cloud announcements can download+run executables and trigger silent log uploads
        StubAll(m, "BlueStacks.Agent.CloudAnnouncement",
            md => md.Name.String is "ShowAnnouncement" or "ShowAnnouncementResponse" or "ShowNotification");
    }

    // ---------------------------------------------------------------- HD-Player.exe

    static void PatchPlayer(ModuleDefMD m, MethodDef apply, MethodDef tryStretch)
    {
        var main = Method(m, "BlueStacks.Player.Program", "Main");
        main.Body.Instructions.Insert(0, OpCodes.Call.ToInstruction(m.Import(apply)));
        Console.WriteLine("  Program.Main -> M1lator.Boost.Booster.Apply()");

        // Fullscreen stretch: if (Display.TryStretch(VMWindow.Instance, ref mScaledDisplayArea)) return;
        var layout = Type(m, "BlueStacks.Player.LayoutManager");
        var fix = Method(layout, "FixupGuestDisplay_FixAspectRatio");
        var area = layout.FindField("mScaledDisplayArea") ?? throw new Exception("LayoutManager.mScaledDisplayArea missing");
        var instance = Type(m, "BlueStacks.Player.VMWindow").FindField("Instance") ?? throw new Exception("VMWindow.Instance missing");
        var body = fix.Body.Instructions;
        var original = body[0];
        body.Insert(0, OpCodes.Ldsfld.ToInstruction(instance));
        body.Insert(1, OpCodes.Ldsflda.ToInstruction(area));
        body.Insert(2, OpCodes.Call.ToInstruction(m.Import(tryStretch)));
        body.Insert(3, OpCodes.Brfalse.ToInstruction(original));
        body.Insert(4, OpCodes.Ret.ToInstruction());
        Console.WriteLine("  LayoutManager.FixupGuestDisplay_FixAspectRatio -> fullscreen stretch");

        // "press F11 to exit full screen" toast drawn by the player itself
        Stub(Method(m, "BlueStacks.Player.FullScreenToast", "Show"));

        PatchPlayerInput(m, boostModule: apply.Module);
    }

    static void PatchPlayerInput(ModuleDefMD m, ModuleDef boostModule)
    {
        // Raw mouse (games that capture the mouse): coalesced, allocation-free handler in M1lator.Boost
        var rawHandle = boostModule.Find("M1lator.Boost.RawInput", false)?.FindMethod("Handle") ?? throw new Exception("RawInput.Handle missing");
        var raw = Method(m, "BlueStacks.Player.InputMapper", "HandleRawInput");      // this, IntPtr lParam
        raw.Body = new CilBody();
        raw.Body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
        raw.Body.Instructions.Add(OpCodes.Call.ToInstruction(m.Import(rawHandle)));
        raw.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        Console.WriteLine("  InputMapper.HandleRawInput -> coalesced raw mouse");

        // Key auto-repeat: if (KeyRepeat.IsSuppressed() && pressed && sKeyStateSet.ContainsKey(key)) return false;
        var vm = Type(m, "BlueStacks.Player.VMWindow");
        var uks = Method(vm, "UpdateKeyState");                                     // this, Keys key, bool pressed
        var loadSet = uks.Body.Instructions.First(i => i.OpCode == OpCodes.Ldsfld && i.Operand is IField f && f.Name == "sKeyStateSet");
        var contains = uks.Body.Instructions.First(i => (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call) && i.Operand is IMethod im && im.Name == "ContainsKey");
        var suppressed = boostModule.Find("M1lator.Boost.KeyRepeat", false)?.FindMethod("IsSuppressed") ?? throw new Exception("KeyRepeat missing");
        var orig = uks.Body.Instructions[0];
        var pre = new[]
        {
            OpCodes.Call.ToInstruction(m.Import(suppressed)), OpCodes.Brfalse.ToInstruction(orig),
            OpCodes.Ldarg.ToInstruction(uks.Parameters[2]), OpCodes.Brfalse.ToInstruction(orig),
            new Instruction(OpCodes.Ldsfld, loadSet.Operand), OpCodes.Ldarg.ToInstruction(uks.Parameters[1]),
            new Instruction(contains.OpCode, contains.Operand), OpCodes.Brfalse.ToInstruction(orig),
            OpCodes.Ldc_I4_0.ToInstruction(), OpCodes.Ret.ToInstruction(),
        };
        for (int i = 0; i < pre.Length; i++) uks.Body.Instructions.Insert(i, pre[i]);
        Console.WriteLine("  VMWindow.UpdateKeyState -> ignore auto-repeat of held keys");

        // "user-active" report every 10 s from every mouse/key event (new thread + HTTP each time, usage timeline)
        Stub(Method(vm, "UpdateUserActivityStatus"));

        // Sensors BlueStacks 4 doesn't implement (Android asks for magnetometer/orientation): ignore quietly
        // instead of a synchronous error log line several times per second.
        var sensors = Type(m, "BlueStacks.Player.SensorDevice");
        var lookup = Method(sensors, "LookupState");
        foreach (var name in new[] { "EnableHandlerImpl", "SetDelayHandlerImpl" })
        {
            var md = Method(sensors, name);                                          // this, Type sensor, ...
            var first = md.Body.Instructions[0];
            md.Body.Instructions.Insert(0, OpCodes.Ldarg_0.ToInstruction());
            md.Body.Instructions.Insert(1, OpCodes.Ldarg_1.ToInstruction());
            md.Body.Instructions.Insert(2, OpCodes.Call.ToInstruction(lookup));
            md.Body.Instructions.Insert(3, OpCodes.Brtrue.ToInstruction(first));
            md.Body.Instructions.Insert(4, OpCodes.Ret.ToInstruction());
        }
        Console.WriteLine("  SensorDevice: unsupported sensors ignored without log spam");
    }

    // ---------------------------------------------------------------- helpers

    static TypeDef Type(ModuleDef m, string fullName) =>
        m.Find(fullName, false) ?? throw new Exception("type not found: " + fullName);

    static MethodDef Method(ModuleDef m, string type, string name) => Method(Type(m, type), name);

    static MethodDef Method(TypeDef t, string name)
    {
        var all = t.Methods.Where(x => x.Name == name).ToList();
        Expect(all.Count == 1, $"{t.FullName}::{name} (found {all.Count})");
        return all[0];
    }

    static void StubAll(ModuleDef m, string type, Func<MethodDef, bool> filter)
    {
        var targets = Type(m, type).Methods.Where(x => x.HasBody && !x.IsConstructor && filter(x)).ToList();
        Expect(targets.Count > 0, "stub targets in " + type);
        foreach (var md in targets) Stub(md);
    }

    static void Stub(MethodDef md)
    {
        var body = new CilBody();
        var rt = md.ReturnType.RemovePinnedAndModifiers();
        switch (rt.ElementType)
        {
            case ElementType.Void: break;
            case ElementType.Boolean: case ElementType.I4: case ElementType.U4:
            case ElementType.I2: case ElementType.U2: case ElementType.I1: case ElementType.U1: case ElementType.Char:
                body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction()); break;
            case ElementType.String:
                body.Instructions.Add(OpCodes.Ldstr.ToInstruction("")); break;
            default:
                if (!rt.IsValueType) { body.Instructions.Add(OpCodes.Ldnull.ToInstruction()); break; }
                throw new Exception($"cannot stub {md.FullName}: unsupported return type {rt}");
        }
        body.Instructions.Add(OpCodes.Ret.ToInstruction());
        md.Body = body;
        Console.WriteLine($"  stub {md.DeclaringType.Name}.{md.Name}");
    }

    // System.IO.File::Exists(string) referenced from the target's own corlib (mscorlib for .NET Framework)
    static IMethod CorlibFileExists(ModuleDef m)
    {
        var corlibRef = m.CorLibTypes.AssemblyRef;
        var fileType = new TypeRefUser(m, "System.IO", "File", corlibRef);
        return new MemberRefUser(m, "Exists",
            MethodSig.CreateStatic(m.CorLibTypes.Boolean, m.CorLibTypes.String), fileType);
    }

    static void Expect(bool ok, string what)
    {
        if (!ok) throw new Exception("patch target mismatch: " + what);
    }
}
