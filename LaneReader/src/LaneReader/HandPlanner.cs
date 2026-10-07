using System;
using System.Collections.Generic;
using System.Text;

namespace LaneReader
{
    /// <summary>
    /// What one hand-split pass produced: a lane for every note, plus the tallies the diagnostic
    /// log prints.
    /// </summary>
    public sealed class HandPlan
    {
        /// <summary>Lane (0..3) for every note, in the order the notes were passed in.</summary>
        public int[] Lanes = new int[0];

        /// <summary>
        /// Finger slot per note: +N is the main hand's nth key, -N the off hand's. The lane is
        /// derived from this, and it is the vocabulary the reference implementation talks in, so
        /// the verification harness compares these instead of lanes.
        /// </summary>
        public int[] Slots = new int[0];

        /// <summary>Window decisions, for the log and for the verification harness.</summary>
        public readonly List<int> WindowCount = new List<int>();
        public readonly List<double> WindowDegree = new List<double>();
        public readonly List<bool> WindowPreferred = new List<bool>();

        /// <summary>Histogram over <see cref="WindowCount"/>: index 1..3 are counts, index 4 is "4+".</summary>
        public readonly int[] WindowHistogram = new int[5];

        public int Rolls;
        public int Taps;
        public double BaseBpm;
        public string Summary = "";

