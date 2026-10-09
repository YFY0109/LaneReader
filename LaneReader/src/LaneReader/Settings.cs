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

        // The sprite is drawn exactly as authored: there was a "tint it with the lane colour"
        // switch here, and it was removed on request - a note image already carries the colours
        // its author wanted, and tinting it turned every one of them into the same hue.

        /// <summary>How the sprite is fitted to the note's box: true keeps its aspect ratio
        /// (contained and centred), false stretches it to the box.</summary>
        public bool NoteImageKeepAspect = true;

        /// <summary>
        /// A second sprite, for the hold bar (the body between a hold's head and its tail). Set the
        /// same way as the note's; the bar is then the picture, stretched along it. Off by default,
        /// where the bar is the flat colour block instead.
        /// </summary>
        public bool HoldImageEnabled = false;

        /// <summary>The hold bar's sprite. Relative paths resolve against the mod's own folder.</summary>
        public string HoldImagePath = "hold.png";

        /// <summary>Clockwise rotation of the whole overlay, in degrees. The overlay turns about
        /// the middle of its judgement line, so the pivot does not move with the angle.</summary>
        public float RotationDegrees = 0f;

        /// <summary>
        /// Fill the whole screen with one colour before the overlay draws anything, to hide whatever
        /// the level is doing behind it. Off by default; the colour is black at 50%, which dims a
        /// level without hiding it completely - its alpha is the opacity, so 100% is a solid
        /// backdrop.
        ///
        /// It is drawn in screen space, not inside the overlay's rotation: it is there to cover the
        /// screen, so turning the lanes must not leave corners of the level showing.
        /// </summary>
        public bool BackgroundEnabled = false;

        public Color BackgroundColour = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>Fall speed, in reference pixels per second at 1080p. The default is the speed
        /// this mod has settled on with its player.</summary>
        public float NoteSpeedPxPerSec = 1000f;
        public float NoteHeightPx = 26f;
        public float LaneWidthPx = 110f;
        public float LaneGapPx = 8f;
        public float JudgeLineY = 0.18f;

        /// <summary>
        /// Draw the judgement line across the whole screen (the default) or only as long as the width
        /// percentage says, centred on the playfield. A shorter line reads as part of the overlay rather
        /// than as a strut across the level, which matters when the level's own background is visible.
        /// </summary>
        public bool JudgeLineFullWidth = true;

        /// <summary>Its width as a percentage of the screen, 5..100, when it is not full width.</summary>
        public float JudgeLineWidthPercent = 100f;

        /// <summary>
        /// How far the lanes run from the judgement line towards the top of the screen, in
        /// reference pixels (1080p). This is a number of its own on purpose: the lanes used to be
        /// drawn all the way to the screen edge, which silently re-stretched every lane whenever
        /// the judgement line height, the vertical offset or the rotation changed. 0 keeps that old
        /// behaviour (lanes to the screen edge); any positive value pins the length, so moving the
        /// line moves the lanes with it instead of changing how long they are.
        ///
        /// It is also the top of the visible playfield: notes and beat lines come into being there
        /// rather than at the screen edge. The default is one and a half screens, i.e. longer than the
        /// screen itself - on purpose. The playfield is then bounded by the screen rather than by the
        /// setting (nothing ever pops into existence in view), and under 3D the vanishing point sits
        /// just off the top of the screen, which keeps the taper gentle enough to read.
        /// </summary>
        public float LaneLengthPx = 1620f;

        /// <summary>Horizontal shift of the whole overlay, in reference pixels (1080p).</summary>
        public float OffsetXPx = 0f;

        // There used to be an OffsetYPx here as well. It was dropped: it moved the judgement line
        // by pixels while JudgeLineY moves the same line by a fraction of the screen, so the two
        // controls were the same feature twice. JudgeLineY is the one that stayed, and it is the
        // only vertical control now (the rotation pivot follows it too).

        /// <summary>Lane background opacity, 0..1 (the panel shows all of it: 1 = fully opaque).
        /// 0 removes the fill and leaves only the lane edges.</summary>
        public float LaneOpacity = 0.06f;

        /// <summary>
        /// Draw the frame rate (and the frame time) under the judgement line. Off by default: it is
        /// a diagnostic, not part of the playfield. See <see cref="Plugin.FrameRateText"/>.
        /// </summary>
        public bool ShowFps = false;

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

        // ------------------------------------------------------------------ colours
        //
        // Every element carries its own RGBA colour, so the light/dark switch and the old
        // five-entry palette are both gone: "black lines on a light level" is now just a theme
        // (Themes.cs), and the left/right hand colours are the note colours of lanes 1/2 and 3/4.
        //
        // Alpha: the four "track" elements below are multiplied by their own opacity slider
        // (LaneOpacity for the lane body, BeatLineAlpha for the lines), the rest use the colour's
        // own alpha directly. Where a "Separate" flag is off, the single colour is used for all
        // four lanes; where it is on, the array is used, one entry per lane (D F J K).

        public Color LaneColour = new Color(1f, 1f, 1f, 1f);
        public bool LaneSeparate = false;
        public Color[] LaneColours = new Color[]
        {
            new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f),
        };

        public Color LaneEdgeColour = new Color(1f, 1f, 1f, 0.28f);
        public bool LaneEdgeSeparate = false;
        public Color[] LaneEdgeColours = new Color[]
        {
            new Color(1f, 1f, 1f, 0.28f), new Color(1f, 1f, 1f, 0.28f),
            new Color(1f, 1f, 1f, 0.28f), new Color(1f, 1f, 1f, 0.28f),
        };

        /// <summary>Note colours. Separate is on by default so the left/right hand split survives:
        /// lanes 1/2 take one colour and lanes 3/4 the other.</summary>
        public Color NoteColour = new Color(0.40f, 0.70f, 1.00f, 1f);
        public bool NoteSeparate = true;
        public Color[] NoteColours = new Color[]
        {
            new Color(0.40f, 0.70f, 1.00f, 1f), new Color(0.40f, 0.70f, 1.00f, 1f),
            new Color(1.00f, 0.62f, 0.25f, 1f), new Color(1.00f, 0.62f, 0.25f, 1f),
        };

        public Color NoteEdgeColour = new Color(1f, 1f, 1f, 1f);
        public bool NoteEdgeSeparate = false;
        public Color[] NoteEdgeColours = new Color[]
        {
            new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f),
            new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f),
        };

        public Color JudgeLineColour = new Color(1f, 1f, 1f, 0.85f);
        public Color JudgeBoxColour = new Color(1f, 1f, 1f, 0.22f);
        public Color BeatLineColour = new Color(1f, 1f, 1f, 1f);
        public Color BarLineColour = new Color(1f, 1f, 1f, 1f);
        public Color LabelColour = new Color(1f, 1f, 1f, 0.75f);
        public Color FpsColour = new Color(1f, 1f, 1f, 0.7f);

        /// <summary>
        /// The bar drawn between a hold's head and its release, in the same style as the notes: a
        /// colour (per lane when separate), a width as a fraction of the lane, and its own alpha.
        /// </summary>
        public Color HoldColour = new Color(1f, 1f, 1f, 0.45f);
        public bool HoldSeparate = false;
        public Color[] HoldColours = new Color[]
        {
            new Color(0.40f, 0.70f, 1.00f, 0.45f), new Color(0.40f, 0.70f, 1.00f, 0.45f),
            new Color(1.00f, 0.62f, 0.25f, 0.45f), new Color(1.00f, 0.62f, 0.25f, 0.45f),
        };

        /// <summary>Hold bar width as a fraction of the lane width, 0.05..1.</summary>
        public float HoldWidthFraction = 0.35f;

        /// <summary>Which theme button was pressed last, for the panel caption only.</summary>
        public int Theme = 0;

        // ------------------------------------------------------------------ experimental

        /// <summary>
        /// Experimental 3D lanes: the four lanes converge on a vanishing point that sits
        /// <see cref="LaneLengthPx"/> above the judgement line - i.e. exactly at the lane top, which
        /// is also where notes and beat lines come into being.
        ///
        /// The whole playfield is put through one pinhole camera (see Plugin.View). A point that is
        /// <c>d</c> pixels of fall above the judgement line is drawn
        /// <code>
        ///     rise  = L * d / (d + eye)
        ///     scale = eye / (d + eye)
        /// </code>
        /// above the line and that much smaller, with eye = L / <see cref="ThreeDStrength"/>. So a
        /// note slides along its own lane's edges all the way to the vanishing point, and it is
        /// genuinely slower up there and faster as it arrives - while the hit moment stays exact,
        /// because rise(0) = 0. The judgement line, the boxes and the labels are all at d = 0 and are
        /// untouched.
        /// </summary>
        public bool ThreeDLanes = false;

        /// <summary>
        /// How close the camera is: eye = lane length / strength, i.e. a note has shrunk to half size
        /// and climbed to half the lane once it is (lane length / strength) pixels of fall above the
        /// line. 1.0 is the natural setting - the fall then covers the same screen distance near the
        /// judgement line as it does with 3D off, so notes arrive at the speed the fall-speed slider
        /// promises - and higher values pull the camera in for a more violent taper.
        /// </summary>
        public float ThreeDStrength = 1f;

        // ---- 娱乐（实验性分类里的小分类）----

        /// <summary>Fun: the four lanes wander around on their own, each on its own noise path.</summary>
        public bool FunFly = false;

        /// <summary>How far a lane may wander from where it belongs, in reference pixels.</summary>
        public float FunFlyAmplitudePx = 260f;

        /// <summary>How fast the wander moves.</summary>
        public float FunFlySpeed = 0.35f;

        /// <summary>Fun: the whole overlay keeps rotating about the judgement line, on top of
        /// <see cref="RotationDegrees"/>.</summary>
        public bool FunSpin = false;

        /// <summary>Spin speed in degrees per second.</summary>
        public float FunSpinSpeed = 30f;

        /// <summary>
        /// Fun: the four lanes point in four mutually perpendicular directions instead of all
        /// pointing up. Each lane is drawn centred on the overlay and then rotated a quarter turn
        /// further than the last about the middle of the judgement line, so their bottom centres all
        /// sit on that one point and the four bodies radiate from it. (There used to be a small
        /// square in the middle - the rotations were anchored on its four edge midpoints - and it
        /// was removed on request: the empty box in the centre read as a hole in the overlay.)
        /// </summary>
        public bool FunPetals = false;

        /// <summary>
        /// Experimental: draw a motion-blur trail behind every falling note.
        ///
        /// The note positions are exact (they come straight from the song clock), but they are only
        /// *sampled* once per frame, so at 60 fps and 1000 px/s a note jumps about 17 px between
        /// frames - on a fast chart that reads as stepping rather than as motion. The trail is a few
        /// extra copies of the note, faded, placed where it was on the previous frames (the distance
        /// the clock actually advanced since the last frame, so a paused or frozen clock gets no
        /// trail at all). It cannot add information that was never rendered; what it does is make the
        /// eye read the jump as movement. Off by default.
        /// </summary>
        public bool SmoothFalling = false;

        /// <summary>How many trail copies, 1..10.</summary>
        public int SmoothGhosts = 4;

        /// <summary>Opacity of the nearest trail copy, as a fraction of the note's own alpha.</summary>
        public float SmoothStrength = 0.45f;

        /// <summary>
        /// Hold support: the overlay reads scrFloor.holdLength, draws the hold's body as a bar running
        /// from the head to the next tile, keeps that bar on screen (and lets the judgement line eat it)
        /// after the head is struck, and holds the head's strike feedback up until the tail arrives. The
        /// hand-split planner also feeds each release as an extra key, forced onto the head's lane.
        ///
        /// On by default. A chart without holds is completely unaffected - nothing reads holdLength
        /// except this pass, and it finds nothing to do - so there is no reason to make a hold chart
        /// opt in to being read correctly.
        /// </summary>
        public bool HoldSupport = true;

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

                string json = File.ReadAllText(path);
                Settings settings = JsonUtility.FromJson<Settings>(json);

                // A file written before the colours became settable has none of the colour keys, and
                // JsonUtility leaves absent fields at their type default - which for a Color is
                // transparent black, i.e. an invisible overlay. Those configs get the classic theme
                // instead, which is exactly what they were looking at before the upgrade. Any later
                // save writes the colour keys, so this only ever fires once.
                if (settings != null && json.IndexOf("\"LaneColour\"", StringComparison.Ordinal) < 0)
                {
                    Themes.Apply(settings, 0);
                    modEntry.Logger.Log("settings: pre-colour config upgraded to the classic theme");
                }

                return settings ?? new Settings();
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
