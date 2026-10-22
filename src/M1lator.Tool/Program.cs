using System;
using M1lator.Tool;

// m1lator tool
//   patch <originalDir> <outDir> <boostDll>   IL-patch pristine BlueStacks assemblies
//   pcores                                    print the number of performance-core logical processors
if (args.Length == 4 && args[0] == "patch")
    return Patcher.Run(args[1], args[2], args[3]);
if (args.Length == 1 && args[0] == "pcores")
{
    Console.WriteLine(CpuInfo.PerformanceLogicalProcessors());
    return 0;
}

Console.Error.WriteLine("usage: m1lator patch <originalDir> <outDir> <boostDll> | pcores");
return 2;
