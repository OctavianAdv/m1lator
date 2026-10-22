using System;
using M1lator.Tool;

// m1lator patcher
//   patch <originalDir> <outDir> <boostDll>   IL-patch pristine BlueStacks assemblies
if (args.Length == 4 && args[0] == "patch")
    return Patcher.Run(args[1], args[2], args[3]);

Console.Error.WriteLine("usage: m1lator patch <originalDir> <outDir> <boostDll>");
return 2;
