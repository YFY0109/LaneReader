using System;
using System.Collections.Generic;
using System.Text;

namespace LaneReader
{
    /// <summary>
    /// What one hand-split pass produced: a lane for every note, plus the tallies the
    /// diagnostic log prints.
    /// </summary>
    public sealed class HandPlan
    {
        /// <summary>Lane (0..3) for every note, in the order the notes were passed in.</summary>
        public int[] Lanes = new int[0];

        /// <summary>
        /// Finger slot per note: +N is the main hand's nth key, -N the off hand's. The lane is
        /// derived from this, and it is the vocabulary the reference implementation talks in,
        /// so the verification harness compares these instead of lanes.
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
    /// Splits a chart between the two hands and picks the finger for every note.
    ///
    /// This is a port of ReADOFAIMacro's fingering allocation
    /// (https://github.com/TonyLimps/ReADOFAIMacro, MIT). That project is a macro that plays a
    /// chart automatically, so it has to decide the fingering itself; the point here is to
    /// reuse a method that already works rather than invent one.
    ///
    /// Its model, and therefore this one:
    ///
    ///   * the chart's BPM is first normalised into 550..1100 by doubling, then halved once
    ///     more if it is still above the player's hard-resist limit. Everything below is
    ///     measured in beats of that normalised BPM, which is what makes a chart behave the
    ///     same at any tempo;
    ///   * notes are cut into windows: keep taking notes until their combined length reaches
    ///     one normalised beat. A window is therefore "one beat's worth of music" - a single
    ///     press is a slow note, two or more presses inside one beat is a stream that has to be
    ///     rolled, and a zero-length press (a chord, a midspin pair) lands in the same window
    ///     for free, which is why pseudo-doubles come out as roll material;
    ///   * the press count picks the pattern: one press is a single finger, N presses are N
    ///     fingers of one hand in descending order, and more than the hand holds is split
    ///     between the hands recursively;
    ///   * the hand that starts the next window is decided by the window's length: a one-beat
    ///     window hands over (the hands alternate), a two-beat window keeps the same hand (one
    ///     finger repeating), and so on. That rule is lifted verbatim from the reference, see
    ///     <see cref="NextPreferred"/>.
    ///
    /// Only note times are needed. The reference reconstructs times from a level file's tile
    /// angles, twirls, pauses and speed events; in the running game every note already carries
    /// its own entry time, so none of that has to be repeated here.
    /// </summary>
    public static class HandPlanner
    {
        /// <summary>The reference's default hard-resist limit (its readme suggests 800..1100).</summary>
        public const double DefaultMaxHardResistBpm = 840.0;

        /// <summary>
        /// One normalised beat, in "degrees". The reference accumulates tile rotation until the
        /// window reaches 180 degrees, which is one beat of planet travel; the constant is what
        /// turns a note's length into the numbers its hand rule is written against.
        /// </summary>
        private const double DegreesPerBeat = 180.0;

        /// <summary>
        /// Assigns every note a finger. <paramref name="hitTimes"/> are seconds, in chart order;
        /// <paramref name="handKeys"/> is how many keys one hand commands - 2 for the DFJK
        /// layout, 4 to reproduce the reference (which is only used to verify this port).
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

            // What one *chart* beat is worth in this scale. The window lengths above are measured in
            // beats of the normalised BPM, which runs several octaves faster than the chart itself,
            // so a tile that is a whole chart beat long is worth this many degrees - and the
            // reference's own hand rule already treats 720 degrees as "a long note". At the default
            // limit the two agree exactly.
            double chartBeat = levelBpm > 0.0
                ? DegreesPerBeat * baseBpm / levelBpm
                : DegreesPerBeat;

            bool preferred = true;
            int first = 0;

