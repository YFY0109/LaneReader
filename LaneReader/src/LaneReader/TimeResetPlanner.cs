using System;
using System.Collections.Generic;

namespace LaneReader
{
    /// <summary>
    /// Fingers the chart from the press times alone - one number, the main-hand reset interval.
    /// No BPM, no angles, no grouping.
    ///
    /// The hand-split planner (<see cref="HandPlanner"/>) accumulates notes into windows of 180
    /// degrees and hands each window to a hand. The degrees it accumulates are the tile angle
    /// scaled by a normalisation factor (a power of two: see HandPlanner.NormaliseBpm) over the
    /// speed the tile is played at, so a window is 1/2^k of a chart beat rather than a fixed span
    /// of time. A three-note subdivision is the figure that suffers most from that: its three notes
    /// are 60 degrees apart, they only add up to one window when the factor happens to be exactly
    /// 1, and at every other factor the window boundary falls inside the group and drags the note
    /// after it in as well - which moves every following window by one and shows up as a passage
    /// whose fingering is suddenly wrong.
    ///
    /// This planner decides each press on its own instead, and reads nothing but the interval from
    /// the press before. Since it never groups anything, an evenly spaced run can only come out as
    /// one repeating pattern, whatever the tempo is doing.
    ///
    /// The four keys, and the names the rules use for them:
    ///
    ///     D 副轮 off-hand roll     F 副手 off-hand     J 主手 main hand     K 主轮 main-hand roll
    ///
    /// State is the finger the previous press used, plus the time of the last press that used the
    /// main hand and of the last one that used the off hand. With R the reset interval:
    ///
    ///     no state, or off-roll      -> main hand
    ///     main hand,  d > R          -> main hand   (slow: one finger can keep tapping)
    ///     main hand,  R/2 < d <= R   -> off hand    (the hands take the strong and weak beats)
    ///     main hand,  d <= R/2       -> main roll   (too fast for one finger, same hand rolls)
    ///     main roll,  d > R          -> main hand   (the fast passage is over)
    ///     main roll,  d <= R         -> off hand    (still in it, hand over)
    ///     off hand,   dOff <= R/2 or dMain <= R -> off roll   (the off hand has to roll too)
    ///     off hand,   otherwise      -> main hand
    ///
    /// where d is measured from the last main-hand press and dOff/dMain from the last off-hand and
    /// last main-hand press respectively. Every boundary is inclusive.
    ///
    /// A hold needs no special case beyond one: the note a hold ends on is the same key as the
    /// head, so it takes the head's finger and the clock moves on to it - the finger is only free
    /// again once it has let go. Chains (a hold whose tail is the next hold's head) come out on one
    /// lane for free.
    ///
    /// <paramref name="outerRoll"/> is a post-pass, run once the whole chart is decided: every roll
    /// press trades fingers with the press before it, which puts the outer finger down first
    /// (K J D F rather than J K F D). Because the roll pair is settled inside a hand, this only
    /// changes which of that hand's two fingers leads.
    /// </summary>
    public sealed class TimeResetPlanner
    {
        /// <summary>Milliseconds. One chart beat at 150 BPM; see Settings.ResetMs.</summary>
        public const double DefaultResetMs = 300.0;

        // The four keys, named for the role the rules give them.
        private const int OffRoll = (int)Finger.LeftMiddle;    // D
        private const int OffHand = (int)Finger.LeftIndex;     // F
        private const int MainHand = (int)Finger.RightIndex;   // J
        private const int MainRoll = (int)Finger.RightMiddle;  // K

        private const int PatternChars = 32;

        /// <summary>
        /// Seconds. The times are doubles, so a gap that lands exactly on a boundary - a three-note
        /// subdivision whose notes are exactly R/2 apart, say - comes out a few 1e-17 either side of
        /// it and the decision flips per note. Comparing with a nanosecond of slack makes the
        /// boundary mean what it says. HandPlanner carries the same slack for the same reason.
        /// </summary>
        private const double RoundOff = 1e-9;

        public struct Result
        {
            /// <summary>Notes the rules decided (a hold counts once, at its head).</summary>
            public int Presses;

            /// <summary>Notes that rode along on the note before them (a hold's release).</summary>
            public int Tails;

            /// <summary>Pairs the outer-roll pass swapped.</summary>
            public int Flips;

            /// <summary>Hand changes between consecutive presses.</summary>
            public int Switches;

            /// <summary>How many presses each finger got, indexed by <see cref="Finger"/>.</summary>
            public int[] Counts;

            /// <summary>The first presses as key letters, for the log.</summary>
            public string Pattern;

            /// <summary>Everything above, as one line.</summary>
            public string Summary;
        }

