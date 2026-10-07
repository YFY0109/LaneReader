using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace LaneReader
{
    /// <summary>
    /// A read-only 4K falling-note overlay for A Dance of Fire and Ice.
    ///
    /// It reads the chart the game has already built (see <see cref="ChartReader"/>) and draws
    /// it as four vertical lanes with notes falling to a judgement line. Nothing here touches
    /// input, scoring or judgement: the stock game keeps handling all of that underneath.
    ///
    /// Three engine facts shape the design:
    ///
    ///   * UnityModManager has no automatic settings panel. Every mod that works on this
    ///     install - JALib, BetterCalibration, JipperResourcePack - assigns ModEntry.OnGUI and
    ///     draws its own panel with GUILayout. This mod does the same.
    ///   * ModEntry.OnGUI only fires while the manager's window is open, so it cannot host a
    ///     full-screen overlay. The overlay is drawn from a Harmony postfix on
    ///     AsyncInputManager.OnGUI, which the game calls full-screen every frame.
    ///   * The note timeline uses the game's raw audio clock. Offsetting or smoothing it
    ///     desynchronises the notes from the music, so it is left alone.
    /// </summary>
    public static class Plugin
    {
        internal static UnityModManager.ModEntry.ModLogger Logger;
        internal static Settings Cfg;
        internal static Harmony Harmony;

        private static readonly ChartReader Chart = new ChartReader();
        private static FingeringPlanner.Result _plan;
        private static double _planFingerprint = double.NaN;

        private static Texture2D _white;
        private static Font _originalFont;

        private static bool _hidden;
        private static float _nextLogAt;
        private static bool _guiLogged;

        // ---- strike feedback: purely time driven, the game's judgement is not read ----
        private static int _chartKey = -1;
        /// <summary>
        /// How much the note swells at the line. Small on purpose: a big swell reads as a
        /// different object rather than as the same note being struck.
        /// </summary>
        private const float StrikeGrow = 0.10f;
        private const double StrikeShrink2 = 0.82;

        private static bool _showAppearance = true;
        private static bool _showFingering = true;
        private static bool _showBeat = true;

        /// <summary>Left/right hand colours offered in the panel, so no colour picker is needed.</summary>
        internal static readonly Color[] Palette =
        {
            new Color(0.40f, 0.70f, 1.00f, 1f),   // blue
            new Color(1.00f, 0.62f, 0.25f, 1f),   // orange
            new Color(0.45f, 1.00f, 0.50f, 1f),   // green
            new Color(1.00f, 0.45f, 0.75f, 1f),   // pink
            new Color(0.80f, 0.80f, 0.90f, 1f),   // grey
        };

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            Cfg = Settings.Load(modEntry);

            modEntry.OnGUI = OnGui;
            modEntry.OnSaveGUI = OnSaveGui;
            modEntry.OnUnload = OnUnload;

            try
            {
                Harmony = new Harmony("com.local.lanereader");
                Harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.Log("harmony hooks applied");
            }
            catch (Exception ex)
            {
                Logger.Log("harmony patch failed - overlay will not draw: " + ex);
            }

            Logger.Log("LaneReader ready - reads the live chart, no file needed");
            return true;
        }

        private static void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            if (Cfg != null) Cfg.Save(modEntry);
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Chart.Reset();
            _planFingerprint = double.NaN;
            if (Harmony != null)
            {
                try { Harmony.UnpatchAll("com.local.lanereader"); }
                catch { /* unloading anyway */ }
            }
            return true;
        }

        // ------------------------------------------------------------------ settings panel

        private static void OnGui(UnityModManager.ModEntry modEntry)
        {
            if (Cfg == null)
                return;

            if (!_guiLogged)
            {
                _guiLogged = true;
                Logger.Log("settings panel drawing");
            }

            GUILayout.BeginVertical();

            Toggle("显示叠加层", ref Cfg.Enabled);
            Space();

            Header("显示");
            Toggle("轨道", ref Cfg.ShowLanes);
            Toggle("判定线", ref Cfg.ShowJudgeLine);
            Toggle("音符", ref Cfg.ShowNotes);

            Toggle("键位标签 D F J K", ref Cfg.ShowKeyLabels);
            Toggle("判定框", ref Cfg.ShowJudgeBoxes);
            Toggle("打击弹跳", ref Cfg.ShowHitEffects);
            Toggle("音符边框", ref Cfg.ShowNoteBorder);
            if (Cfg.ShowNoteBorder)
                Slider("边框粗细 (px)", ref Cfg.NoteBorderPx, 1f, 8f, "F0");
            Space();

            Header("布局");
            Slider("下落速度 (px/秒 @1080p)", ref Cfg.NoteSpeedPxPerSec, 50f, 3000f, "F0");
            Slider("音符高度 (px)", ref Cfg.NoteHeightPx, 4f, 120f, "F0");
            Slider("轨道宽度 (px)", ref Cfg.LaneWidthPx, 40f, 400f, "F0");
            Slider("轨道间距 (px)", ref Cfg.LaneGapPx, 0f, 80f, "F0");
            Slider("判定线高度", ref Cfg.JudgeLineY, 0f, 1f, "F2");
            Slider("水平偏移 (px)", ref Cfg.OffsetXPx, -1200f, 1200f, "F0");
            Slider("垂直偏移 (px)", ref Cfg.OffsetYPx, -800f, 800f, "F0");
            Slider("轨道背景透明度", ref Cfg.LaneOpacity, 0f, 0.5f, "F3");
            Space();

            _showBeat = Fold("小节线", _showBeat);
            if (_showBeat)
            {
                Toggle("显示小节线", ref Cfg.ShowBeatLines);
                if (Cfg.ShowBeatLines)
                {
                    IntSlider("每小节拍数 (3=3/4, 4=4/4)", ref Cfg.BeatsPerBar, 2, 8);
                    Slider("小节起点偏移 (拍)", ref Cfg.BeatPhaseBeats, -8f, 8f, "F0");
                    Slider("线透明度", ref Cfg.BeatLineAlpha, 0f, 0.6f, "F3");
                    Slider("细线粗细 (px)", ref Cfg.BeatLineThicknessPx, 1f, 8f, "F0");
                    Slider("粗线粗细 (px)", ref Cfg.BarLineThicknessPx, 1f, 12f, "F0");
                    Toggle("横跨全屏", ref Cfg.BeatLineFullWidth);
                }
            }
            Space();

            Header("对齐");
            Slider("时间偏移 (秒)", ref Cfg.TimeOffsetSeconds, -1f, 1f, "F3");
            Space();

            _showFingering = Fold("排键", _showFingering);
            if (_showFingering)
            {
                GUILayout.Label("轨道顺序: D  F  J  K");
                GUILayout.BeginHorizontal();
                GUILayout.Label("手法", GUILayout.Width(200f));
                if (GUILayout.Button(Cfg.FingeringMode == 1 ? "拆手序" : "AdoFalling 原版",
                        GUILayout.Width(160f)))
                    Cfg.FingeringMode = Cfg.FingeringMode == 1 ? 0 : 1;
                GUILayout.EndHorizontal();

                if (Cfg.FingeringMode == 1)
                {
                    Slider("最大硬抗 BPM", ref Cfg.MaxHardResistBpm, 400f, 1200f, "F0");
                    GUILayout.Label("低于它用两指硬抗，高于它开始轮指（建议 800~1100）");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("轮指方向", GUILayout.Width(200f));
                    if (GUILayout.Button(Cfg.OuterRoll ? "外轮 K J D F" : "内轮 J K F D",
                            GUILayout.Width(160f)))
                        Cfg.OuterRoll = !Cfg.OuterRoll;
                    GUILayout.EndHorizontal();
                }
                else
                {
                    Slider("轮指窗口 (毫秒)", ref Cfg.RollWindowMs, 40f, 400f, "F0");
                    GUILayout.Label("间隔小于窗口 = 轮指；大于 = 食指交替");
                }
            }
            Space();

            _showAppearance = Fold("外观", _showAppearance);
            if (_showAppearance)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("背景明暗", GUILayout.Width(140f));
                if (GUILayout.Button(Cfg.LightBackground ? "亮背景（黑线）" : "暗背景（白线）",
                        GUILayout.Width(160f)))
                    Cfg.LightBackground = !Cfg.LightBackground;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("配色 " + (Cfg.ColourPreset + 1) + "/" + Palette.Length, GUILayout.Width(140f));
                if (GUILayout.Button("下一个", GUILayout.Width(80f)))
                    Cfg.ColourPreset = (Cfg.ColourPreset + 1) % Palette.Length;
                GUILayout.EndHorizontal();
            }
            Space();

            Header("其它");
            if (GUILayout.Button("切换键: " + Cfg.Key, GUILayout.Width(200f)))
                Cfg.ToggleKey = NextKey(Cfg.ToggleKey);
            Toggle("输出诊断日志", ref Cfg.DebugLog);
            Space();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复默认", GUILayout.Width(110f)))
                Cfg = new Settings();
            if (GUILayout.Button("保存", GUILayout.Width(110f)))
                Cfg.Save(modEntry);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void Header(string text)
        {
            GUILayout.Label("<b>" + text + "</b>");
        }

        private static void Space()
        {
            GUILayout.Space(4f);
        }

        private static bool Fold(string text, bool open)
        {
            return GUILayout.Toggle(open, " " + text);
        }

        private static void Toggle(string label, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200f));
            value = GUILayout.Toggle(value, value ? "开" : "关", GUILayout.Width(80f));
            GUILayout.EndHorizontal();
        }

        private static void Slider(string label, ref float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220f));
            GUILayout.Label(value.ToString(format), GUILayout.Width(70f));
            GUILayout.EndHorizontal();
        }

        /// <summary>A slider over whole numbers, for settings that are stored as an int.</summary>
        private static void IntSlider(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200f));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220f)));
            GUILayout.Label(value.ToString(), GUILayout.Width(70f));
            GUILayout.EndHorizontal();
        }

        /// <summary>Cycles the toggle key through a small, sensible set of choices.</summary>
        private static int NextKey(int current)
        {
            int[] keys = { (int)KeyCode.F8, (int)KeyCode.F7, (int)KeyCode.F6, (int)KeyCode.F9,
                           (int)KeyCode.F10, (int)KeyCode.Tab, (int)KeyCode.BackQuote };
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == current)
                    return keys[(i + 1) % keys.Length];
            }
            return keys[0];
        }

        // ------------------------------------------------------------------ per-frame logic

        /// <summary>Called from the Harmony postfix on AsyncInputManager.Update.</summary>
        internal static void Tick()
        {
            if (Cfg == null)
                return;

            if (Input.GetKeyDown(Cfg.Key))
            {
                _hidden = !_hidden;
                Logger.Log("overlay " + (_hidden ? "hidden" : "shown"));
            }

            if (!Cfg.Enabled || _hidden)
                return;

            bool log = Cfg.DebugLog && Time.unscaledTime >= _nextLogAt;
            if (log)
                _nextLogAt = Time.unscaledTime + 2f;

            Chart.Poll(log);

            // The timeline is the game's raw clock - see ChartReader.DrawTime. Nothing here
            // offsets or smooths it, because doing so desynchronised the notes from the music.
            bool frozen = Chart.IsFrozen();

            if (!Chart.Ready)
                return;

            // A rebuilt chart (new level, retry) invalidates the overlay clock, the cleared notes
            // and the hit feedback. Keying this off the generation counter rather than the note
            // count matters: a retry rebuilds an identical chart, and keying off the count left
            // every note cleared and un-replanned.
            if (Chart.Generation != _chartKey)
            {
                _chartKey = Chart.Generation;
                Chart.ResetRenderClock();
            }

            // Advances the overlay clock: freezes it while the run is over or paused, so the
            // notes hold position instead of sliding away.
            Chart.AdvanceRenderClock(frozen);

            // Leaving a death/clear state is a new attempt (revive, retry or restart). The
            // overlay has to come back, and it has to come back re-planned, because a checkpoint
            // revive reuses the same chart objects rather than rebuilding them.
            if (Chart.RunRestarted())
            {
                Chart.RestoreNotes();
                Chart.ResetRenderClock();
                _planFingerprint = double.NaN;
            }

            // Death (or a clear) wipes the overlay. Pausing does not: that should just hold.
            if (Chart.RunEnded())
                Chart.ClearNotes();


            if (log)
            {
                float refScale = Mathf.Max(0.5f, Screen.height / 1080f);
                bool frame = Chart.LevelStanding();
                Logger.Log("frame: " + Chart.FrameDiag(Cfg.NoteSpeedPxPerSec * refScale,
                    Screen.height, frozen, frame, NotesVisible(frame)));
            }

            // Re-plan only when a fingering input actually changed, not every frame.
            double fingerprint = Chart.Generation * 1000000.0 + Cfg.RollWindowMs * 100.0
                                 + Cfg.FingeringMode * 100000000.0 + Cfg.MaxHardResistBpm
                                 + (Cfg.OuterRoll ? 1.0 : 2.0);
            if (fingerprint == _planFingerprint)
                return;

            _planFingerprint = fingerprint;
            if (Cfg.FingeringMode == 1)
                ApplyHandSplit();
            else
                ApplyAdoFalling();
        }

        /// <summary>The planner this mod has always shipped with; see FingeringPlanner.</summary>
        private static void ApplyAdoFalling()
        {
            _plan = FingeringPlanner.Assign(Chart.Notes, Cfg.RollWindowMs);
            Logger.Log(string.Format(
                "fingering: {0} notes -> roll {1}, tap {2}, window {3:F0}ms, lanes D={4} F={5} J={6} K={7}",
                Chart.Count, _plan.Rolls, _plan.Taps, _plan.WindowMs,
                _plan.LaneUse[(int)Finger.LeftMiddle], _plan.LaneUse[(int)Finger.LeftIndex],
                _plan.LaneUse[(int)Finger.RightIndex], _plan.LaneUse[(int)Finger.RightMiddle]));
        }

        /// <summary>
        /// The hand-split planner. It needs the chart's BPM, because its windows are one beat of
        /// that tempo rather than a fixed number of milliseconds; when the game will not hand the
        /// BPM over we say so in the log rather than silently fingering the chart off a guess.
        /// </summary>
        private static void ApplyHandSplit()
        {
            IList<Note> notes = Chart.Notes;
            var times = new List<double>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
                times.Add(notes[i].HitTime);

            string source;
            double bpm = ChartBpm(out source);
            HandPlan plan = HandPlanner.Assign(times, bpm, Cfg.MaxHardResistBpm,
                2, Cfg.OuterRoll);

            int n = Math.Min(notes.Count, plan.Lanes.Length);
            for (int i = 0; i < n; i++)
            {
                Note note = notes[i];
                note.Lane = plan.Lanes[i];
                notes[i] = note;
            }

            Logger.Log(string.Format(
                "hand split: {0} notes, bpm {1:F1} ({2}){3}, {4}, {5}, rolls {6}, taps {7}",
                n, bpm, source, bpm > 0.0 ? "" : " - unusable, plan is a guess", plan.Summary,
                Cfg.OuterRoll ? "outer roll" : "inner roll", plan.Rolls, plan.Taps));
        }

        // ------------------------------------------------------------------ drawing

        private static Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            return _white;
        }

        private static void Fill(Rect r, Color c)
        {
            if (c.a <= 0.002f || r.width <= 0f || r.height <= 0f)
                return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = prev;
        }

        /// <summary>
        /// The overlay has two halves, and they have two different gates.
        ///
        ///   * the static frame - lanes, judgement line, boxes, key labels - is wanted as soon as
        ///     a level is up, including the "press any key to start" prompt a fresh level opens
        ///     with, so it only needs the level to be standing;
        ///   * the notes belong to a run that is actually under way, so they additionally need a
        ///     playing state and a live clock. Drawing them at the prompt scrolled the chart past
        ///     the player before they had pressed anything.
        ///
        /// Together the two gates are what keep the overlay off the menus, the level editor, the
        /// pause menu and the death screen.
        /// </summary>
        private static bool NotesVisible(bool frame)
        {
            return frame && Chart.Playing() && Chart.TimelineLive();
        }

        /// <summary>
        /// Called from the Harmony postfix on AsyncInputManager.OnGUI. The game has already
        /// begun full-screen IMGUI by that point, so plain screen coordinates are correct.
        /// </summary>
        internal static void Draw()
        {
            if (Cfg == null || !Cfg.Enabled || _hidden)
                return;

            // Only while a level is up. See NotesVisible: the frame is drawn on the pre-start
            // prompt, the notes are not.
            bool frame = Chart.LevelStanding();
            bool notes = NotesVisible(frame);
            Chart.NotePlayState(notes);
            if (!Chart.Ready || !frame)
                return;

            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            _originalFont = GUI.skin.font;
            try
            {
                DrawOverlay(notes);
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= _nextLogAt)
                {
                    _nextLogAt = Time.unscaledTime + 5f;
                    Logger.Log("draw failed: " + ex.Message);
                }
            }
            finally
            {
                GUI.skin.font = _originalFont;
            }
        }

        /// <summary>
        /// Draws the overlay. The frame is always drawn; <paramref name="drawNotes"/> is false on
        /// the "press any key to start" prompt, where only the frame is wanted.
        /// </summary>
        private static void DrawOverlay(bool drawNotes)
        {
            float refScale = Mathf.Max(0.5f, Screen.height / 1080f);
            float w = Screen.width;
            float h = Screen.height;

            float laneW = Cfg.LaneWidthPx * refScale;
            float laneGap = Cfg.LaneGapPx * refScale;
            float total = FingeringPlanner.FingerCount * laneW + (FingeringPlanner.FingerCount - 1) * laneGap;
            float x0 = (w - total) * 0.5f + Cfg.OffsetXPx * refScale;

            float judgeY = h * (1f - Mathf.Clamp01(Cfg.JudgeLineY)) + Cfg.OffsetYPx * refScale;
            float speed = Mathf.Max(1f, Cfg.NoteSpeedPxPerSec * refScale);
            float noteH = Mathf.Max(2f, Cfg.NoteHeightPx * refScale);

            Color left = Palette[Cfg.ColourPreset % Palette.Length];
            Color right = Palette[(Cfg.ColourPreset + 1) % Palette.Length];

            // ---- lanes ----------------------------------------------------------
            if (Cfg.ShowLanes)
            {
                Color body = Ink(Cfg.LaneOpacity);
                Color edge = Ink(0.28f);
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    float x = x0 + i * (laneW + laneGap);
                    Fill(new Rect(x, 0f, laneW, judgeY), body);
                    Fill(new Rect(x, 0f, 2f, judgeY), edge);
                    Fill(new Rect(x + laneW - 2f, 0f, 2f, judgeY), edge);
                }
            }

            double now = Chart.RenderTime() + Cfg.TimeOffsetSeconds;

            // ---- beat lines -----------------------------------------------------
            // Drawn right after the lanes so they sit behind the line, the boxes and the notes:
            // they are a reading aid, not a playfield element. They scroll with the clock, so the
            // pre-start prompt (which draws the frame only) leaves them out.
            if (drawNotes && Cfg.ShowBeatLines && Chart.Ready)
                DrawBeatLines(refScale, w, x0, total, judgeY, speed, noteH, now);

            // ---- judgement line -------------------------------------------------
            if (Cfg.ShowJudgeLine)
                Fill(new Rect(0f, judgeY, w, Mathf.Max(2f, 3f * refScale)), Ink(0.85f));

            // ---- key labels -----------------------------------------------------
            // Never change GUI.skin.font here: the panel text has to stay on the game's own font.
            if (Cfg.ShowKeyLabels)
            {
                var label = new GUIStyle(GUI.skin.label);
                label.fontSize = Mathf.RoundToInt(18f * refScale);
                label.alignment = TextAnchor.LowerCenter;
                label.normal.textColor = Ink(0.75f);
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    float x = x0 + i * (laneW + laneGap);
                    GUI.Label(new Rect(x, judgeY + 6f * refScale, laneW, 26f * refScale),
                        FingeringPlanner.KeyName(i), label);
                }
            }

            // ---- judgement boxes -------------------------------------------------
            // One rectangle per lane, same size as a note, sitting so its CENTRE LINE is the
            // judgement line. Notes are positioned against that centre line, which is what makes
            // the strike pop grow symmetrically about the line instead of off one edge.
            if (Cfg.ShowJudgeBoxes)
            {
                float t = Mathf.Max(2f, 2f * refScale);
                Color box = Ink(0.22f);
                float top = judgeY - noteH * 0.5f;
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    float x = x0 + i * (laneW + laneGap);
                    Fill(new Rect(x, top, laneW, t), box);                       // top edge
                    Fill(new Rect(x, top + noteH - t, laneW, t), box);           // bottom edge
                    Fill(new Rect(x, top, t, noteH), box);                       // left edge
                    Fill(new Rect(x + laneW - t, top, t, noteH), box);           // right edge
                }
            }

            // Frame only: the pre-start prompt stops here.
            if (!drawNotes || !Chart.Ready || Chart.Count == 0)
                return;

            int count = Chart.Count;
            IList<Note> notes = Chart.Notes;

            // ---- notes ----------------------------------------------------------
            for (int i = 0; i < count; i++)
            {
                Note note = notes[i];

                // Skip what the game has not generated, and what it has already played.
                if (!Chart.ShouldDraw(i))
                    continue;

                // Bottom edge of the judgement box. Both the falling note and the strike pop are
                // positioned against it, so the note arrives exactly where the pop starts - any
                // other reference makes it look like the note snaps backwards on contact.
                float boxBottom = judgeY + noteH * 0.5f;

                // Falling note: its bottom edge travels down to the box bottom.
                float y = boxBottom - noteH - (float)((note.HitTime - now) * speed);

                if (y + noteH < 0f || y > boxBottom + noteH * 2f)
                    continue;

                float x = x0 + note.Lane * (laneW + laneGap);
                Color c = FingeringPlanner.HandOf(note.Lane) == Hand.Left ? left : right;

                // ---- strike pop ----------------------------------------------------
                // Reaching the line pins the note there. It then swells and shrinks, but its
                // BOTTOM EDGE stays welded to the judgement line throughout, so the note reads as
                // resting on the line rather than floating over it. Purely time driven: the
                // game's judgement is never consulted.
                double age = now - note.HitTime;
                if (age >= 0.0)
                {
                    if (!Cfg.ShowHitEffects || age > ChartReader.StrikePopSeconds)
                        continue;

                    float t = (float)(age / ChartReader.StrikePopSeconds);   // 0..1

                    // Quick swell, then a long contraction that goes well past the original size
                    // so the shrink is unmistakable.
                    float scale;
                    if (t < 0.25f)
                        scale = 1f + StrikeGrow * (t / 0.25f);
                    else
                    {
                        float u = (t - 0.25f) / 0.75f;
                        scale = (1f + StrikeGrow) - (float)((1f + StrikeGrow - StrikeShrink2) * u);
                    }
                    float w2 = laneW * scale;
                    float h2 = noteH * scale;
                    float alpha = 1f - t * t;                                // fade out

                    // Bottom edge welded to the bottom of the judgement box, so the note grows
                    // upward and never drifts off the line as its size changes.
                    FillNote(new Rect(x + (laneW - w2) * 0.5f, boxBottom - h2, w2, h2),
                        new Color(c.r, c.g, c.b, alpha), refScale);
                    continue;
                }

                if (Cfg.ShowNotes)
                    FillNote(new Rect(x + 2f, y, laneW - 4f, noteH), c, refScale);
            }
        }

        /// <summary>
        /// Fills a note, with a darker outline when the outline is enabled. The block is drawn in
        /// the darkened colour and the note's own colour is inset on top of it, so a note keeps
        /// exactly the footprint it has without an outline.
        /// </summary>
        private static void FillNote(Rect r, Color c, float refScale)
        {
            if (!Cfg.ShowNoteBorder)
            {
                Fill(r, c);
                return;
            }

            float b = Mathf.Max(1f, Cfg.NoteBorderPx * refScale);
            if (r.width <= b * 2f || r.height <= b * 2f)
            {
                Fill(r, c);                       // too small to hold an outline
                return;
            }

            // The opposite of the background rather than a darker shade of the note: on a dark
            // level the outline is white, on a light one black, so a note always separates from
            // whatever is behind it and from its neighbours.
            Fill(r, Ink(c.a));
            Fill(new Rect(r.x + b, r.y + b, r.width - 2f * b, r.height - 2f * b), c);
        }

        /// <summary>
        /// The overlay's neutral colour: white ink on a dark level, black ink on a light one. Every
        /// part that is not a note colour goes through here, which is what makes the light/dark
        /// switch a single setting.
        /// </summary>
        private static Color Ink(float alpha)
        {
            return Cfg.LightBackground
                ? new Color(0f, 0f, 0f, alpha)
                : new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>
        /// Beat and bar lines: thin on every beat, thick every BeatsPerBar beats.
        ///
        /// The beat times come from the chart itself (ChartReader works out a tile's length in
        /// beats from the planet's turn), so the lines sit on the chart's own beats and follow it
        /// through accelerations and slowdowns instead of being pinned to one tempo. Each line is
        /// positioned against the same bottom edge the notes arrive at, so it crosses the judgement
        /// line exactly on its beat.
        /// </summary>
        private static void DrawBeatLines(float refScale, float screenW, float x0, float total,
            float judgeY, float speed, float noteH, double now)
        {
            IList<BeatLine> lines = Chart.BeatLines;
            if (lines.Count == 0)
                return;

            int perBar = Mathf.Clamp(Cfg.BeatsPerBar, 2, 16);
            int downbeat = Mathf.RoundToInt(Cfg.BeatPhaseBeats);
            float boxBottom = judgeY + noteH * 0.5f;
            double topTime = now + boxBottom / speed;      // the time at the top of the screen

            float left = Cfg.BeatLineFullWidth ? 0f : x0;
            float width = Cfg.BeatLineFullWidth ? screenW : total;

            for (int index = FirstLineAtOrAfter(lines, now); index < lines.Count; index++)
            {
                BeatLine line = lines[index];
                if (line.Time > topTime)
                    break;

                int beat = line.Beat + downbeat;
                bool bar = ((beat % perBar) + perBar) % perBar == 0;
                float thick = Mathf.Max(1f,
                    (bar ? Cfg.BarLineThicknessPx : Cfg.BeatLineThicknessPx) * refScale);
                float alpha = bar ? Mathf.Min(1f, Cfg.BeatLineAlpha * 1.8f) : Cfg.BeatLineAlpha;

                float y = boxBottom - (float)((line.Time - now) * speed);
                Fill(new Rect(left, y, width, thick), Ink(alpha));
            }
        }

        /// <summary>Binary search for the first beat line at or after <paramref name="time"/>.</summary>
        private static int FirstLineAtOrAfter(IList<BeatLine> lines, double time)
        {
            int lo = 0;
            int hi = lines.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (lines[mid].Time < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// The chart's BPM, with the fallbacks the hand-split planner uses: the game's own value
        /// when it is usable, otherwise a guess from the note spacing. 0 means neither worked, and
        /// <paramref name="source"/> says which one answered.
        /// </summary>
        private static double ChartBpm(out string source)
        {
            double bpm = Chart.LevelBpm();
            source = "game";
            if (bpm < 20.0 || bpm > 1000.0)
            {
                double estimated = Chart.EstimatedBeatSeconds();
                bpm = estimated > 0.0001 ? 60.0 / estimated : 0.0;
                source = "estimated";
            }
            return bpm;
        }

        // ------------------------------------------------------------------ logging

        internal static void Log(string message)
        {
            if (Logger != null)
                Logger.Log(message);
        }
    }
}
