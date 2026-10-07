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

        /// <summary>Lane flash and judgement text when the game registers a hit.</summary>
        public bool ShowHitEffects = true;

        public float NoteSpeedPxPerSec = 620f;
        public float NoteHeightPx = 26f;
        public float LaneWidthPx = 110f;
        public float LaneGapPx = 8f;
        public float JudgeLineY = 0.18f;

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
