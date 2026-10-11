using System;
using System.Collections.Generic;

namespace LaneReader
{
    /// <summary>
    /// Double presses: two notes closer together than the merge threshold are read as one press with
    /// two fingers, so the later note is pulled level with the earlier one and both are drawn on the
    /// same row. It is about what the chart looks like, not about which finger plays what, so it runs
    /// after every planner alike (<see cref="Plugin"/> calls it after all three).
    ///
    /// Only presses move. A hold's release is the same key as its head and its bar has to end where
    /// it does, so it never moves - and since the note after a hold head IS that release, no hold's
    /// length changes either. A threshold of 0 or less turns the whole thing off.
    /// </summary>
    public static class DoublePressMerge
    {
        /// <summary>
        /// Seconds of slack on the threshold. The times are doubles, so a gap that lands exactly on
        /// the threshold comes out a few 1e-17 either side of it: 0.95 - 0.9 evaluates to
        /// 0.050000000000000044, which missed the merge at every second pair of a 50 ms figure until
        /// this was here. HandPlanner and TimeResetPlanner carry the same slack for the same reason.
        /// </summary>
        private const double RoundOff = 1e-9;

        /// <summary>
        /// Pulls every note that follows its predecessor by no more than <paramref name="mergeMs"/>
        /// milliseconds up onto its predecessor's time, and returns how many moved.
        /// </summary>
        public static int Apply(IList<Note> notes, double mergeMs, bool holdChains)
        {
            double merge = mergeMs / 1000.0;
            if (double.IsNaN(merge) || double.IsInfinity(merge) || merge <= 0.0)
                return 0;

            int merged = 0;
            for (int i = 1; i < notes.Count; i++)
            {
                if (holdChains && notes[i - 1].HoldSeconds > 0.0)
                    continue;   // a hold's release, not a press: the bar ends here

                double gap = notes[i].HitTime - notes[i - 1].HitTime;
                if (gap <= 0.0 || gap > merge + RoundOff)
                    continue;

                Note note = notes[i];
                note.HitTime = notes[i - 1].HitTime;
                notes[i] = note;
                merged++;
            }

            return merged;
        }
    }
}
