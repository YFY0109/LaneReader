using System;
using System.Collections.Generic;

namespace LaneReader
{
    /// <summary>Which hand a key belongs to, for the colour coding.</summary>
    public enum Hand
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>
    /// The four keys of the DFJK layout, in screen order from left to right.
    ///
    /// The hands rest on F and J (the index fingers); D and K are the middle fingers.
    /// </summary>
    public enum Finger
    {
        LeftMiddle = 0,   // D
        LeftIndex = 1,    // F
        RightIndex = 2,   // J
        RightMiddle = 3,  // K
    }

    /// <summary>
    /// Plans a human fingering for the chart, ported verbatim from AdoFalling's
    /// FingeringPlanner so the lane behaviour matches that mod exactly.
    ///
    /// The rules model what two hands on a 4-key row actually do:
    ///
    ///   * notes closer together than the roll window ("RollWindowMs", 180 ms by default)
    ///     cannot be repeated by one finger, so they are played as a roll (轮指): the lane walks
    ///     one step in the current direction every note, wrapping around the four lanes. Each
    ///     new fast run flips the direction, which reads as the hands taking turns;
    ///   * slower notes are played with the two index fingers, so the lane alternates between
    ///     the inner lanes 1 and 2 instead of staying on one key;
    ///   * the result never places two consecutive notes on the same lane.
    ///
    /// Lower the window to roll more, raise it to tap more.
    /// </summary>
    public sealed class FingeringPlanner
    {
        public const int FingerCount = 4;
        public const double DefaultWindowMs = 180.0;

        /// <summary>The lanes the slow notes alternate between (the index fingers, F and J).</summary>
        private const int TapLaneA = (int)Finger.LeftIndex;    // F
        private const int TapLaneB = (int)Finger.RightIndex;   // J

        public struct Result
        {
            public int Rolls;
            public int Taps;
            public int Chords;
            public int[] LaneUse;
            public double WindowMs;
        }

        /// <summary>
        /// Re-lanes every note in <paramref name="notes"/> (in HitTime order) and returns the
        /// tallies for the log. <paramref name="windowMs"/> is the largest gap (milliseconds)
        /// that still counts as a roll; 0 or invalid values fall back to the default window.
        /// </summary>
        public static Result Assign(IList<Note> notes, double windowMs)
        {
            var result = new Result();
            result.LaneUse = new int[FingerCount];

            int count = notes.Count;
            if (count == 0)
                return result;

            double window = windowMs / 1000.0;
            if (double.IsNaN(window) || double.IsInfinity(window) || window <= 0.0001)
                window = DefaultWindowMs / 1000.0;
            result.WindowMs = window * 1000.0;

            int lane = TapLaneA;
            int direction = 1;
            bool inRoll = false;

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    double gap = notes[i].HitTime - notes[i - 1].HitTime;
                    if (gap < window - 1e-9)
                    {
                        // Roll: step one lane. A fresh run reverses the direction so the hands
                        // alternate instead of always sweeping the same way.
                        if (!inRoll)
                            direction = -direction;
                        lane = (lane + direction + FingerCount) % FingerCount;
                        inRoll = true;
                        result.Rolls++;
                    }
                    else
                    {
                        // Tap: alternate the two index lanes.
                        lane = (lane == TapLaneB) ? TapLaneA : TapLaneB;
                        inRoll = false;
                        result.Taps++;
                    }
                }

                Note note = notes[i];
                note.Lane = lane;
                notes[i] = note;
                result.LaneUse[lane]++;
            }

            return result;
        }

        /// <summary>Lane 0 and 1 are the left hand's keys, 2 and 3 the right hand's.</summary>
        public static Hand HandOf(int lane)
        {
            return lane <= (int)Finger.LeftIndex ? Hand.Left : Hand.Right;
        }

        /// <summary>Key label for a lane, for logs and the key strip.</summary>
        public static string KeyName(int lane)
        {
            switch (lane)
            {
                case (int)Finger.LeftMiddle: return "D";
                case (int)Finger.LeftIndex: return "F";
                case (int)Finger.RightIndex: return "J";
                case (int)Finger.RightMiddle: return "K";
                default: return "?";
            }
        }
    }
}
