using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LaneReader;

/// <summary>
/// Offline verifier for <see cref="HandPlanner"/>.
///
/// It eats a dump produced by the reference macro (see the ANALYSIS patch in ReADOFAIMacro:
/// LEVELBPM / BASEBPM / MAXBPM plus one "TS &lt;ms&gt;" line per note) and replays the same
/// note times through our port, printing the per-window decisions in the macro's own format so
/// the two can be diffed mechanically:
///
///   WIN &lt;i&gt; count=&lt;n&gt; degree=&lt;d&gt; pref=&lt;0|1&gt;     - the decision layer
///   SEQ &lt;i&gt; p2 p1 ...                     - the finger pattern it produced
///
///   HandPlanCheck &lt;dump.txt&gt; &lt;handKeys&gt; [inner]
///
/// handKeys 4 reproduces the reference (four keys per hand) and must match it exactly; 2 is the
/// DFJK layout the mod actually uses.
/// </summary>
internal static class HandPlanCheck
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: HandPlanCheck <dump.txt> <handKeys> [inner]");
            return 2;
        }

        string path = args[0];
        int handKeys = int.Parse(args[1], CultureInfo.InvariantCulture);
        bool outerRoll = !(args.Length > 2 && args[2] == "inner");

        double levelBpm = 0.0;
        double maxBpm = HandPlanner.DefaultMaxHardResistBpm;
        var times = new List<double>();

        // Two passes over the dump: the exact times (TSX) are preferred, and only when the dump
        // has none do the rounded "TS" lines get used. Reading both into one list would double
        // the note count, which is exactly the bug this comment exists to prevent.
        string[] lines = File.ReadAllLines(path);
        string exactPrefix = "TSX ";
        bool haveExact = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("LEVELBPM "))
                levelBpm = Parse(line, 9);
            else if (line.StartsWith("MAXBPM "))
                maxBpm = Parse(line, 7);
            else if (line.StartsWith(exactPrefix))
                haveExact = true;
        }

        string timePrefix = haveExact ? exactPrefix : "TS ";
        int timeOffset = haveExact ? 4 : 3;
        foreach (string line in lines)
        {
            if (line.StartsWith(timePrefix))
                times.Add(Parse(line, timeOffset) / 1000.0);
        }

        if (times.Count == 0)
        {
            Console.Error.WriteLine("no TS lines in " + path);
            return 2;
        }

        HandPlan plan = HandPlanner.Assign(times, levelBpm, maxBpm, handKeys, outerRoll);

        for (int i = 0; i < plan.WindowCount.Count; i++)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "WIN {0} count={1} degree={2:R} pref={3}",
                i, plan.WindowCount[i], plan.WindowDegree[i],
                plan.WindowPreferred[i] ? 1 : 0));
        }

        int first = 0;
        for (int i = 0; i < plan.WindowCount.Count; i++)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < plan.WindowCount[i]; k++)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(HandPlanner.SymbolOf(plan.Slots[first + k]));
            }
            Console.WriteLine("SEQ " + i.ToString(CultureInfo.InvariantCulture) + " " + sb);
            first += plan.WindowCount[i];
        }

        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} notes, {2}", Path.GetFileName(path), times.Count, plan.Summary));
        return 0;
    }

    private static double Parse(string line, int offset)
    {
        return double.Parse(line.Substring(offset).Trim(), CultureInfo.InvariantCulture);
    }
}
