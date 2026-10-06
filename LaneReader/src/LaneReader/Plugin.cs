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
            Space();

            Header("布局");
            Slider("下落速度 (px/秒 @1080p)", ref Cfg.NoteSpeedPxPerSec, 50f, 3000f, "F0");
            Slider("音符高度 (px)", ref Cfg.NoteHeightPx, 4f, 120f, "F0");
            Slider("轨道宽度 (px)", ref Cfg.LaneWidthPx, 40f, 400f, "F0");
            Slider("轨道间距 (px)", ref Cfg.LaneGapPx, 0f, 80f, "F0");
            Slider("判定线高度", ref Cfg.JudgeLineY, 0f, 1f, "F2");
            Space();

            Header("对齐");
            Slider("时间偏移 (秒)", ref Cfg.TimeOffsetSeconds, -1f, 1f, "F3");
            Space();

            _showFingering = Fold("排键", _showFingering);
            if (_showFingering)
            {
                GUILayout.Label("轨道顺序: D  F  J  K");
                Slider("轮指窗口 (毫秒)", ref Cfg.RollWindowMs, 40f, 400f, "F0");
                GUILayout.Label("间隔小于窗口 = 轮指；大于 = 食指交替");
            }
            Space();

            _showAppearance = Fold("外观", _showAppearance);
            if (_showAppearance)
            {
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
                Logger.Log("frame: " + Chart.FrameDiag(Cfg.NoteSpeedPxPerSec * refScale,
                    Screen.height, frozen));
            }

            // Re-plan only when a fingering input actually changed, not every frame.
            double fingerprint = Chart.Generation * 1000000.0 + Cfg.RollWindowMs * 100.0;
            if (fingerprint == _planFingerprint)
                return;

            _planFingerprint = fingerprint;
            _plan = FingeringPlanner.Assign(Chart.Notes, Cfg.RollWindowMs);
            Logger.Log(string.Format(
                "fingering: {0} notes -> roll {1}, tap {2}, window {3:F0}ms, lanes D={4} F={5} J={6} K={7}",
                Chart.Count, _plan.Rolls, _plan.Taps, _plan.WindowMs,
                _plan.LaneUse[(int)Finger.LeftMiddle], _plan.LaneUse[(int)Finger.LeftIndex],
                _plan.LaneUse[(int)Finger.RightIndex], _plan.LaneUse[(int)Finger.RightMiddle]));
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
        /// Called from the Harmony postfix on AsyncInputManager.OnGUI. The game has already
        /// begun full-screen IMGUI by that point, so plain screen coordinates are correct.
        /// </summary>
        internal static void Draw()
        {
            if (Cfg == null || !Cfg.Enabled || _hidden)
                return;

            // Only while a level is actually being played. This is what keeps the overlay off the
            // menus, the level editor, the pause menu and the death screen.
            bool playing = Chart.Playing();
            Chart.NotePlayState(playing);
            if (!Chart.Ready || !playing)
                return;

            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            _originalFont = GUI.skin.font;
            try
            {
                DrawOverlay();
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

        private static void DrawOverlay()
        {
            float refScale = Mathf.Max(0.5f, Screen.height / 1080f);
            float w = Screen.width;
            float h = Screen.height;

            float laneW = Cfg.LaneWidthPx * refScale;
            float laneGap = Cfg.LaneGapPx * refScale;
            float total = FingeringPlanner.FingerCount * laneW + (FingeringPlanner.FingerCount - 1) * laneGap;
            float x0 = (w - total) * 0.5f;

            float judgeY = h * (1f - Mathf.Clamp01(Cfg.JudgeLineY));
            float speed = Mathf.Max(1f, Cfg.NoteSpeedPxPerSec * refScale);
            float noteH = Mathf.Max(2f, Cfg.NoteHeightPx * refScale);

            Color left = Palette[Cfg.ColourPreset % Palette.Length];
            Color right = Palette[(Cfg.ColourPreset + 1) % Palette.Length];

            // ---- lanes ----------------------------------------------------------
            if (Cfg.ShowLanes)
            {
                var body = new Color(1f, 1f, 1f, 0.06f);
                var edge = new Color(1f, 1f, 1f, 0.28f);
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    float x = x0 + i * (laneW + laneGap);
                    Fill(new Rect(x, 0f, laneW, judgeY), body);
                    Fill(new Rect(x, 0f, 2f, judgeY), edge);
                    Fill(new Rect(x + laneW - 2f, 0f, 2f, judgeY), edge);
                }
            }

            // ---- judgement line -------------------------------------------------
            if (Cfg.ShowJudgeLine)
                Fill(new Rect(0f, judgeY, w, Mathf.Max(2f, 3f * refScale)),
                    new Color(1f, 1f, 1f, 0.85f));

            // ---- key labels -----------------------------------------------------
            // Never change GUI.skin.font here: the panel text has to stay on the game's own font.
            if (Cfg.ShowKeyLabels)
            {
                var label = new GUIStyle(GUI.skin.label);
                label.fontSize = Mathf.RoundToInt(18f * refScale);
                label.alignment = TextAnchor.LowerCenter;
                label.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
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
                var box = new Color(1f, 1f, 1f, 0.22f);
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

            if (!Chart.Ready || Chart.Count == 0)
                return;

            double now = Chart.RenderTime() + Cfg.TimeOffsetSeconds;
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
                    Fill(new Rect(x + (laneW - w2) * 0.5f, boxBottom - h2, w2, h2),
                        new Color(c.r, c.g, c.b, alpha));
                    continue;
                }

                if (Cfg.ShowNotes)
                    Fill(new Rect(x + 2f, y, laneW - 4f, noteH), c);
            }
        }

        // ------------------------------------------------------------------ logging

        internal static void Log(string message)
        {
            if (Logger != null)
                Logger.Log(message);
        }
    }
}