        /// <summary>
        /// Re-lanes every note in <paramref name="notes"/> (in HitTime order). <paramref name="resetMs"/>
        /// is the main-hand reset interval; 0 or invalid values fall back to the default.
        /// <paramref name="holdChains"/> ties each hold's release to its head, which is what the mod
        /// does whenever hold support is on.
        /// </summary>
        public static Result Assign(IList<Note> notes, double resetMs, bool outerRoll, bool holdChains)
        {
            var result = new Result();
            result.Counts = new int[FingeringPlanner.FingerCount];
            result.Pattern = "";

            int count = notes.Count;
            if (count == 0)
            {
                result.Summary = "no notes";
                return result;
            }

            double reset = resetMs / 1000.0;
            if (double.IsNaN(reset) || double.IsInfinity(reset) || reset <= 0.0001)
                reset = DefaultResetMs / 1000.0;
            double half = reset * 0.5;

            int[] finger = new int[count];
            double lastMain = double.NegativeInfinity;
            double lastOff = double.NegativeInfinity;
            int state = -1;   // nothing decided yet

            for (int i = 0; i < count; i++)
            {
                double t = notes[i].HitTime;
                int f;

                if (holdChains && i > 0 && notes[i - 1].HoldSeconds > 0.0)
                {
                    // The note a hold ends on: same key as the head, so no rule applies to it.
                    f = finger[i - 1];
                }
                else if (state < 0 || state == OffRoll)
                {
                    f = MainHand;
                }
                else if (state == MainHand)
                {
                    double d = t - lastMain;
                    if (d > reset + RoundOff)
                        f = MainHand;
                    else if (d > half + RoundOff)
                        f = OffHand;
                    else
                        f = MainRoll;
                }
                else if (state == MainRoll)
                {
                    // Measured from the main-hand press before the roll - the note the roll answered.
                    f = (t - lastMain) > reset + RoundOff ? MainHand : OffHand;
                }
                else
                {
                    double dOff = t - lastOff;
                    double dMain = t - lastMain;
                    f = (dOff <= half + RoundOff || dMain <= reset + RoundOff) ? OffRoll : MainHand;
                }

                // The clock always moves to this note, hold or not: while a hold is down the finger
                // is busy, and it is free again at the note the hold ends on.
                if (f == MainHand)
                    lastMain = t;
                else if (f == OffHand)
                    lastOff = t;

                state = f;
                finger[i] = f;
            }

            if (outerRoll)
                OuterPass(notes, finger, holdChains, ref result);

            int previousPress = -1;
            for (int i = 0; i < count; i++)
            {
                Note note = notes[i];
                note.Lane = finger[i];
                notes[i] = note;
                result.Counts[finger[i]]++;

                bool isTail = holdChains && i > 0 && notes[i - 1].HoldSeconds > 0.0;
                if (isTail)
                {
                    result.Tails++;
                    continue;
                }

                result.Presses++;
                if (previousPress >= 0
                    && FingeringPlanner.HandOf(finger[i]) != FingeringPlanner.HandOf(finger[previousPress]))
                    result.Switches++;
                if (result.Pattern.Length < PatternChars)
                    result.Pattern += KeyLetter(finger[i]);
                previousPress = i;
            }

            result.Summary = string.Format(
                "{0:F0}ms reset, {1}, D={2} F={3} J={4} K={5}, {6} presses ({7} hold tails), {8} hand switches{9}, {10}{11}",
                reset * 1000.0, outerRoll ? "outer roll" : "inner roll",
                result.Counts[OffRoll], result.Counts[OffHand],
                result.Counts[MainHand], result.Counts[MainRoll],
                result.Presses, result.Tails, result.Switches,
                result.Flips > 0 ? ", " + result.Flips + " outer swaps" : "",
                result.Pattern, result.Presses > PatternChars ? "..." : "");

            return result;
        }

        /// <summary>
        /// Puts the outer finger down first: each roll press trades fingers with the press before
        /// it. A release is not a press, so it never takes part - but a hold whose head ends up on
        /// the roll sits on it for its whole length, so the notes it runs over follow it there.
        /// </summary>
        private static void OuterPass(IList<Note> notes, int[] finger, bool holdChains, ref Result result)
        {
            int previousPress = -1;
            for (int i = 0; i < notes.Count; i++)
            {
                if (holdChains && i > 0 && notes[i - 1].HoldSeconds > 0.0)
                    continue;

                if (previousPress >= 0 && (finger[i] == MainRoll || finger[i] == OffRoll))
                {
                    int kept = finger[previousPress];
                    finger[previousPress] = finger[i];
                    finger[i] = kept;
                    result.Flips++;
                }

                previousPress = i;
            }

            if (!holdChains)
                return;

            for (int i = 1; i < notes.Count; i++)
            {
                if (notes[i - 1].HoldSeconds > 0.0)
                    finger[i] = finger[i - 1];
            }
        }

        private static string KeyLetter(int finger)
        {
            switch (finger)
            {
                case OffRoll: return "D";
                case OffHand: return "F";
                case MainRoll: return "K";
                default: return "J";
            }
        }
    }
}
