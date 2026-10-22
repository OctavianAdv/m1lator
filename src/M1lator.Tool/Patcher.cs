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
    // Android uses FPS as its display refresh rate; games pace frames as integer divisors of it
    // (30 FPS cap at 1000 Hz -> every 34th vsync = ~29, or worse 15). Keep it a multiple of 60, max 240.
    public const int MaxGuestFps = 240;

    public static int Run(string originalDir, string outDir, string boostDll)
    {
        var boost = ModuleDefMD.Load(File.ReadAllBytes(boostDll));
        var applyMethod = boost.Find("M1lator.Boost.Booster", false)?.FindMethod("Apply")
            ?? throw new Exception("Booster.Apply not found in " + boostDll);
        var stretchMethod = boost.Find("M1lator.Boost.Display", false)?.FindMethod("TryStretch")
            ?? throw new Exception("Display.TryStretch not found in " + boostDll);

        foreach (var dir in new[] { "ProgramFiles", "Client" })
            PatchFile(originalDir, outDir, dir, "HD-Common.dll", PatchHdCommon);
        PatchFile(originalDir, outDir, "Client", "Bluestacks.exe", PatchClient);
        PatchFile(originalDir, outDir, "ProgramFiles", "HD-Agent.exe", PatchAgent);
        PatchFile(originalDir, outDir, "ProgramFiles", "HD-Player.exe", m => PatchPlayer(m, applyMethod, stretchMethod));

        File.Copy(boostDll, Path.Combine(outDir, "ProgramFiles", "M1lator.Boost.dll"), true);
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

    static void PatchHdCommon(ModuleDefMD m)
    {
        // 1. FPS = Android display refresh rate: every reader of InstanceRegistry.FPS (settings UI, boot params,
        //    live setfpsvalue) gets a value games can divide evenly: <60 unchanged, else multiple of 60, max 240.
        //    (High framerate mode keeps BlueStacks' own 240 slider maximum.)
        var registry = Type(m, "BlueStacks.Common.InstanceRegistry");
        var snap = AddSnapFps(m, registry);
        var getFps = Method(registry, "get_FPS");
        // turn each `ret` into `call SnapFps` + new `ret`, so branches that target the old ret also pass through the snap
        var rets = getFps.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        Expect(rets.Count > 0, "InstanceRegistry.get_FPS ret");
        foreach (var r in rets)
        {
            r.OpCode = OpCodes.Call;
            r.Operand = snap;
            getFps.Body.Instructions.Insert(getFps.Body.Instructions.IndexOf(r) + 1, OpCodes.Ret.ToInstruction());
        }
        Console.WriteLine($"  InstanceRegistry.FPS -> multiple of 60, max {MaxGuestFps}");

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

    static void PatchClient(ModuleDefMD m)
    {
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

    // static int M1lator_SnapFps(int v) => v < 60 ? v : Math.Min(v, MaxGuestFps) / 60 * 60;
    static MethodDef AddSnapFps(ModuleDef m, TypeDef owner)
    {
        var md = new MethodDefUser("M1lator_SnapFps",
            MethodSig.CreateStatic(m.CorLibTypes.Int32, m.CorLibTypes.Int32),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig);
        md.ParamDefs.Add(new ParamDefUser("v", 1));
        var body = new CilBody();
        var i = body.Instructions;
        var capped = OpCodes.Ldarg_0.ToInstruction();
        var atLeast60 = OpCodes.Ldarg_0.ToInstruction();
        i.Add(OpCodes.Ldarg_0.ToInstruction());
        i.Add(OpCodes.Ldc_I4.ToInstruction(60));
        i.Add(OpCodes.Bge.ToInstruction(atLeast60));
        i.Add(OpCodes.Ldarg_0.ToInstruction());
        i.Add(OpCodes.Ret.ToInstruction());
        i.Add(atLeast60);                                   // if (v > Max) v = Max;
        i.Add(OpCodes.Ldc_I4.ToInstruction(MaxGuestFps));
        i.Add(OpCodes.Ble.ToInstruction(capped));
        i.Add(OpCodes.Ldc_I4.ToInstruction(MaxGuestFps));
        i.Add(OpCodes.Starg.ToInstruction(md.Parameters[0]));
        i.Add(capped);                                      // return v / 60 * 60;
        i.Add(OpCodes.Ldc_I4.ToInstruction(60));
        i.Add(OpCodes.Div.ToInstruction());
        i.Add(OpCodes.Ldc_I4.ToInstruction(60));
        i.Add(OpCodes.Mul.ToInstruction());
        i.Add(OpCodes.Ret.ToInstruction());
        md.Body = body;
        owner.Methods.Add(md);
        return md;
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
