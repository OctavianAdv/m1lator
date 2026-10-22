using M1lator.ThemeGen;

// m1lator theme generator
//   M1lator.ThemeGen <srcThemeDir> <outDir>   build the m1lator theme from a stock theme (Client\Assets)
if (args.Length == 2)
    return ThemeGen.Run(args[0], args[1]);

Console.Error.WriteLine("usage: M1lator.ThemeGen <srcThemeDir> <outDir>");
return 2;
