using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LaneReader;

namespace LaneReader
{
    /// <summary>Just enough of a note for the planner to run outside Unity.</summary>
    public struct Note
    {
        public double HitTime;
        public int Lane;
        public double HoldSeconds;
    }
}

/// <summary>
/// Offline verifier for <see cref="TimeResetPlanner"/>.
///
///   TimeResetCheck                 - the built-in suite: uniform gaps, the figures that made the
///                                    hand-split planner misbehave, and holds
///   TimeResetCheck &lt;dump&gt; &lt;resetMs&gt; [inner]
///                                  - replay a reference-macro dump (TSX/TS lines, same format
///                                    HandPlanCheck reads) through the planner
/// </summary>
internal static class TimeResetCheck
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && File.Exists(args[0]))
            return Dump(args);

        Suite();
        return 0;
    }

    // ------------------------------------------------------------------ built-in suite

    private static void Suite()
    {
        Console.WriteLine("== uniform gaps (R = 300ms): one finger / hands take turns / roll ==");
        double[] gaps = { 100, 150, 151, 200, 250, 300, 301, 400, 600 };
        foreach (double g in gaps)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "g={0,4:F0}ms  inner {1,-18} outer {2}",
                g, Pattern(Repeat(g, 16), 300, false), Pattern(Repeat(g, 16), 300, true)));
        }

        Console.WriteLine();
        Console.WriteLine("== figures, R = 300ms (each row: one cycle's worth of gaps) ==");
        double[][] figures =
        {
            new double[] { 100, 100, 200 },
            new double[] { 100, 100, 300 },
            new double[] { 125, 125, 250 },
            new double[] { 200, 200, 400 },
            new double[] { 250, 250, 500 },
            new double[] { 100, 100, 100, 100 },
            new double[] { 50, 400 },
        };
        foreach (double[] figure in figures)
        {
            string head = "";
            foreach (double g in figure)
                head += (head.Length > 0 ? "+" : "") + g.ToString("F0", CultureInfo.InvariantCulture);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[{0}]ms  inner {1,-18} outer {2}", head,
                Pattern(Figure(figure, 24), 300, false), Pattern(Figure(figure, 24), 300, true)));
        }

        Console.WriteLine();
        Console.WriteLine("== holds, R = 300ms (head keeps the tail on its lane) ==");
        Hold("single hold head on the roll", SingleHold(), false);
        Hold("single hold head on the roll", SingleHold(), true);
        Hold("chained holds share one lane", ChainedHold(), false);
        Hold("chained holds share one lane", ChainedHold(), true);
    }

    private static void Hold(string what, List<Note> notes, bool outer)
    {
        string lanes = Lanes(notes, 300, outer);
        TimeResetPlanner.Result r = TimeResetPlanner.Assign(notes, 300, outer, true);
        string check = "ok";
        for (int i = 1; i < notes.Count; i++)
            if (notes[i - 1].HoldSeconds > 0.0 && notes[i].Lane != notes[i - 1].Lane)
                check = "TAIL OFF ITS HEAD'S LANE at " + i;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-32} {1,-5} lanes {2}  ({3} presses, {4} tails, {5})",
            what, outer ? "outer" : "inner", lanes, r.Presses, r.Tails, check));
    }

    /// <summary>Press, a hold that starts 50 ms later and runs 800 ms, then three more presses.</summary>
    private static List<Note> SingleHold()
    {
        var notes = new List<Note>();
        notes.Add(Press(0.0, -1.0));
        notes.Add(Press(0.05, 0.8));
        notes.Add(Press(0.85, -1.0));
        notes.Add(Press(0.9, -1.0));
        notes.Add(Press(0.95, -1.0));
        notes.Add(Press(1.0, -1.0));
        return notes;
    }

    /// <summary>Press, then two holds back to back (the second starts where the first ends).</summary>
    private static List<Note> ChainedHold()
    {
        var notes = new List<Note>();
        notes.Add(Press(0.0, -1.0));
        notes.Add(Press(0.05, 0.8));
        notes.Add(Press(0.85, 0.5));
        notes.Add(Press(1.35, -1.0));
        notes.Add(Press(1.4, -1.0));
        return notes;
    }

    // ------------------------------------------------------------------ dump mode

    private static int Dump(string[] args)
    {
        string path = args[0];
        double resetMs = double.Parse(args[1], CultureInfo.InvariantCulture);
        bool outer = !(args.Length > 2 && args[2] == "inner");

        string[] lines = File.ReadAllLines(path);
        bool haveExact = false;
        foreach (string line in lines)
            if (line.StartsWith("TSX "))
                haveExact = true;

        string prefix = haveExact ? "TSX " : "TS ";
        int offset = haveExact ? 4 : 3;
        var notes = new List<Note>();
        foreach (string line in lines)
            if (line.StartsWith(prefix))
                notes.Add(Press(double.Parse(line.Substring(offset).Trim(),
                    CultureInfo.InvariantCulture) / 1000.0, -1.0));

        if (notes.Count == 0)
        {
            Console.Error.WriteLine("no TS/TSX lines in " + path);
            return 2;
        }

        TimeResetPlanner.Result result = TimeResetPlanner.Assign(notes, resetMs, outer, false);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} notes, R={2:F0}ms, {3}", Path.GetFileName(path), notes.Count, resetMs,
            outer ? "outer" : "inner"));
        Console.WriteLine(result.Summary);
        for (int i = 0; i < notes.Count && i < 96; i++)
            Console.Write(Letter(notes[i].Lane));
        Console.WriteLine();
        return 0;
    }

    // ------------------------------------------------------------------ helpers

    private static Note Press(double hitTime, double holdSeconds)
    {
        var note = new Note();
        note.HitTime = hitTime;
        note.HoldSeconds = holdSeconds;
        return note;
    }

    private static List<Note> Repeat(double gapMs, int count)
    {
        var notes = new List<Note>();
        for (int i = 0; i < count; i++)
            notes.Add(Press(i * gapMs / 1000.0, -1.0));
        return notes;
    }

    private static List<Note> Figure(double[] gapsMs, int count)
    {
        var notes = new List<Note>();
        double t = 0.0;
        for (int i = 0; i < count; i++)
        {
            notes.Add(Press(t, -1.0));
            t += gapsMs[i % gapsMs.Length] / 1000.0;
        }
        return notes;
    }

    private static string Pattern(List<Note> notes, double resetMs, bool outer)
    {
        return TimeResetPlanner.Assign(notes, resetMs, outer, false).Pattern;
    }

    private static string Lanes(List<Note> notes, double resetMs, bool outer)
    {
        TimeResetPlanner.Assign(notes, resetMs, outer, true);
        var sb = new StringBuilder();
        foreach (Note note in notes)
            sb.Append(Letter(note.Lane));
        return sb.ToString();
    }

    private static string Letter(int lane)
    {
        switch (lane)
        {
            case 0: return "D";
            case 1: return "F";
            case 3: return "K";
            default: return "J";
        }
    }
}