            while (first < count)
            {
                // One window: notes until the accumulated length covers a normalised beat.
                double degree = 0.0;
                int window = 0;
                while (first + window < count)
                {
                    double own = DegreeOf(hitTimes, first + window, baseBpm);

                    // A note that is a whole beat or more on its own is a slow note, not a
                    // subdivision of one, so it must not be glued to the note before it. Without
                    // this, one tiny tile (a shallow turn, short in time) followed by a long one
                    // adds up to a beat and came out as a two-finger roll - a roll between two
                    // notes that are nowhere near each other. The reference happily does that; it was
                    // written for unbroken streams, and this is a deliberate departure from it.
                    if (window > 0 && own >= chartBeat - Epsilon)
                        break;

                    degree += own;
                    window++;
                    if (degree >= DegreesPerBeat - Epsilon)
                        break;
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

            plan.Summary = string.Format("windows {0}, {1}, base {2:F0}bpm",
                plan.WindowCount.Count, plan.Histogram(), baseBpm);
            return plan;
        }

        /// <summary>
        /// Tolerance in degrees, and it is deliberately tiny: enough to absorb the double
        /// round-off between the reference's exact rational angles and our summed note times
        /// (which agree to about 1e-13), and nothing more.
        ///
        /// Wider tolerances were tried and are wrong. Charts really do produce window lengths a
        /// hair under a threshold - one chart here has windows of 239.99994, 479.99988 and
        /// 719.99982 degrees throughout, from a speed multiplier that is not quite 1 - and the
        /// reference keeps the same hand for those. Rounding them onto the threshold flipped the
        /// hand and never flipped it back.
        /// </summary>
        private const double Epsilon = 1e-9;

        /// <summary>
        /// The reference's hand rule, verbatim: the window's length decides whether the next
        /// window starts on the same hand. A one-beat window alternates; three beats also
        /// alternates; long notes fall back to the main hand, which is what makes a slow passage
        /// come out as one finger repeating.
        /// </summary>
        private static bool NextPreferred(bool preferred, double degree)
        {
            if (degree <= 270.0 + Epsilon)
                return !preferred;
            if (degree <= 450.0 + Epsilon)
                return preferred;
            if (degree <= 630.0 + Epsilon)
                return !preferred;
            if (degree < 720.0 - Epsilon)
                return preferred;
            return true;
        }

        /// <summary>
        /// Lays one window's notes onto the hands. A hand holds <paramref name="handKeys"/> keys
        /// and the reference plays them in descending order (middle finger first, index last -
        /// the "outer roll" the mod defaults to); <paramref name="outerRoll"/> false reverses
        /// that pair, which is the inner roll. Anything longer than one hand is split between
        /// the hands and recursed into, exactly as the reference does.
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
        /// where the hand rests) is J for it and F for the off hand; slot 2 is the middle finger,
        /// K and D. The DFJK layout only has those two slots per hand - anything larger only
        /// arises while reproducing the reference, which uses four.
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

        /// <summary>
        /// A note's length in degrees of normalised rotation. The reference derives this from
        /// the tile's turn angle scaled by the speed events; the same quantity is just
        /// 180 * (seconds * baseBpm / 60) = 3 * seconds * baseBpm, because the turn angle is
        /// already baked into the note's entry time.
        /// </summary>
        private static double DegreeOf(IList<double> hitTimes, int note, double baseBpm)
        {
            double seconds;
            if (note + 1 < hitTimes.Count)
                seconds = hitTimes[note + 1] - hitTimes[note];
            else if (note > 0)
                seconds = hitTimes[note] - hitTimes[note - 1];   // the reference's sentinel
            else
                return 0.0;

            if (seconds < 0.0)
                seconds = 0.0;                 // a rewind, or two notes on the same instant
            return DegreesPerBeat * seconds * baseBpm / 60.0;
        }

        /// <summary>
        /// The reference's BPM normalisation: lift the chart into 550..1100 so that a window is
        /// always a fraction of a beat rather than a fixed number of milliseconds, then drop one
        /// more octave if the chart is still faster than the player's hard-resist limit. Lower
        /// limits therefore roll more, because windows get shorter.
        /// </summary>
        private static double NormaliseBpm(double levelBpm, double maxHardResistBpm)
        {
            double bpm = levelBpm;
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0.0)
                bpm = 100.0;                   // no usable BPM: anything sane will do
            while (bpm <= 550.0)
                bpm *= 2.0;
            while (bpm > 1100.0)
                bpm /= 2.0;

            // A hair over the limit must not count as over it. Charts whose tempo lands exactly on
            // the limit fall in this trap: 210 BPM normalises to 840, which is the default limit, so
            // a reading of 210.5 would drop a whole octave and double every window - and with it the
            // fingering changes completely. Measured: 210.0 -> 840, 210.5 -> 421. One BPM of slack is
            // below anything a slider can express and far below any real difference between charts.
            if (maxHardResistBpm > 0.0 && bpm > maxHardResistBpm + 1.0)
                bpm /= 2.0;

            // The limit then scales the windows continuously. On its own the octave drop above is a
            // cliff that only ever fires once - on a 220 BPM chart every setting from 400 to 840
            // halves it to 440 and the slider does nothing at all, which is worse than useless on a
            // control the player is supposed to tune by feel. Scaling the other way makes "lower =
            // rolls more" true across the whole range, and at the default limit the factor is 1, so
            // the reference's own behaviour is what runs.
            if (maxHardResistBpm > 0.0)
                bpm *= maxHardResistBpm / DefaultMaxHardResistBpm;
            return bpm;
        }
    }
}
