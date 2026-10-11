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
///   TimeResetCheck [resetMs]        - the built-in suite at that reset interval (default 300):
///                                    uniform gaps, the figures that made the hand-split planner
///                                    misbehave, and holds
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

        // Optional first argument: the reset interval to run the suite at.
        double resetMs = 300.0;
        if (args.Length >= 1)
            double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out resetMs);
        if (resetMs <= 0.0)
            resetMs = 300.0;

        Suite(resetMs);
        return 0;
    }

    // ------------------------------------------------------------------ built-in suite

    private static void Suite(double resetMs)
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "== uniform gaps (R = {0:F0}ms): one finger / hands take turns / roll ==", resetMs));
        double[] gaps = { 50, 75, 76, 100, 150, 151, 200, 300, 400 };
        foreach (double g in gaps)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "g={0,4:F0}ms  inner {1,-18} outer {2}",
                g, Pattern(Repeat(g, 16), resetMs, false), Pattern(Repeat(g, 16), resetMs, true)));
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "== figures, R = {0:F0}ms (each row: one cycle's worth of gaps) ==", resetMs));
        double[][] figures =
        {
            new double[] { 100, 100, 200 },
            new double[] { 50, 50, 100 },
            new double[] { 80, 150 },
            new double[] { 80, 220 },
            new double[] { 100, 100, 300 },
            new double[] { 125, 125, 250 },
            new double[] { 200, 200, 400 },
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
                Pattern(Figure(figure, 24), resetMs, false), Pattern(Figure(figure, 24), resetMs, true)));
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "== holds, R = {0:F0}ms (head keeps the tail on its lane) ==", resetMs));
        Hold("single hold head on the roll", SingleHold(), false, resetMs);
        Hold("single hold head on the roll", SingleHold(), true, resetMs);
        Hold("chained holds share one lane", ChainedHold(), false, resetMs);
        Hold("chained holds share one lane", ChainedHold(), true, resetMs);

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "== double-press merge (DoublePressMerge), R = {0:F0}ms, merge = 50ms ==", resetMs));
        MergeDemo("50+400", new double[] { 50, 400 }, resetMs, 50.0);
        MergeDemo("40+40+40", new double[] { 40, 40, 40 }, resetMs, 50.0);
        MergeDemo("100+100+200", new double[] { 100, 100, 200 }, resetMs, 50.0);
        MergeDemo("50+400 merge off", new double[] { 50, 400 }, resetMs, 0.0);
    }

    private static void MergeDemo(string what, double[] gapsMs, double resetMs, double mergeMs)
    {
        List<Note> notes = Figure(gapsMs, 10);
        TimeResetPlanner.Assign(notes, resetMs, false, false);          // lanes first
        int merged = DoublePressMerge.Apply(notes, mergeMs, false);      // then the double presses
        var sb = new StringBuilder();
        foreach (Note note in notes)
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F0}{1} ", note.HitTime * 1000.0, Letter(note.Lane));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-18} {1}  (merged {2})", what, sb.ToString().Trim(), merged));
    }

    private static void Hold(string what, List<Note> notes, bool outer, double resetMs)
    {
        string lanes = Lanes(notes, resetMs, outer);
        TimeResetPlanner.Result r = TimeResetPlanner.Assign(notes, resetMs, outer, true);
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