        /// <summary>e.g. "count1=340 count2=24 count3=2" - the shape of the chart, in one line.</summary>
        public string Histogram()
        {
            var sb = new StringBuilder();
            for (int i = 1; i < WindowHistogram.Length; i++)
            {
                if (WindowHistogram[i] == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append("count").Append(i);
                if (i == WindowHistogram.Length - 1)
                    sb.Append('+');
                sb.Append('=').Append(WindowHistogram[i]);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }

    /// <summary>
    /// Splits a chart between the two hands, ported from ReADOFAIMacro
    /// (https://github.com/TonyLimps/ReADOFAIMacro, MIT) - src/PlayScript.cpp, its PlayScript()
    /// constructor and allocateFingers().
    ///
    /// The reference is a macro that plays a chart by itself, so it has to decide the fingering.
    /// This is that decision, in full:
    ///
    ///   1. Normalise the chart's BPM: double it until it is above 550, halve it until it is at most
    ///      1100, then halve it once more if it is still above the player's hard-resist limit.
    ///      Everything below is measured in beats of that normalised BPM (baseBpm), which is what
    ///      lets one fingering survive a chart that changes speed - and the octave drop is the
    ///      reference's own way of playing more with rolls than with two-finger resistance.
    ///   2. Cut the notes into windows: walk them, adding each one's length in degrees (180 degrees
    ///      being one normalised beat), and close the window as soon as it reaches 180. A window is
    ///      one beat's worth of music, and its count is how many presses fit in it.
    ///   3. Give each window a pattern (see <see cref="Emit"/>): one press is one finger, N presses
    ///      are N fingers of one hand in descending order, and a window longer than one hand is
    ///      split between the hands and recursed into.
    ///   4. Decide which hand starts the next window from this window's length in degrees (see
    ///      <see cref="NextPreferred"/>). That one rule is what makes a slow passage come out as one
    ///      finger repeating and a fast one as two hands alternating.
    ///
    /// Only note times are needed. The reference reconstructs times from a level file's tile angles,
    /// twirls, pauses and speed events; in the running game every note already carries its own entry
    /// time, so none of that is repeated here.
    ///
    /// tools/HandPlanCheck.cs checks this against the reference's own output, window by window.
    /// </summary>
    public static class HandPlanner
    {
        /// <summary>The reference's default hard-resist limit (its readme suggests 800..1100).</summary>
        public const double DefaultMaxHardResistBpm = 840.0;

        /// <summary>
        /// One normalised beat, in degrees. The reference accumulates tile rotation until a window
        /// reaches 180 degrees, which is one beat of planet travel; this constant is what turns a
        /// note's length into the numbers its hand rule is written against.
        /// </summary>
        private const double DegreesPerBeat = 180.0;

        /// <summary>
        /// Slack of last resort, in degrees, used only where two floating point routes to the same
        /// number disagree in their last bits: the reference accumulates exact rational angles, while
        /// these windows are summed from note times. It is far below any difference between two real
        /// windows, and it deliberately does not move any threshold - the reference's numbers are
        /// what they are, and stretching them past round-off changes the fingering.
        /// </summary>
        private const double RoundOff = 1e-9;

        /// <summary>
        /// Assigns every note a finger. <paramref name="hitTimes"/> are seconds, in chart order;
        /// <paramref name="handKeys"/> is how many keys one hand commands - 2 for the DFJK layout,
        /// 4 to reproduce the reference (which is only used to verify this port).
        /// </summary>
        public static HandPlan Assign(IList<double> hitTimes, double levelBpm,
            double maxHardResistBpm, int handKeys, bool outerRoll)
        {
            var plan = new HandPlan();
            if (hitTimes == null || hitTimes.Count == 0)
                return plan;

            int count = hitTimes.Count;
            plan.Lanes = new int[count];
            plan.Slots = new int[count];

            double baseBpm = NormaliseBpm(levelBpm, maxHardResistBpm);
            plan.BaseBpm = baseBpm;

            bool preferred = true;
            int first = 0;

            while (first < count)
            {
                // One window: notes until the accumulated length covers a normalised beat. The
                // reference's loop is a do/while, so a window always holds at least one note.
                double degree = DegreeOf(hitTimes, first, baseBpm);
                int window = 1;
                while (degree < DegreesPerBeat - RoundOff && first + window < count)
                {
                    degree += DegreeOf(hitTimes, first + window, baseBpm);
                    window++;
                }

                plan.WindowCount.Add(window);
                plan.WindowDegree.Add(degree);
                plan.WindowPreferred.Add(preferred);
                plan.WindowHistogram[window < plan.WindowHistogram.Length
                    ? window : plan.WindowHistogram.Length - 1]++;

                if (window >= 2)
                    plan.Rolls++;
                else
                    plan.Taps++;

                Emit(plan, first, window, preferred, handKeys, outerRoll);
                preferred = NextPreferred(preferred, degree);
                first += window;
            }

            plan.Summary = string.Format("windows {0}, {1}, base {2:F2}bpm",
                plan.WindowCount.Count, plan.Histogram(), baseBpm);
            return plan;
        }

        /// <summary>
        /// The reference's BPM normalisation, verbatim:
        /// <code>
        /// while (baseBpm &lt;= 550) baseBpm *= 2;
        /// while (baseBpm &gt; 1100) baseBpm /= 2;
        /// if (baseBpm &gt; maxIndexingBpm) baseBpm /= 2;
        /// </code>
        /// A level BPM that cannot be read falls back to the reference's own idea of something sane,
        /// 100. Everything the hand rule does is measured against this number, so it is worth logging
        /// (the caller prints it with its source).
        /// </summary>
        private static double NormaliseBpm(double levelBpm, double maxHardResistBpm)
        {
            double bpm = levelBpm;
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0.0)
                bpm = 100.0;

            while (bpm <= 550.0)
                bpm *= 2.0;
            while (bpm > 1100.0)
                bpm /= 2.0;
            if (maxHardResistBpm > 0.0 && bpm > maxHardResistBpm)
                bpm /= 2.0;
            return bpm;
        }

        /// <summary>
        /// A note's length in degrees of normalised rotation: 180 * (seconds * baseBpm / 60), which
        /// is 3 * seconds * baseBpm.
        ///
        /// The reference derives the same quantity from the tile's turn angle scaled by the speed
        /// events, and appends a copy of its last angle as a sentinel so its loop can look one step
        /// ahead - which is why the final note is measured by the gap before it rather than by zero.
        /// </summary>
        private static double DegreeOf(IList<double> hitTimes, int note, double baseBpm)
        {
            double seconds;
            if (note + 1 < hitTimes.Count)
                seconds = hitTimes[note + 1] - hitTimes[note];
            else if (note > 0)
                seconds = hitTimes[note] - hitTimes[note - 1];
            else
                return 0.0;

            if (seconds < 0.0)
                seconds = 0.0;                     // a rewind, or two notes on the same instant
            return DegreesPerBeat * seconds * baseBpm / 60.0;
        }

        /// <summary>
        /// The reference's hand rule, verbatim: the window that just ended decides whether the next
        /// one starts on the same hand.
        ///
        ///   * a one-beat window (up to 270 degrees) hands over, so the hands alternate;
        ///   * anything up to 450 degrees keeps the same hand;
        ///   * up to 630 alternates again, and up to 720 keeps;
        ///   * a window of four beats or more returns to the main hand, which is what turns a slow
        ///     passage into one finger repeating.
        /// </summary>
        private static bool NextPreferred(bool preferred, double degree)
        {
            if (degree <= 270.0 + RoundOff)
                return !preferred;
            if (degree <= 450.0 + RoundOff)
                return preferred;
            if (degree <= 630.0 + RoundOff)
                return !preferred;
            if (degree < 720.0 - RoundOff)
                return preferred;
            return true;
        }

        /// <summary>
        /// Lays one window's notes onto the hands, following allocateFingers(): a hand holds
        /// <paramref name="handKeys"/> keys, and the reference plays them in descending order (its
        /// p2 before p1, which on DFJK is the middle finger before the index finger - the "outer"
        /// roll). <paramref name="outerRoll"/> false reverses that pair, which is the inner roll. A
        /// window longer than one hand is split between the hands and recursed into, in the same
        /// order the reference uses: the hand that started keeps the smaller half.
        /// </summary>
        private static void Emit(HandPlan plan, int first, int window, bool mainHand, int handKeys, bool outerRoll)
        {
            if (window <= 0)
                return;

            if (window <= handKeys)
            {
                for (int i = 0; i < window; i++)
                {
                    int slot = outerRoll ? window - i : i + 1;
                    Place(plan, first + i, mainHand, slot);
                }
                return;
            }

            int thisHand = window / 2;
            Emit(plan, first, thisHand, mainHand, handKeys, outerRoll);
            Emit(plan, first + thisHand, window - thisHand, !mainHand, handKeys, outerRoll);
        }

        private static void Place(HandPlan plan, int note, bool mainHand, int slot)
        {
            plan.Slots[note] = mainHand ? slot : -slot;
            plan.Lanes[note] = LaneOf(mainHand, slot);
        }

        /// <summary>
        /// Lane for a finger slot. The main hand is the right one, so slot 1 (the index finger,
        /// where the hand rests) is J for it and F for the off hand; slot 2 is the middle finger, K
        /// and D. The DFJK layout only has those two slots per hand - larger slots only arise while
        /// reproducing the reference, which commands four.
        /// </summary>
        private static int LaneOf(bool mainHand, int slot)
        {
            if (mainHand)
                return slot == 1 ? 2 : 3;      // J, then K
            return slot == 1 ? 1 : 0;          // F, then D
        }

        /// <summary>The reference's key vocabulary for a slot: p1..pN main hand, n1..nN off hand.</summary>
        public static string SymbolOf(int slot)
        {
            if (slot == 0)
                return "--";
            return (slot > 0 ? "p" : "n") + (slot > 0 ? slot : -slot).ToString();
        }
    }
}
