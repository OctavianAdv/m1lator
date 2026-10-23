using System;
using System.Globalization;
using M1lator.ThemeGen;

// m1lator theme generator
//   M1lator.ThemeGen <srcThemeDir> <outDir> [--name N] [--accent #RRGGBB] [--tint #RRGGBB] [--glass 0..1] [--radius px]
//   builds the m1lator theme from a stock theme (Client\Assets); options come from a preset (scripts\themes\*.json)
if (args.Length >= 2)
{
    var preset = new Preset();
    for (int i = 2; i + 1 < args.Length; i += 2)
    {
        string v = args[i + 1];
        switch (args[i])
        {
            case "--name": preset.Name = v; break;
            case "--accent": preset.Accent = v; break;
            case "--tint": preset.Tint = v; break;
            case "--glass": preset.Glass = double.Parse(v, CultureInfo.InvariantCulture); break;
            case "--radius": preset.Radius = int.Parse(v, CultureInfo.InvariantCulture); break;
            default: Console.Error.WriteLine("unknown option " + args[i]); return 2;
        }
    }
    return ThemeGen.Run(args[0], args[1], preset);
}

Console.Error.WriteLine("usage: M1lator.ThemeGen <srcThemeDir> <outDir> [--name N] [--accent #RRGGBB] [--tint #RRGGBB] [--glass 0..1] [--radius px]");
return 2;
