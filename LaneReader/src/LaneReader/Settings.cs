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

        /// <summary>Draw a png/jpg from disk as the falling note instead of the flat block.
        /// See <see cref="NoteImage"/>.</summary>
        public bool NoteImageEnabled = false;

        /// <summary>The note sprite. Relative paths resolve against the mod's own folder, so
        /// "note.png" means Mods/LaneReader/note.png.</summary>
        public string NoteImagePath = "note.png";

        /// <summary>Paint the sprite in the hand's colour instead of its own. Off by default:
        /// an image is usually already the colour its author wanted.</summary>
        public bool NoteImageTint = false;

        /// <summary>How the sprite is fitted to the note's box: true keeps its aspect ratio
        /// (contained and centred), false stretches it to the box.</summary>
        public bool NoteImageKeepAspect = true;

        /// <summary>Clockwise rotation of the whole overlay, in degrees. The overlay turns about
        /// the middle of its judgement line, so the pivot does not move with the angle.</summary>
        public float RotationDegrees = 0f;

        /// <summary>Fall speed, in reference pixels per second at 1080p. The default is the speed
        /// this mod has settled on with its player.</summary>
        public float NoteSpeedPxPerSec = 1000f;
        public float NoteHeightPx = 26f;
        public float LaneWidthPx = 110f;
        public float LaneGapPx = 8f;
        public float JudgeLineY = 0.18f;

        /// <summary>
        /// How far the lanes run from the judgement line towards the top of the screen, in
        /// reference pixels (1080p). This is a number of its own on purpose: the lanes used to be
        /// drawn all the way to the screen edge, which silently re-stretched every lane whenever
        /// the judgement line height, the vertical offset or the rotation changed. 0 keeps that old
        /// behaviour (lanes to the screen edge); any positive value pins the length, so moving the
        /// line moves the lanes with it instead of changing how long they are.
        /// </summary>
        public float LaneLengthPx = 900f;

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

        /// <summary>Shifts which beat counts as a downbeat, in whole beats. The grid itself is
        /// anchored to the notes; this only moves the thick lines.</summary>
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
        /// <see cref="HandPlanner"/>. The hand-split planner is the default: it is the one that
        /// was tuned against real play.
        /// </summary>
        public int FingeringMode = 1;

        /// <summary>
        /// The hard-resist limit the hand-split planner works against: passing it halves the
        /// window, which is how the reference implementation rolls more the lower it is set.
        ///
        /// 850 is what the player asked this default to be. Note what it does to the base BPM when
        /// the level BPM is unreadable (the normal case on this install): the planner's own fallback
        /// normalises to 800, and 800 is NOT above 850, so nothing is halved and the base stays 800 -
        /// half the base the mod shipped with (506 halved it to 400), which halves every window's
        /// press count and therefore rolls far less. See HANDOFF §14.
        /// </summary>
        public float MaxHardResistBpm = 850f;

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
