using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using UnityModManagerNet;

namespace LaneReader
{
    /// <summary>
    /// The tunable values, plain fields so XmlSerializer round-trips them without ceremony.
    ///
    /// These are deliberately NOT decorated for UnityModManager's reflection-based drawer.
    /// That drawer renders nothing for this mod no matter which mask is passed, and every mod
    /// that actually works on this install (JALib, BetterCalibration, JipperResourcePack) draws
    /// its own panel with GUILayout instead. This type is only storage.
    /// </summary>
    [Serializable]
    public class Settings
    {
        public bool Enabled = true;
        public int ToggleKey = (int)KeyCode.F8;

        public bool ShowLanes = true;
        public bool ShowJudgeLine = true;
        public bool ShowNotes = true;
        public bool ShowKeyLabels = true;

        /// <summary>Judgement boxes: one note-sized rectangle per lane at the line.</summary>
        public bool ShowJudgeBoxes = true;

        /// <summary>Strike bounce on the judgement line; purely time driven, the game's judgement
        /// is never read.</summary>
        public bool ShowHitEffects = true;

        /// <summary>Draw every note with a darker outline, so notes near each other stay readable.</summary>
        public bool ShowNoteBorder = true;

        /// <summary>Outline thickness, in reference pixels (1080p).</summary>
        public float NoteBorderPx = 2f;

        public float NoteSpeedPxPerSec = 620f;
        public float NoteHeightPx = 26f;
        public float LaneWidthPx = 110f;
        public float LaneGapPx = 8f;
        public float JudgeLineY = 0.18f;

        /// <summary>Horizontal shift of the whole overlay, in reference pixels (1080p).</summary>
        public float OffsetXPx = 0f;

        /// <summary>Vertical shift of the judgement line, in reference pixels (1080p). The notes,
        /// boxes and beat lines follow it, since they are all anchored to that line.</summary>
        public float OffsetYPx = 0f;

        /// <summary>Lane background opacity. 0 removes the fill and leaves only the lane edges.</summary>
        public float LaneOpacity = 0.06f;

        /// <summary>Beat lines: a thin line on every beat and a thick one every
        /// <see cref="BeatsPerBar"/> beats, scrolling with the notes.</summary>
        public bool ShowBeatLines = true;

        /// <summary>Beats per bar: 4 for 4/4, 3 for 3/4.</summary>
        public int BeatsPerBar = 4;

        /// <summary>Beat tempo for the lines, or 0 to take the chart's own BPM.</summary>
        public float BeatBpm = 0f;

        /// <summary>Shifts the beat grid by this many beats, to line it up with the music when the
        /// chart's first tile is not on a downbeat.</summary>
        public float BeatPhaseBeats = 0f;

        /// <summary>Opacity of a beat line; bar lines are drawn stronger than this.</summary>
        public float BeatLineAlpha = 0.18f;

        /// <summary>Thin line thickness, in reference pixels (1080p).</summary>
        public float BeatLineThicknessPx = 1f;

        /// <summary>Thick (bar) line thickness, in reference pixels (1080p).</summary>
        public float BarLineThicknessPx = 3f;

        /// <summary>Draw the beat lines across the whole screen instead of only across the lanes.</summary>
        public bool BeatLineFullWidth = true;

        /// <summary>
        /// True when the level's background is light: the overlay's neutral parts (lanes, judgement
        /// line, boxes, labels, beat lines) are then drawn in black instead of white, and note
        /// outlines take the opposite end of that scale so they still separate a note from what is
        /// behind it.
        /// </summary>
        public bool LightBackground = false;

        public float TimeOffsetSeconds = 0f;

        /// <summary>
        /// Largest gap (ms) that still counts as a roll. Below it the lane walks one step per
        /// note; above it the two index lanes alternate. Matches AdoFalling's default of 180.
        /// Only used while <see cref="FingeringMode"/> is the AdoFalling planner.
        /// </summary>
        public float RollWindowMs = 180f;

        /// <summary>
        /// Which fingering to use: 0 = AdoFalling's original planner (what this mod shipped
        /// with), 1 = the hand-split planner ported from ReADOFAIMacro. See
        /// <see cref="HandPlanner"/>.
        /// </summary>
        public int FingeringMode = 0;

        /// <summary>
        /// The hard-resist limit the hand-split planner works against: above this tempo two
        /// fingers are no longer considered enough and notes get rolled instead. This is the
        /// reference implementation's own knob, and its readme suggests 800-1100; lower values
        /// roll more.
        /// </summary>
        public float MaxHardResistBpm = 840f;

        /// <summary>
        /// Roll direction: true starts each hand on its middle finger (K J D F, the "outer"
        /// roll), false on its index finger (J K F D, the "inner" roll).
        /// </summary>
        public bool OuterRoll = true;

        /// <summary>Palette index shared by both hands; see <see cref="Plugin.Palette"/>.</summary>
        public int ColourPreset = 0;

        public bool DebugLog = true;

        private static string PathFor(UnityModManager.ModEntry modEntry)
        {
            return Path.Combine(modEntry.Path, "Settings.json");
        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                string path = PathFor(modEntry);
                if (!File.Exists(path))
                    return new Settings();
                return JsonUtility.FromJson<Settings>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                modEntry.Logger.Log("settings load failed, using defaults: " + ex.Message);
                return new Settings();
            }
        }

        public void Save(UnityModManager.ModEntry modEntry)
        {
            try
            {
                File.WriteAllText(PathFor(modEntry), JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                modEntry.Logger.Log("settings save failed: " + ex.Message);
            }
        }

        public KeyCode Key
        {
            get { return (KeyCode)ToggleKey; }
        }
    }
}
