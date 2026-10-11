using System;
using System.Collections.Generic;
using LaneReader;

/// <summary>
/// Offline check for the panel's language files: loads them exactly the way the mod does and prints
/// a few strings, so the parsing (tab separator, the " = " fallback, leading spaces, comments, the
/// missing-file fallback) can be verified without starting the game.
///
///   LangCheck &lt;mod folder&gt;
/// </summary>
internal static class LangCheck
{
    private static readonly string[] Sample =
    {
        "显示叠加层",
        "轨道长度 (px, 0=自动到屏幕顶)",
        "间隔小于窗口 = 轮指；大于 = 食指交替。",
        "  处理不了双押（同时的两颗音只能挤在一起）。",
        "当前主题: ",
        "开",
        "这条不在语言文件里，应该原样返回",
    };

    private static int Main(string[] args)
    {
        string modPath = args.Length > 0 ? args[0] : @"..\..\Mod";

        L10n.Init(modPath, Console.WriteLine);
        Console.WriteLine("folder:    " + modPath);
        Console.WriteLine("available: " + string.Join(", ", ToArray()));
        Console.WriteLine();

        foreach (string code in L10n.Available)
        {
            L10n.Use(code);
            Console.WriteLine("--- " + code + "  (" + L10n.Name + ") ---");
            foreach (string key in Sample)
                Console.WriteLine("  " + key + "   ->   " + L10n.Tr(key));
            Console.WriteLine();
        }

        L10n.Use("xx");
        Console.WriteLine("--- xx (no such file): must fall back to the Chinese text ---");
        Console.WriteLine("  显示叠加层   ->   " + L10n.Tr("显示叠加层"));
        return 0;
    }

    private static string[] ToArray()
    {
        var list = new List<string>();
        foreach (string code in L10n.Available)
            list.Add(code);
        return list.ToArray();
    }
}
