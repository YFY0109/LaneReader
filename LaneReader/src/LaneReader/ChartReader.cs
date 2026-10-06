using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace LaneReader
{
    /// <summary>One note in the falling timeline, already assigned to a lane.</summary>
    public struct Note
    {
        /// <summary>Song-clock time in seconds when this note reaches the judgement line.</summary>
        public double HitTime;

        /// <summary>0..3.</summary>
        public int Lane;

        /// <summary>Chart tile index (scrFloor.seqID).</summary>
        public int SeqId;

        /// <summary>+1 if the chart turns clockwise out of this tile, -1 otherwise.</summary>
        public int Direction;

        /// <summary>The live scrFloor, so the renderer can read the game's own judgement.</summary>
        public object Floor;

        /// <summary>
        /// True when this note came from a midspin tile. A midspin is carried by a pair of tiles
        /// at one instant, so the duplicate is dropped - and only midspins are deduplicated,
        /// because two genuinely simultaneous taps elsewhere are a different case.
        /// </summary>
        public bool MidSpin;
    }

    /// <summary>The game's own judgement value for a tile (scrFloor.grade / HitMargin).</summary>
    public enum Grade
    {
        None = -1,
        TooEarly = 0,
        VeryEarly = 1,
        EarlyPerfect = 2,
        Perfect = 3,
        LatePerfect = 4,
        VeryLate = 5,
        TooLate = 6,
        Multipress = 7,
        FailMiss = 8,
        FailOverload = 9,
        Auto = 10,
        OverPress = 11,
    }

    /// <summary>
    /// Reads the chart the game has already built in memory.
    ///
    /// The previous attempt at this failed because it read
    /// <c>scrLevelMaker.instance</c> before the level existed and then never retried
    /// meaningfully. This version treats a null singleton as "not ready yet" and simply
    /// keeps polling, then rebuilds whenever the floor list identity changes.
    /// </summary>
    public sealed class ChartReader
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static;

        // Resolved reflection handles.
        private PropertyInfo _lmInstance;
        private FieldInfo _lmListFloors;
        private FieldInfo _lmBpm;
        private FieldInfo _floorSeqId;
        private FieldInfo _floorEntryTime;
        private FieldInfo _floorEntryAngle;
        private FieldInfo _floorExitAngle;
        private FieldInfo _floorSpeed;
        private FieldInfo _floorGrade;
        private FieldInfo _floorMidSpin;
        private FieldInfo _floorIsFake;
        private PropertyInfo _conductorInstance;
        private PropertyInfo _conductorSongPosProp;
        private FieldInfo _conductorSongPosField;
        private FieldInfo _conductorDspTimeSong;
        private FieldInfo _conductorHasSongStarted;

        private PropertyInfo _ctrlInstance;
        private PropertyInfo _ctrlPaused;
        private FieldInfo _ctrlCurrentState;
        private FieldInfo _ctrlCurrentSeqId;

        // Level editor: playMode distinguishes testing from building.
        private FieldInfo _editorInstance;
        private PropertyInfo _editorPlayMode;
        private PropertyInfo _editorPausedInPlay;

        /// <summary>
        /// States in which the run is over and the overlay must stop moving. Without this the
        /// notes keep falling while the death screen is up, because the audio clock runs on.
        /// </summary>
        private static readonly string[] FrozenStates = { "Fail", "Fail2", "Won" };

        /// <summary>States before the music starts. Notes must not be drawn here.</summary>
        private static readonly string[] PreSongStates = { "None", "Start", "Checkpoint", "Countdown" };

        /// <summary>
        /// States in which a level is loaded and the overlay's static frame belongs on screen.
        ///
        /// "Countdown" covers both the 3-2-1 before a checkpoint resume and the "press any key
        /// to start" prompt a fresh level opens with. The frame is wanted on both; the notes are
        /// not, and they are gated separately - see <see cref="TimelineLive"/>.
        /// </summary>
        private static readonly string[] StandingStates = { "Start", "Countdown", "PlayerControl", "Checkpoint" };

        private bool _reflectionReady;
        private bool _reflectionFailed;
        private string _diagnostic = "not initialised";

        // Current chart.
        private readonly List<Note> _notes = new List<Note>();
        private object _lastFloorList;
        private int _lastFloorCount = -1;
        private double _firstHitTime;
        private double _lastHitTime;

        // ---- render clock -----------------------------------------------------------
        // Kept separate from the raw audio clock so pausing/death can freeze the overlay
        // without touching the value the notes are positioned against during normal play.
        private readonly HashSet<int> _cleared = new HashSet<int>();

        /// <summary>
        /// Bumped every time the note list is rebuilt. Callers use it to tell "same chart, same
        /// run" from "the chart was rebuilt", which is the only reliable way to know a restart
        /// happened: the note count and timings can be identical across a retry.
        /// </summary>
        private int _generation;
        private double _frozenAt = double.NaN;
        private double _previousRaw = double.NaN;

        /// <summary>
        /// True once the game's clock has been seen to move since this chart was set up. A clock
        /// that never moves is not a timeline the notes can be positioned against - see
        /// <see cref="TimelineLive"/>.
        /// </summary>
        private bool _clockMoved;

        private double _displayTime;
        private bool _displayTimeValid;

        public IList<Note> Notes { get { return _notes; } }
        public int Count { get { return _notes.Count; } }
        public double FirstHitTime { get { return _firstHitTime; } }
        public double LastHitTime { get { return _lastHitTime; } }
        public string Diagnostic { get { return _diagnostic; } }
        public bool Ready { get { return _notes.Count > 0; } }

        /// <summary>Song clock in seconds, read straight from the game's own conductor.</summary>
        public double SongPosition()
        {
            object conductor = ReadStatic(_conductorInstance);
            if (conductor == null)
                return 0.0;

            // Prefer the public property; fall back to the backing field. The field is named
            // _songposition_minusi with a leading underscore, so an exact-name lookup of
            // "songposition_minusi" silently finds nothing and would freeze the timeline at 0.
            double v = ReadDouble(conductor, _conductorSongPosProp);
            if (double.IsNaN(v))
                v = ReadDouble(conductor, _conductorSongPosField);
            if (double.IsNaN(v))
                v = ReadDouble(conductor, _conductorDspTimeSong);
            return double.IsNaN(v) ? 0.0 : v;
        }

        private static double ReadDouble(object target, MemberInfo member)
        {
            if (target == null || member == null)
                return double.NaN;
            try
            {
                var p = member as PropertyInfo;
                if (p != null) return ToDouble(p.GetValue(target, null));
                var f = member as FieldInfo;
                if (f != null) return ToDouble(f.GetValue(target));
            }
            catch
            {
                // fall through to the next source
            }
            return double.NaN;
        }

        public void Reset()
        {
            _notes.Clear();
            _lastFloorList = null;
            _lastFloorCount = -1;
            _firstHitTime = 0.0;
            _lastHitTime = 0.0;
            _clockMoved = false;
        }

        // ------------------------------------------------------------ reflection setup

        private void EnsureReflection()
        {
            if (_reflectionReady || _reflectionFailed)
                return;

            try
            {
                Assembly asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "Assembly-CSharp") { asm = a; break; }
                }
                if (asm == null)
                {
                    _diagnostic = "Assembly-CSharp not loaded";
                    return;
                }

                Type lm = asm.GetType("scrLevelMaker", false);
                Type floor = asm.GetType("scrFloor", false);
                Type cond = asm.GetType("scrConductor", false);
                Type ctrl = asm.GetType("scrController", false);
                if (lm == null || floor == null || cond == null || ctrl == null)
                {
                    _diagnostic = string.Format(
                        "missing types: levelMaker={0} floor={1} conductor={2} controller={3}",
                        lm != null, floor != null, cond != null, ctrl != null);
                    _reflectionFailed = true;
                    return;
                }

                _lmInstance = lm.GetProperty("instance", Any);
                _lmListFloors = lm.GetField("listFloors", Any);
                _lmBpm = lm.GetField("bpm_forUnityUseOnly", Any);

                _floorSeqId = floor.GetField("seqID", Any);
                _floorEntryTime = floor.GetField("entryTime", Any);
                _floorEntryAngle = floor.GetField("entryangle", Any);
                _floorExitAngle = floor.GetField("exitangle", Any);
                _floorSpeed = floor.GetField("speed", Any);
                // The game's own judgement for each tile. Reading this is how the overlay knows
                // a note has been struck, how well, and whether it was missed.
                _floorGrade = floor.GetField("grade", Any);
                // Midspins and fake tiles are not separate presses: the planet passes through
                // them without the player tapping. Drawing them as notes produced phantom hits
                // in the middle of every stream.
                _floorMidSpin = floor.GetField("midSpin", Any);
                _floorIsFake = floor.GetField("isFake", Any);

                _conductorInstance = cond.GetProperty("instance", Any);
                _conductorSongPosProp = cond.GetProperty("songposition_minusi", Any);
                _conductorSongPosField = cond.GetField("_songposition_minusi", Any);
                _conductorDspTimeSong = cond.GetField("dspTimeSong", Any);
                _conductorHasSongStarted = cond.GetField("hasSongStarted", Any);

                _ctrlInstance = ctrl.GetProperty("instance", Any);
                _ctrlPaused = ctrl.GetProperty("paused", Any);
                _ctrlCurrentState = ctrl.GetField("currentState", Any);
                _ctrlCurrentSeqId = ctrl.GetField("currentSeqID", Any);

                Type editor = asm.GetType("scnEditor", false);
                if (editor != null)
                {
                    _editorInstance = editor.GetField("instance", Any);
                    _editorPlayMode = editor.GetProperty("playMode", Any);
                    _editorPausedInPlay = editor.GetProperty("pausedInPlayMode", Any);
                }

                if (_lmInstance == null || _lmListFloors == null)
                {
                    _diagnostic = "core members missing (instance / listFloors)";
                    _reflectionFailed = true;
                    return;
                }
                if (_ctrlPaused == null)
                    _diagnostic = "warning: scrController.paused not found; pause gating disabled";

                _reflectionReady = true;
                _diagnostic = "reflection ready";
            }
            catch (Exception ex)
            {
                _diagnostic = "reflection error: " + ex.Message;
                _reflectionFailed = true;
            }
        }

        // ------------------------------------------------------------ polling

        /// <summary>
        /// Called every frame. Rebuilds the note list when a new chart appears or the
        /// floor count changes (restart, new level, editor edits).
        /// </summary>
        public void Poll(bool log)
        {
            EnsureReflection();
            if (!_reflectionReady)
                return;

            object lm = ReadStatic(_lmInstance);
            if (lm == null)
            {
                // Level not built yet. Drop a stale chart so we never draw the previous song.
                if (_notes.Count > 0)
                {
                    Reset();
                    if (log) Plugin.Log("chart cleared (no level)");
                }
                _diagnostic = "waiting: scrLevelMaker.instance is null";
                return;
            }

            object raw = _lmListFloors.GetValue(lm);
            var floors = raw as IList;
            if (floors == null || floors.Count == 0)
            {
                if (_notes.Count > 0)
                {
                    Reset();
                    if (log) Plugin.Log("chart cleared (empty floor list)");
                }
                _diagnostic = "waiting: listFloors empty or not a list";
                return;
            }

            // Rebuild only when the chart actually changed.
            if (ReferenceEquals(raw, _lastFloorList) && floors.Count == _lastFloorCount)
                return;

            _lastFloorList = raw;
            _lastFloorCount = floors.Count;
            Build(floors, log);
        }

        // ------------------------------------------------------------ run state

        /// <summary>Reads scrController.currentState as a string, or "" when unavailable.</summary>
        private string CurrentState()
        {
            object ctrl = ReadStatic(_ctrlInstance);
            if (ctrl == null || _ctrlCurrentState == null)
                return "";
            try
            {
                object v = _ctrlCurrentState.GetValue(ctrl);
                return v == null ? "" : v.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// True while the run is over (death or clear) or the player paused. The overlay must
        /// hold still in these states: the audio clock keeps running behind the death screen,
        /// so without this every note slides off the bottom of the screen while you are dead.
        /// </summary>
        public bool IsFrozen()
        {
            object ctrl = ReadStatic(_ctrlInstance);
            if (ctrl != null && _ctrlPaused != null)
            {
                try
                {
                    object v = _ctrlPaused.GetValue(ctrl, null);
                    if (v is bool && (bool)v)
                        return true;
                }
                catch { /* fall through to the state check */ }
            }

            string state = CurrentState();
            for (int i = 0; i < FrozenStates.Length; i++)
            {
                if (state == FrozenStates[i])
                    return true;
            }
            return false;
        }

        /// <summary>True once the music has actually begun; false through the countdown.</summary>
        public bool SongRunning()
        {
            object cond = ReadStatic(_conductorInstance);
            if (cond == null)
                return false;
            if (_conductorHasSongStarted != null)
            {
                try
                {
                    object v = _conductorHasSongStarted.GetValue(cond);
                    if (v is bool)
                        return (bool)v;
                }
                catch { /* fall back to the state check */ }
            }
            string state = CurrentState();
            for (int i = 0; i < PreSongStates.Length; i++)
            {
                if (state == PreSongStates[i])
                    return false;
            }
            return state.Length > 0;
        }

        /// <summary>
        /// One-line snapshot of the live timeline, for the periodic diagnostic log.
        ///
        /// <paramref name="pxPerSecond"/> is the on-screen fall speed, so the visible count is
        /// computed the same way the renderer culls: a note is on screen while its travel time
        /// from the line is under the time it takes to cross the whole screen.
        ///
        /// <paramref name="frame"/> and <paramref name="notes"/> are the two render gates the
        /// caller used this frame, echoed here so a log always says what the screen showed.
        /// </summary>
        public string FrameDiag(float pxPerSecond, float screenHeight, bool frozen,
            bool frame, bool notes)
        {
            double now = DrawTime();
            double win = pxPerSecond > 1f ? screenHeight / pxPerSecond : 3.0;

            int onScreen = 0;
            int drawable = 0;
            int nextIndex = -1;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (ShouldDraw(i))
                    drawable++;

                double d = _notes[i].HitTime - now;
                if (d >= -0.25 && d <= win) onScreen++;
                if (nextIndex < 0 && d >= 0.0) nextIndex = i;
            }

            string next = nextIndex >= 0
                ? string.Format("#{0} in {1:F3}s", nextIndex, _notes[nextIndex].HitTime - now)
                : "none ahead";

            return string.Format(
                "t={0:F3}s raw={1:F3}s win={2:F3}s onscreen={3} {4} frozen={5} | " +
                "seq={6} drawable={7}/{8} | state={9} song={10} live={11} frame={12} notes={13}",
                now, SongPosition(), win, onScreen, next, frozen,
                CurrentSeqId(), drawable, _notes.Count,
                CurrentState(), SongRunning() ? 1 : 0, TimelineLive() ? 1 : 0,
                frame ? 1 : 0, notes ? 1 : 0);
        }

        /// <summary>
        /// The clock the notes are positioned against: the game's own audio clock, raw.
        ///
        /// An earlier revision anchored and smoothed this (to lift the opening note and to ease
        /// to a stop on death). Both were wrong: the raw clock already matches the chart's entry
        /// times, so any offset shifts the whole timeline off the music. The raw read is the
        /// correct behaviour, and is what this returns again.
        /// </summary>
        public double DrawTime()
        {
            return SongPosition();
        }

        /// <summary>The clock the overlay last rendered against.</summary>
        public double RenderTime()
        {
            return _displayTimeValid ? _displayTime : SongPosition();
        }

        /// <summary>
        /// How long a note keeps existing after it reaches the line, so the strike pop can play.
        /// The caller draws that window differently - pinned to the line, not falling.
        /// </summary>
        public const double StrikePopSeconds = 0.30;

        /// <summary>
        /// Whether the overlay should draw this note at all.
        ///
        /// Time driven throughout: the game's judgement is never consulted, so a note behaves
        /// identically whether it was struck or missed.
        /// </summary>
        public bool ShouldDraw(int index)
        {
            if (index < 0 || index >= _notes.Count)
                return false;
            if (_cleared.Contains(index))
                return false;

            // The very first note is never drawn. One missing note at the start of a chart costs
            // nothing, and it avoids a boundary case that kept showing it a frame early.
            if (index == 0)
                return false;

            // Where this run began, inclusive. The starting tile is skipped as well: it is under
            // the planet the moment play begins, so drawing it only ever puts a note on the line
            // with no time to read it.
            if (_runStartSeqId > 0 && _notes[index].SeqId <= _runStartSeqId)
                return false;

            // Past the line the note survives only long enough to play its strike pop.
            if (_notes[index].HitTime <= RenderTime() - StrikePopSeconds)
                return false;

            return true;
        }

        /// <summary>
        /// seqID this run started from, or -1 before the run has begun. Latched, never advanced:
        /// using the game's moving position here made the starting tile appear for one frame and
        /// then vanish, which read as "one extra note at the beginning".
        /// </summary>
        private int _runStartSeqId = -1;

        /// <summary>
        /// Called once per frame by the overlay. Latches the run's starting tile when play
        /// begins, and releases it whenever the overlay is not drawing.
        /// </summary>
        public void NotePlayState(bool playing)
        {
            if (!playing)
            {
                _runStartSeqId = -1;
                return;
            }

            if (_runStartSeqId >= 0)
                return;

            int current = CurrentSeqId();
            if (current >= 0)
                _runStartSeqId = current;
        }


        /// <summary>
        /// True whenever a level is being played - the countdown included, and the editor's
        /// built-in play mode.
        ///
        /// The three cases that must NOT show the overlay, and what excludes them:
        ///
        ///   * "press any key to start" - the state is already Countdown there, but the song has
        ///     not begun, so <see cref="SongRunning"/> is what excludes it;
        ///   * death and the results screen - Fail, Fail2 and Won;
        ///   * menus, the editor's EDIT mode, and any pause.
        /// </summary>
        public bool Playing()
        {
            object ctrl = ReadStatic(_ctrlInstance);
            if (ctrl != null && _ctrlPaused != null)
            {
                try
                {
                    object v = _ctrlPaused.GetValue(ctrl, null);
                    if (v is bool && (bool)v)
                        return false;
                }
                catch { /* an unreadable pause flag is treated as not paused */ }
            }

            // The editor runs its own play mode, which does not always drive the same state
            // machine as a normal level.
            if (EditorPlaying())
                return true;

            string state = CurrentState();
            if (state == "Fail" || state == "Fail2" || state == "Won")
                return false;

            if (state == "PlayerControl" || state == "Checkpoint")
                return true;

            // Countdown covers both "press any key to start" and the 3-2-1; only the latter has
            // the song clock running.
            return state == "Countdown" && SongRunning();
        }

        /// <summary>
        /// True while a level is loaded and its static frame belongs on screen: the lanes, the
        /// judgement line and the boxes. This is the overlay's outer gate, and it deliberately
        /// includes the "press any key to start" prompt, where the chart is already built but
        /// the run has not begun.
        ///
        /// False in the menus, in the editor outside play mode, and while paused, dead or
        /// finished - the states in which the overlay must disappear entirely.
        /// </summary>
        public bool LevelStanding()
        {
            if (IsFrozen())
                return false;
            if (EditorPlaying())
                return true;

            string state = CurrentState();
            for (int i = 0; i < StandingStates.Length; i++)
            {
                if (state == StandingStates[i])
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True once this attempt's note timeline is live: the game's clock has moved on from
        /// where it stood when the chart was set up, and has reached the chart's own zero.
        ///
        /// This is what keeps the notes off the "press any key to start" prompt. The prompt
        /// leaves <c>scrConductor.songposition_minusi</c> holding the previous attempt's
        /// position - or pre-rolling towards zero - while scrController already reports a state
        /// the notes are allowed in, so the clock is the dependable signal here. The conductor's
        /// hasSongStarted flag does not separate the two: the notes were still being drawn at
        /// the prompt, which means that flag already reads true by then.
        /// </summary>
        public bool TimelineLive()
        {
            return _clockMoved && SongPosition() >= 0.0;
        }

        /// <summary>
        /// True while the level editor is in its built-in play mode. scnEditor is a singleton,
        /// and playMode is false whenever the editor is being used to build rather than test.
        /// </summary>
        private bool EditorPlaying()
        {
            object editor = ReadStatic(_editorInstance);
            if (editor == null)
                return false;

            try
            {
                if (_editorPausedInPlay != null)
                {
                    object p = _editorPausedInPlay.GetValue(editor, null);
                    if (p is bool && (bool)p)
                        return false;
                }
                if (_editorPlayMode != null)
                {
                    object m = _editorPlayMode.GetValue(editor, null);
                    return m is bool && (bool)m;
                }
            }
            catch { /* fall through to "not playing" */ }
            return false;
        }

        /// <summary>
        /// True once the game has entered a death or clear state. Pausing is deliberately not
        /// included: pausing should hold the picture, not wipe it.
        /// </summary>
        public bool RunEnded()
        {
            string state = CurrentState();
            return state == "Fail" || state == "Fail2" || state == "Won";
        }

        /// <summary>Raw state name, for diagnostics.</summary>
        public string StateName { get { return CurrentState(); } }

        /// <summary>
        /// True on the frame the run left a death/clear state, i.e. the player revived, retried
        /// or restarted.
        ///
        /// This is the only dependable "new attempt" signal. Watching for a chart rebuild does
        /// not work: a checkpoint revive reuses the same floor objects, so the note list is never
        /// rebuilt and everything the death cleared stays cleared.
        /// </summary>
        public bool RunRestarted()
        {
            string state = CurrentState();
            if (state.Length == 0)
                return false;

            bool ended = state == "Fail" || state == "Fail2" || state == "Won";
            bool restarted = _sawRunEnd && !ended;
            _sawRunEnd = ended;
            return restarted;
        }

        private bool _sawRunEnd;

        /// <summary>Increments whenever the note list is rebuilt (a restart or a new level).</summary>
        public int Generation { get { return _generation; } }

        /// <summary>
        /// Drops every note from the display. Used when the run ends: the chart objects all still
        /// exist, so the only reliable way to stop showing notes on the death screen is to stop
        /// drawing them.
        ///
        /// The cleared set is reset when the chart is rebuilt, which is what a retry does.
        /// </summary>
        public void ClearNotes()
        {
            if (_cleared.Count == _notes.Count)
                return;
            _cleared.Clear();
            for (int i = 0; i < _notes.Count; i++)
                _cleared.Add(i);
        }

        /// <summary>
        /// Brings the notes back after a death. Deliberately does not touch the generation: the
        /// chart itself has not changed, so the finger plan is still valid.
        /// </summary>
        public void RestoreNotes()
        {
            _cleared.Clear();
        }

        /// <summary>True while the overlay clock is held still.</summary>
        public bool ClockFrozen { get { return !double.IsNaN(_frozenAt); } }

        /// <summary>
        /// Resets the render clock. Called when the chart itself changes.
        /// </summary>
        public void ResetRenderClock()
        {
            _cleared.Clear();
            _frozenAt = double.NaN;
            _previousRaw = double.NaN;
            _clockMoved = false;
            _runStartSeqId = -1;
        }

        /// <summary>
        /// The clock the overlay renders against.
        ///
        /// While the run is over or paused the clock stops, so the notes hold position instead
        /// of sliding off the bottom.
        /// </summary>
        public double AdvanceRenderClock(bool frozen)
        {
            double raw = SongPosition();

            // Resuming: restart the clock from wherever the game is now, so a long pause or a
            // death does not cause a jump when play continues.
            if (!frozen && !double.IsNaN(_frozenAt))
            {
                _frozenAt = double.NaN;
                _displayTimeValid = false;
            }

            if (frozen && double.IsNaN(_frozenAt))
                _frozenAt = raw;

            if (double.IsNaN(_frozenAt))
            {
                _displayTime = raw;
                _displayTimeValid = true;
            }

            // Liveness, for TimelineLive: a clock that is held - paused, dead, or sitting on the
            // level's opening prompt with the previous attempt's position - is not a timeline,
            // and neither is one still pre-rolling towards the chart's zero.
            if (frozen || raw < 0.0)
                _clockMoved = false;
            else if (!double.IsNaN(_previousRaw) && Math.Abs(raw - _previousRaw) > 1e-6)
                _clockMoved = true;

            _previousRaw = raw;
            return _displayTimeValid ? _displayTime : raw;
        }

        private void Build(IList floors, bool log)
        {
            _notes.Clear();
            _firstHitTime = 0.0;
            _lastHitTime = 0.0;
            _cleared.Clear();
            _generation++;

            // The render clock is indexed by note position, so it is meaningless for a chart
            // that has just been replaced. Leaving it stale made every note in a shorter chart
            // fall behind the cursor and vanish.
            ResetRenderClock();

            int n = floors.Count;
            var entryTimes = new double[n];
            var seqIds = new int[n];

            for (int i = 0; i < n; i++)
            {
                object f = floors[i];
                if (f == null) continue;
                entryTimes[i] = _floorEntryTime != null ? ToDouble(_floorEntryTime.GetValue(f)) : 0.0;
                seqIds[i] = _floorSeqId != null ? (int)ToDouble(_floorSeqId.GetValue(f)) : i;
            }

            for (int i = 0; i < n; i++)
            {
                object f = floors[i];
                if (f == null) continue;

                var note = new Note();
                note.HitTime = entryTimes[i];
                note.SeqId = seqIds[i];
                note.Floor = f;
                note.MidSpin = IsMidSpin(f);

                // Direction from the entry angle: 0 = clockwise quarter turn in ADOFAI's
                // encoding, which is what scrLevelMaker stores.
                double entryAngle = _floorEntryAngle != null ? ToDouble(_floorEntryAngle.GetValue(f)) : 0.0;
                note.Direction = entryAngle == 0.0 ? 1 : 0;

                _notes.Add(note);
            }

            // A midspin is carried by a pair of tiles at the same instant, so it was drawn twice.
            // Only midspin pairs are collapsed: two simultaneous taps elsewhere are a different
            // case and are left alone.
            int deduped = 0;
            for (int i = _notes.Count - 1; i > 0; i--)
            {
                if (!_notes[i].MidSpin && !_notes[i - 1].MidSpin)
                    continue;
                if (Math.Abs(_notes[i].HitTime - _notes[i - 1].HitTime) >= 1e-4)
                    continue;

                _notes.RemoveAt(i);
                deduped++;
            }
            if (deduped > 0)
                Plugin.Log("merged " + deduped + " duplicate midspin notes");

            if (_notes.Count > 0)
            {
                _firstHitTime = _notes[0].HitTime;
                _lastHitTime = _notes[_notes.Count - 1].HitTime;
            }

            _diagnostic = string.Format("chart read: {0} notes, {1:F3}s..{2:F3}s",
                _notes.Count, _firstHitTime, _lastHitTime);

            // Prove what the game actually handed us. A chart whose entry times are all zero
            // (or not increasing) cannot possibly produce a moving timeline, and guessing at
            // that from the outside wasted several rounds.
            int zeroTimes = 0;
            int backwards = 0;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (Math.Abs(_notes[i].HitTime) < 1e-9) zeroTimes++;
                if (i > 0 && _notes[i].HitTime < _notes[i - 1].HitTime) backwards++;
            }

            Plugin.Log(string.Format(
                "chart: {0} notes, first {1:F3}s, last {2:F3}s, zero-time {3}, non-monotonic {4}",
                _notes.Count, _firstHitTime, _lastHitTime, zeroTimes, backwards));
            Plugin.Log("chart seqIds: " + _notes[0].SeqId + ".." +
                       _notes[_notes.Count - 1].SeqId);

            if (log)
                Plugin.Log(_diagnostic);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// The game's own position in the chart (scrController.currentSeqID). This is what tells
        /// us which tiles have been played, and it is the only reliable signal: scrFloor.grade
        /// initialises to TooEarly (0), not to a "not judged" sentinel, so a grade-based test
        /// treats every note in the chart as already hit and erases the whole overlay.
        /// </summary>
        public int CurrentSeqId()
        {
            object ctrl = ReadStatic(_ctrlInstance);
            if (ctrl == null || _ctrlCurrentSeqId == null)
                return -1;
            try
            {
                return Convert.ToInt32(_ctrlCurrentSeqId.GetValue(ctrl));
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// The game's judgement for one note, or <see cref="Grade.None"/> when the tile has not
        /// been judged yet. The game resets a tile's grade on rewind and restart, so this
        /// self-heals without the overlay tracking any state of its own.
        /// </summary>
        public Grade GradeOf(int index)
        {
            if (_floorGrade == null || index < 0 || index >= _notes.Count)
                return Grade.None;

            object floor = _notes[index].Floor;
            if (floor == null)
                return Grade.None;

            try
            {
                return (Grade)Convert.ToInt32(_floorGrade.GetValue(floor));
            }
            catch
            {
                return Grade.None;
            }
        }

        /// <summary>True when the game considered this note struck or missed (i.e. it is done).</summary>
        public static bool IsConsumed(Grade g)
        {
            return g != Grade.None;
        }

        private static object ReadStatic(PropertyInfo p)
        {
            if (p == null) return null;
            try { return p.GetValue(null, null); }
            catch { return null; }
        }

        private static object ReadStatic(FieldInfo f)
        {
            if (f == null) return null;
            try { return f.GetValue(null); }
            catch { return null; }
        }

        private bool IsMidSpin(object floor)
        {
            return ReadFlag(floor, _floorMidSpin);
        }

        private bool IsFake(object floor)
        {
            return ReadFlag(floor, _floorIsFake);
        }

        private static bool ReadFlag(object floor, FieldInfo field)
        {
            if (floor == null || field == null)
                return false;
            try
            {
                object v = field.GetValue(floor);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }

        private static double ToDouble(object v)        {
            if (v == null) return 0.0;
            if (v is double) return (double)v;
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            try { return Convert.ToDouble(v); }
            catch { return 0.0; }
        }
    }
}
