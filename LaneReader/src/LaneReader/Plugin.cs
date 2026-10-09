using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>The mod's own folder, so a relative note-image path can be resolved against it.</summary>
        internal static string ModPath;

        private static readonly ChartReader Chart = new ChartReader();
        private static FingeringPlanner.Result _plan;
        private static double _planFingerprint = double.NaN;

        private static Texture2D _white;
        private static Font _originalFont;

        private static bool _hidden;
        private static float _nextLogAt;
        private static bool _guiLogged;

        /// <summary>Render time of the previous drawn frame, for the experimental trail.</summary>
        private static double _lastTrailTime = -1.0;

        /// <summary>Degrees the fun spin has accumulated; reset whenever the mode is off.</summary>
        private static float _spinAngle;

        // ---- strike feedback: purely time driven, the game's judgement is not read ----
        private static int _chartKey = -1;
        /// <summary>
        /// How much the note swells at the line. Small on purpose: a big swell reads as a
        /// different object rather than as the same note being struck.
        /// </summary>
        private const float StrikeGrow = 0.10f;
        private const double StrikeShrink2 = 0.82;

        private static bool _showDisplay = true;
        private static bool _showLayout = true;
        private static bool _showAppearance = false;
        private static bool _showColourLane = true;
        private static bool _showColourNote = true;
        private static bool _showColourJudge = false;
        private static bool _showColourBeat = false;
        private static bool _showColourHold = false;
        private static bool _showFingering = true;
        private static bool _showBeat = true;
        private static bool _showAlign = false;
        private static bool _showExperimental = false;
        private static bool _showFun = false;
        private static bool _showOther = false;

        // The old five-entry hand palette lived here. Colours are per element now (see Settings and
        // Themes), which covers what it did and much more, so it is gone.

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
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

            // ---- 显示 ----------------------------------------------------------
            if (Section("显示", ref _showDisplay))
            {
              using (Indent())
              {
                Toggle("轨道", ref Cfg.ShowLanes);
                Toggle("判定线", ref Cfg.ShowJudgeLine);
                Toggle("音符", ref Cfg.ShowNotes);
                Toggle("键位标签 D F J K", ref Cfg.ShowKeyLabels);
                Toggle("判定框", ref Cfg.ShowJudgeBoxes);
                Toggle("打击弹跳", ref Cfg.ShowHitEffects);
                Toggle("音符边框", ref Cfg.ShowNoteBorder);
                if (Cfg.ShowNoteBorder)
                    Slider("边框粗细 (px)", ref Cfg.NoteBorderPx, 1f, 8f, "F0");
                Toggle("显示帧数 (判定线下方)", ref Cfg.ShowFps);

                Toggle("用自定义音符图片", ref Cfg.NoteImageEnabled);
                if (Cfg.NoteImageEnabled)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("图片路径", GUILayout.Width(LabelWidth));
                    Cfg.NoteImagePath = GUILayout.TextField(Cfg.NoteImagePath, GUILayout.Width(320f));
                    if (GUILayout.Button("重新加载", GUILayout.Width(90f)))
                        NoteImage.Invalidate();
                    GUILayout.EndHorizontal();
                    GUILayout.Label("相对路径基于 mod 目录，例: note.png = " + ModPath + "\\note.png");
                    Toggle("保持宽高比", ref Cfg.NoteImageKeepAspect);
                    NoteImage.Get(Cfg.NoteImagePath);            // refreshes the status line in place
                    GUILayout.Label("状态: " + NoteImage.Status);
                    GUILayout.Label("图片按原样画（不再着色）：没画到的地方就是透明的。");
                    GUILayout.Label("打开「音符边框」时，边框只画在图片外面的一圈，不会垫在图片底下。");
                }
              }
            }

            // ---- 布局 ----------------------------------------------------------
            if (Section("布局", ref _showLayout))
            {
              using (Indent())
              {
                Slider("下落速度 (px/秒 @1080p)", ref Cfg.NoteSpeedPxPerSec, 50f, 3000f, "F0");
                Slider("音符高度 (px)", ref Cfg.NoteHeightPx, 4f, 120f, "F0");
                Slider("轨道宽度 (px)", ref Cfg.LaneWidthPx, 40f, 400f, "F0");
                Slider("轨道间距 (px)", ref Cfg.LaneGapPx, 0f, 80f, "F0");
                Slider("判定线高度 (0~1)", ref Cfg.JudgeLineY, 0f, 1f, "F3");
                Slider("水平偏移 (px)", ref Cfg.OffsetXPx, -1200f, 1200f, "F0");
                Slider("轨道长度 (px, 0=自动到屏幕顶)", ref Cfg.LaneLengthPx, 0f, 2000f, "F0");
                Slider("轨道背景透明度", ref Cfg.LaneOpacity, 0f, 1f, "F3");
                Slider("整体旋转 (度)", ref Cfg.RotationDegrees, 0f, 360f, "F0");
                GUILayout.Label("轨道长度同时是可见区域的上边界：音符和小节线都从那里开始出现。");
              }
            }

            // ---- 外观 / 颜色 ---------------------------------------------------
            if (Section("外观 / 颜色", ref _showAppearance))
            {
              using (Indent())
              {
                GUILayout.Label("主题（一键套用，之后仍可逐项改）:");
                for (int t = 0; t < Themes.Names.Length; t += 2)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Themes.Names[t], GUILayout.Width(240f)))
                        Themes.Apply(Cfg, t);
                    if (t + 1 < Themes.Names.Length
                        && GUILayout.Button(Themes.Names[t + 1], GUILayout.Width(240f)))
                        Themes.Apply(Cfg, t + 1);
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("当前主题: " + Themes.Names[Mathf.Clamp(Cfg.Theme, 0, Themes.Names.Length - 1)]
                                + "（手动改过颜色后这里不会跟着变）");

                if (Section("轨道", ref _showColourLane))
                {
                  using (Indent())
                  {
                    ColourToggle("轨道底色", ref Cfg.LaneSeparate);
                    if (Cfg.LaneSeparate)
                    {
                        for (int i = 0; i < 4; i++)
                            ColourRow("轨道 " + (i + 1) + " 底色", ref Cfg.LaneColours[i]);
                    }
                    else
                    {
                        ColourRow("轨道底色", ref Cfg.LaneColour);
                    }
                    Slider("轨道底色总透明度", ref Cfg.LaneOpacity, 0f, 1f, "F3");
                    GUILayout.Label("总透明度会乘在颜色的 alpha 上（默认 0.06，所以颜色 A 拉满也很淡）。");

                    ColourToggle("轨道边框", ref Cfg.LaneEdgeSeparate);
                    if (Cfg.LaneEdgeSeparate)
                    {
                        for (int i = 0; i < 4; i++)
                            ColourRow("轨道 " + (i + 1) + " 边框", ref Cfg.LaneEdgeColours[i]);
                    }
                    else
                    {
                        ColourRow("轨道边框色", ref Cfg.LaneEdgeColour);
                    }
                  }
                }

                if (Section("音符", ref _showColourNote))
                {
                  using (Indent())
                  {
                    ColourToggle("音符", ref Cfg.NoteSeparate);
                    if (Cfg.NoteSeparate)
                    {
                        for (int i = 0; i < 4; i++)
                            ColourRow("轨道 " + (i + 1) + " 音符", ref Cfg.NoteColours[i]);
                        GUILayout.Label("默认 1/2 轨同色、3/4 轨同色 = 原来的左右手配色。");
                    }
                    else
                    {
                        ColourRow("音符", ref Cfg.NoteColour);
                    }

                    ColourToggle("音符边框", ref Cfg.NoteEdgeSeparate);
                    if (Cfg.NoteEdgeSeparate)
                    {
                        for (int i = 0; i < 4; i++)
                            ColourRow("轨道 " + (i + 1) + " 音符边框", ref Cfg.NoteEdgeColours[i]);
                    }
                    else
                    {
                        ColourRow("音符边框色", ref Cfg.NoteEdgeColour);
                    }
                    GUILayout.Label("显示开关和粗细在「显示」区里。");
                  }
                }

                if (Section("判定线 / 判定框", ref _showColourJudge))
                {
                  using (Indent())
                  {
                    ColourRow("判定线", ref Cfg.JudgeLineColour);
                    ColourRow("判定框", ref Cfg.JudgeBoxColour);
                    ColourRow("键位标签", ref Cfg.LabelColour);
                    ColourRow("帧数文字", ref Cfg.FpsColour);
                  }
                }

                if (Section("小节线", ref _showColourBeat))
                {
                  using (Indent())
                  {
                    ColourRow("细线（每拍）", ref Cfg.BeatLineColour);
                    ColourRow("粗线（每小节）", ref Cfg.BarLineColour);
                    Slider("线总透明度", ref Cfg.BeatLineAlpha, 0f, 1f, "F3");
                    GUILayout.Label("粗线另外再乘 1.8，所以它比细线显眼；把粗线颜色 A 调低就能拉平。");
                  }
                }

                if (Section("长按（hold）", ref _showColourHold))
                {
                  using (Indent())
                  {
                    GUILayout.Label("长按的中间那段线条。要和普通音符一样单独配色就打开下面的开关。");
                    ColourToggle("长按线条", ref Cfg.HoldSeparate);
                    if (Cfg.HoldSeparate)
                    {
                        for (int i = 0; i < 4; i++)
                            ColourRow("轨道 " + (i + 1) + " 长按", ref Cfg.HoldColours[i]);
                    }
                    else
                    {
                        ColourRow("长按线条色", ref Cfg.HoldColour);
                    }
                    Slider("线条粗细（轨道宽的比例）", ref Cfg.HoldWidthFraction, 0.05f, 1f, "F2");
                  }
                }
              }
            }

            // ---- 小节线 --------------------------------------------------------
            if (Section("小节线", ref _showBeat))
            {
              using (Indent())
              {
                Toggle("显示小节线", ref Cfg.ShowBeatLines);
                if (Cfg.ShowBeatLines)
                {
                    IntSlider("每小节拍数 (3=3/4, 4=4/4)", ref Cfg.BeatsPerBar, 2, 8);
                    Slider("小节起点偏移 (拍)", ref Cfg.BeatPhaseBeats, -8f, 8f, "F2");
                    Slider("线透明度", ref Cfg.BeatLineAlpha, 0f, 1f, "F3");
                    Slider("细线粗细 (px)", ref Cfg.BeatLineThicknessPx, 1f, 8f, "F0");
                    Slider("粗线粗细 (px)", ref Cfg.BarLineThicknessPx, 1f, 12f, "F0");
                    Toggle("横跨全屏", ref Cfg.BeatLineFullWidth);
                }
              }
            }

            // ---- 排键 ----------------------------------------------------------
            if (Section("排键（手序）", ref _showFingering))
            {
              using (Indent())
              {
                GUILayout.Label("轨道顺序: D  F  J  K");
                GUILayout.BeginHorizontal();
                GUILayout.Label("手法", GUILayout.Width(LabelWidth));
                if (GUILayout.Button(Cfg.FingeringMode == 1 ? "拆手序" : "AdoFalling 原版",
                        GUILayout.Width(160f)))
                    Cfg.FingeringMode = Cfg.FingeringMode == 1 ? 0 : 1;
                GUILayout.EndHorizontal();

                if (Cfg.FingeringMode == 1)
                {
                    Slider("最大硬抗 BPM", ref Cfg.MaxHardResistBpm, 400f, 1200f, "F0");
                    GUILayout.Label("超过它就减半一次窗口（参考实现原样；越低越偏轮指）。");
                    GUILayout.Label("读不到谱面 BPM 时基准是 800：设成 850 就不减半（base 800），");
                    GUILayout.Label("设成 506 会减半（base 400，轮指更多）。");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("轮指方向", GUILayout.Width(LabelWidth));
                    if (GUILayout.Button(Cfg.OuterRoll ? "外轮 K J D F" : "内轮 J K F D",
                            GUILayout.Width(160f)))
                        Cfg.OuterRoll = !Cfg.OuterRoll;
                    GUILayout.EndHorizontal();
                }
                else
                {
                    Slider("轮指窗口 (毫秒)", ref Cfg.RollWindowMs, 40f, 400f, "F0");
                    GUILayout.Label("间隔小于窗口 = 轮指；大于 = 食指交替。");
                }
              }
            }

            // ---- 对齐 ----------------------------------------------------------
            if (Section("对齐", ref _showAlign))
            {
              using (Indent())
              {
                Slider("时间偏移 (秒)", ref Cfg.TimeOffsetSeconds, -1f, 1f, "F4");
                GUILayout.Label("只影响叠加层显示，不动游戏判定。");
              }
            }

            // ---- 实验性 --------------------------------------------------------
            if (Section("实验性", ref _showExperimental))
            {
              using (Indent())
              {
                Toggle("3D 轨道（灭点在轨道顶端）", ref Cfg.ThreeDLanes);
                if (Cfg.ThreeDLanes)
                {
                    Slider("透视强度", ref Cfg.ThreeDStrength, 0.3f, 3f, "F2");
                    GUILayout.Label("灭点就在轨道顶端（= 判定线上方「轨道长度」那么远），四条轨道向它收敛；");
                    GUILayout.Label("音符越靠近灭点越窄越矮，越靠近判定线越大越快。判定线/判定框/标签都在");
                    GUILayout.Label("判定线这一端，不受影响。强度 = 判定线到「缩到一半」的距离的倒数：");
                    GUILayout.Label("1.00 时音符在判定线附近的屏幕速度和关掉 3D 时一样。");
                }

                Toggle("长按（hold）支持", ref Cfg.HoldSupport);
                if (Cfg.HoldSupport)
                {
                    GUILayout.Label("读 scrFloor.holdLength 画长按长条：长条末端正好接上下一颗音符的开头");
                    GUILayout.Label("（和游戏自己一样，游戏的 hold 尾就是下一块砖）；");
                    GUILayout.Label("拆手序还会把松手当成一颗键，强制和长按开头同轨。颜色在「外观 / 颜色 → 长按」。");
                }

                Toggle("平滑下落（重影拖尾）", ref Cfg.SmoothFalling);
                if (Cfg.SmoothFalling)
                {
                    IntSlider("拖尾层数", ref Cfg.SmoothGhosts, 1, 10);
                    Slider("拖尾浓度", ref Cfg.SmoothStrength, 0f, 1f, "F2");
                    GUILayout.Label("音符位置本来每帧才采一次样（60fps / 1000px/s 时一帧跳约 17px），");
                    GUILayout.Label("拖尾只是把它前几帧的位置淡着补画出来，让眼睛读成运动而不是跳。");
                    GUILayout.Label("采样冻结时（暂停、结算）不会画拖尾。纯观感，不影响时间轴。");
                }

                // ---- 娱乐（纯好玩，不影响读谱）----
                if (Section("娱乐", ref _showFun))
                {
                  using (Indent())
                  {
                    Toggle("轨道乱飞", ref Cfg.FunFly);
                    if (Cfg.FunFly)
                    {
                        Slider("乱飞幅度 (px)", ref Cfg.FunFlyAmplitudePx, 0f, 900f, "F0");
                        Slider("乱飞速度", ref Cfg.FunFlySpeed, 0.02f, 3f, "F2");
                        GUILayout.Label("每条轨道走自己的 Perlin 噪声路径（平滑漂移，不是抖），轨道和它的音符一起走。");
                    }

                    Toggle("轨道持续旋转", ref Cfg.FunSpin);
                    if (Cfg.FunSpin)
                    {
                        Slider("旋转速度 (度/秒)", ref Cfg.FunSpinSpeed, 5f, 180f, "F0");
                        GUILayout.Label("整个叠加层绕判定线中点转，叠加在「整体旋转」的角度上；关掉就回到你设的角度。");
                    }

                    Toggle("四花瓣（四条轨道互相垂直）", ref Cfg.FunPetals);
                    if (Cfg.FunPetals)
                    {
                        GUILayout.Label("四条轨道的底部中心都在判定线中点，各绕它转 0/90/180/270 度，");
                        GUILayout.Label("所以朝四个互相垂直的方向伸出去，中间没有方块。");
                    }
                  }
                }
              }
            }

            // ---- 其它 ----------------------------------------------------------
            if (Section("其它", ref _showOther))
            {
              using (Indent())
              {
                if (GUILayout.Button("切换键: " + Cfg.Key, GUILayout.Width(200f)))
                    Cfg.ToggleKey = NextKey(Cfg.ToggleKey);
                Toggle("输出诊断日志", ref Cfg.DebugLog);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("恢复默认", GUILayout.Width(110f)))
                    Cfg = new Settings();
                if (GUILayout.Button("保存", GUILayout.Width(110f)))
                    Cfg.Save(modEntry);
                GUILayout.EndHorizontal();
                GUILayout.Label("改动即时生效；「保存」写进 Settings.json，关面板时 UMM 也会自动保存。");
              }
            }

            GUILayout.EndVertical();
        }

        // ---- panel helpers -------------------------------------------------

        private const float LabelWidth = 210f;
        private const float SliderWidth = 200f;
        private const float NumberWidth = 66f;

        /// <summary>How far each nesting level of the options pushes its contents right.</summary>
        private const float IndentWidth = 16f;

        /// <summary>How wide a section's header button is at nesting level 0.</summary>
        private const float HeaderWidth = 330f;

        /// <summary>Nesting level the panel is currently drawing at, for the header widths.</summary>
        private static int _indentLevel;

        /// <summary>
        /// A collapsible section header. It is a BUTTON with an arrow - deliberately not a toggle.
        /// A toggle draws the skin's little circle, and an empty circle next to a section name reads
        /// as "this feature is switched off" rather than "this heading is folded away" (and a filled
        /// one as "on"), which is exactly the confusion it caused. A button is pressed, not on/off.
        ///
        /// The body goes inside a <see cref="Indent"/> scope, so a section nested in a section is
        /// visibly inset from the one that contains it; the header button also shrinks by the indent
        /// so it still fits the panel at any nesting level.
        /// </summary>
        private static bool Section(string title, ref bool open)
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            float width = Mathf.Max(120f, HeaderWidth - _indentLevel * IndentWidth);
            if (GUILayout.Button((open ? "▼ " : "▶ ") + title, GUILayout.Width(width)))
                open = !open;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return open;
        }

        /// <summary>
        /// Indents everything drawn until it is disposed, which is what makes the panel's nesting
        /// visible. A using block rather than a Begin/End pair on purpose: it is closed on the way
        /// out even if the body throws, so the IMGUI layout groups cannot be left open.
        /// </summary>
        private static IDisposable Indent()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(IndentWidth);
            GUILayout.BeginVertical();
            _indentLevel++;
            return new IndentScope();
        }

        private sealed class IndentScope : IDisposable
        {
            public void Dispose()
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                _indentLevel--;
            }
        }

        private static void Space()
        {
            GUILayout.Space(4f);
        }

        private static void Toggle(string label, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));
            value = GUILayout.Toggle(value, value ? "开" : "关", GUILayout.Width(80f));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// A slider that can also be typed into: drag it, or click the box on the right and write
        /// the number. The typed text is committed on Enter or when the box loses focus, and only
        /// then is it clamped and parsed - parsing on every keystroke would fight the user over
        /// values that are not finished yet ("-" or "1." are not numbers, and rewriting the box
        /// from the value would eat the caret).
        /// </summary>
        private static void Slider(string label, ref float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            float dragged = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderWidth));
            if (dragged != value)
                value = dragged;

            string text;
            if (!_typed.TryGetValue(label, out text))
                text = value.ToString(format);

            GUI.SetNextControlName(label);
            string edited = GUILayout.TextField(text, GUILayout.Width(NumberWidth));

            if (GUI.GetNameOfFocusedControl() == label)
            {
                _typed[label] = edited;
                if (EnterPressed())
                {
                    value = Commit(label, edited, value, min, max);
                    // Keep the caret in the box but show the value that was actually taken, so an
                    // out-of-range entry does not sit there looking accepted.
                    _typed[label] = value.ToString(format);
                }
            }
            else if (_typed.ContainsKey(label))
            {
                value = Commit(label, text, value, min, max);
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>A slider over whole numbers, for settings that are stored as an int.</summary>
        private static void IntSlider(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LabelWidth));

            int dragged = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(SliderWidth)));
            if (dragged != value)
                value = dragged;

            string text;
            if (!_typed.TryGetValue(label, out text))
                text = value.ToString();

            GUI.SetNextControlName(label);
            string edited = GUILayout.TextField(text, GUILayout.Width(NumberWidth));

            if (GUI.GetNameOfFocusedControl() == label)
            {
                _typed[label] = edited;
                if (EnterPressed())
                {
                    value = Mathf.RoundToInt(Commit(label, edited, value, min, max));
                    _typed[label] = value.ToString();
                }
            }
            else if (_typed.ContainsKey(label))
            {
                value = Mathf.RoundToInt(Commit(label, text, value, min, max));
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>Numbers being typed right now, keyed by the field that owns them.</summary>
        private static readonly Dictionary<string, string> _typed = new Dictionary<string, string>();

        /// <summary>
        /// An RGBA colour: four 0..1 channel sliders and a hex box (RRGGBBAA, '#' optional), using
        /// the same commit-on-Enter rule as the number fields.
        /// </summary>
        private static void ColourRow(string label, ref Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(126f));

            c.r = Channel("R", c.r);
            c.g = Channel("G", c.g);
            c.b = Channel("B", c.b);
            c.a = Channel("A", c.a);

            string key = "hex:" + label;
            string text;
            if (!_typed.TryGetValue(key, out text))
                text = Hex(c);

            GUI.SetNextControlName(key);
            string edited = GUILayout.TextField(text, GUILayout.Width(74f));

            if (GUI.GetNameOfFocusedControl() == key)
            {
                _typed[key] = edited;
                if (EnterPressed())
                {
                    c = ParseHex(edited, c);
                    _typed[key] = Hex(c);
                }
            }
            else if (_typed.ContainsKey(key))
            {
                c = ParseHex(text, c);
            }

            GUILayout.EndHorizontal();
        }

        private static float Channel(string axis, float v)
        {
            GUILayout.Label(axis, GUILayout.Width(13f));
            return GUILayout.HorizontalSlider(v, 0f, 1f, GUILayout.Width(54f));
        }

        /// <summary>A checkbox with a shorter label column, for the "one per lane" switches.</summary>
        private static void ColourToggle(string label, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(126f));
            value = GUILayout.Toggle(value, value ? "四轨单独" : "四轨统一", GUILayout.Width(90f));
            GUILayout.EndHorizontal();
        }

        private static string Hex(Color c)
        {
            return ((int)(Mathf.Clamp01(c.r) * 255f + 0.5f)).ToString("X2")
                   + ((int)(Mathf.Clamp01(c.g) * 255f + 0.5f)).ToString("X2")
                   + ((int)(Mathf.Clamp01(c.b) * 255f + 0.5f)).ToString("X2")
                   + ((int)(Mathf.Clamp01(c.a) * 255f + 0.5f)).ToString("X2");
        }

        /// <summary>RRGGBB or RRGGBBAA; anything unparsable leaves the colour alone.</summary>
        private static Color ParseHex(string text, Color current)
        {
            if (text == null)
                return current;

            text = text.Trim().TrimStart('#');
            if (text.Length != 6 && text.Length != 8)
                return current;

            int r, g, b, a = 255;
            if (!int.TryParse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                || !int.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                || !int.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                return current;

            if (text.Length == 8
                && !int.TryParse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
                return current;

            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        private static bool EnterPressed()
        {
            Event e = Event.current;
            return e != null && e.type == EventType.KeyDown
                   && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter
                       || e.keyCode == KeyCode.Tab);
        }

        /// <summary>Parses a typed number, clamps it, and stops tracking the field.</summary>
        private static float Commit(string key, string text, float current, float min, float max)
        {
            _typed.Remove(key);

            float parsed;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed))
                return current;

            return Mathf.Clamp(parsed, min, max);
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

            MeasureFrameRate();

            // The fun spin is integrated here rather than read from unscaledTime so that changing the
            // speed does not teleport the overlay; turning it off rewinds to the player's own angle.
            if (Cfg.FunSpin)
                _spinAngle = Mathf.Repeat(_spinAngle + Time.unscaledDeltaTime * Cfg.FunSpinSpeed, 360f);
            else
                _spinAngle = 0f;

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

            // The chart's true BPM is only knowable as it plays, so the beat grid is learned along
            // with it; see ChartReader.SampleBeatTempo.
            Chart.SampleBeatTempo(Chart.RenderTime());

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
                float refScale = RefScale();
                bool frame = Chart.LevelStanding();
                Logger.Log("frame: " + FrameRateText() + " | " + Chart.FrameDiag(
                    Cfg.NoteSpeedPxPerSec * refScale, Screen.height, frozen, frame,
                    NotesVisible(frame)));
            }

            // Re-plan only when a fingering input actually changed, not every frame. HoldSupport is
            // part of the fingerprint because it decides whether the releases are fed in at all: a
            // toggle that changes the plan but not the fingerprint would leave the old lanes in place.
            double fingerprint = Chart.Generation * 1000000.0 + Cfg.RollWindowMs * 100.0
                                 + Cfg.FingeringMode * 100000000.0 + Cfg.MaxHardResistBpm
                                 + (Cfg.OuterRoll ? 1.0 : 2.0)
                                 + (Cfg.HoldSupport ? 4.0 : 8.0);
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

            // With hold support on, every hold contributes a second entry for its release - one beat
            // list entry per key the player is responsible for - and that release is forced onto the
            // head's lane. The release's time is the head's time plus the hold's own duration, so it
            // sits inside the same timeline the planner walks; holdHead maps each entry back to the
            // press it belongs to (-1 for an ordinary note).
            var times = new List<double>(notes.Count);
            var holdHead = new List<int>(notes.Count);
            var noteOfEntry = new List<int>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
            {
                times.Add(notes[i].HitTime);
                holdHead.Add(-1);
                noteOfEntry.Add(i);

                if (!Cfg.HoldSupport || notes[i].HoldSeconds <= 0.0)
                    continue;

                int head = times.Count - 1;
                times.Add(notes[i].HitTime + notes[i].HoldSeconds);
                holdHead.Add(head);
                noteOfEntry.Add(i);
            }

            // The original port's source, and deliberately so. scrLevelMaker.bpm_forUnityUseOnly is
            // not readable on this install, so this hands HandPlanner a 0 and leaves its own fallback
            // in charge - which normalises to 800 BPM, and that is the fingering the player knows:
            // almost every note lands in the "hand over" band, and dense passages roll.
            // Substituting a BPM guessed from the note spacing here silently re-scaled every chart.
            double bpm = Chart.LevelBpm();
            HandPlan plan = HandPlanner.Assign(times, bpm, Cfg.MaxHardResistBpm,
                2, Cfg.OuterRoll, Cfg.HoldSupport ? holdHead.ToArray() : null);

            // Only the presses get a lane back; a release rides along with its head and is not drawn
            // as its own note (the hold's bar marks where it is).
            for (int entry = 0; entry < noteOfEntry.Count && entry < plan.Lanes.Length; entry++)
            {
                if (holdHead[entry] >= 0)
                    continue;

                int noteIndex = noteOfEntry[entry];
                if (noteIndex >= notes.Count)
                    continue;

                Note note = notes[noteIndex];
                note.Lane = plan.Lanes[entry];
                notes[noteIndex] = note;
            }

            Logger.Log(string.Format(
                "hand split: {0} notes, level bpm {1:F1}{2}, {3}, {4}, rolls {5}, taps {6} hand switches {7}",
                notes.Count, bpm, bpm > 0.0 ? "" : " (unreadable!)", plan.Summary,
                Cfg.OuterRoll ? "outer roll" : "inner roll", plan.Rolls, plan.Taps,
                CountHandSwitches(plan)));
        }

        // ------------------------------------------------------------------ frame rate

        /// <summary>Frames counted since the last frame-rate sample.</summary>
        private static int _fpsFrames;
        private static float _fpsElapsed;
        private static float _fps = -1f;
        private static float _frameMs = -1f;

        /// <summary>
        /// Counts frames and re-samples the rate twice a second.
        ///
        /// Tick runs from a postfix on the game's own Update, so this is the game's frame rate, not
        /// a rate of our own - the overlay has no loop to have one. Averaging over half a second
        /// keeps the number readable instead of flickering between neighbouring values.
        /// </summary>
        private static void MeasureFrameRate()
        {
            _fpsFrames++;
            _fpsElapsed += Time.unscaledDeltaTime;
            if (_fpsElapsed < 0.5f || _fpsFrames <= 0)
                return;

            _fps = _fpsFrames / _fpsElapsed;
            _frameMs = _fpsElapsed * 1000f / _fpsFrames;
            _fpsFrames = 0;
            _fpsElapsed = 0f;
        }

        /// <summary>"143 fps  7.0 ms", or a placeholder until the first sample lands.</summary>
        private static string FrameRateText()
        {
            if (_fps < 0f)
                return "-";
            return string.Format("{0:F0} fps  {1:F1} ms", _fps, _frameMs);
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
        ///   * the static frame - lanes, judgement line, boxes, key labels - is wanted as soon as a
        ///     level is up, including the "press any key to start" prompt a fresh level opens with,
        ///     so it only needs the level to be standing;
        ///   * the notes belong to a run that is under way, which takes two things:
        ///     <see cref="ChartReader.TimelineLive"/> (the clock has moved since the chart was set up)
        ///     and <see cref="ChartReader.SongRunning"/> (the music has actually begun).
        ///
        /// Neither of those is a state test, and that is on purpose. ChartReader.Playing() is: it
        /// accepts only PlayerControl/Checkpoint/Countdown, and the game reports "Start" both while
        /// paused and while waiting for the first key press. Gating on it hid the notes when the
        /// player paused to look at the chart (log: "state=Start song=1 live=1 frame=1 notes=0"),
        /// and dropping it let a retry's prompt show notes (log: "state=Start song=0 live=1 ... notes=1").
        /// The conductor's own flag separates those two cases exactly.
        ///
        /// Together the two gates are what keep the overlay off the menus, the level editor, the
        /// death screen and the pre-start prompt.
        /// </summary>
        private static bool NotesVisible(bool frame)
        {
            return frame && Chart.TimelineLive() && Chart.SongRunning();
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
            Matrix4x4 savedMatrix = GUI.matrix;
            try
            {
                // The whole overlay turns as one piece, about the middle of its judgement line, so
                // the pivot stays put and only the angle changes. The settings panel is drawn
                // elsewhere and is unaffected. The fun spin is added on top of the player's angle.
                float angle = Cfg.RotationDegrees + (Cfg.FunSpin ? _spinAngle : 0f);
                if (angle != 0f)
                    GUIUtility.RotateAroundPivot(angle, Pivot());

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
                GUI.matrix = savedMatrix;
                GUI.skin.font = _originalFont;
            }
        }

        /// <summary>
        /// The point the overlay rotates about: the centre of the judgement line, which is also
        /// the centre of the four lanes. Everything the overlay draws is anchored to that line, so
        /// rotating about it keeps the playfield in the same place on screen.
        /// </summary>
        private static Vector2 Pivot()
        {
            float refScale = RefScale();
            float judgeY = Screen.height * (1f - Mathf.Clamp01(Cfg.JudgeLineY));
            return new Vector2(Screen.width * 0.5f + Cfg.OffsetXPx * refScale, judgeY);
        }

        /// <summary>Pixels per 1080p reference pixel.</summary>
        private static float RefScale()
        {
            return Mathf.Max(0.5f, Screen.height / 1080f);
        }

        /// <summary>
        /// Draws the overlay. The frame is always drawn; <paramref name="drawNotes"/> is false on
        /// the "press any key to start" prompt, where only the frame is wanted.
        /// </summary>
        private static void DrawOverlay(bool drawNotes)
        {
            float refScale = RefScale();
            float w = Screen.width;
            float h = Screen.height;

            float laneW = Cfg.LaneWidthPx * refScale;
            float laneGap = Cfg.LaneGapPx * refScale;
            float total = FingeringPlanner.FingerCount * laneW + (FingeringPlanner.FingerCount - 1) * laneGap;
            float x0 = (w - total) * 0.5f + Cfg.OffsetXPx * refScale;

            float judgeY = h * (1f - Mathf.Clamp01(Cfg.JudgeLineY));
            float speed = Mathf.Max(1f, Cfg.NoteSpeedPxPerSec * refScale);
            float noteH = Mathf.Max(2f, Cfg.NoteHeightPx * refScale);

            // ---- playfield geometry ---------------------------------------------
            // The lane length is its own setting (LaneLengthPx), not "whatever is left above the
            // judgement line": deriving it from the line meant every change to the line height or
            // the rotation silently re-stretched the lanes. 0 asks for the old behaviour (all the
            // way to the screen edge); otherwise the lanes are exactly that long and ride up and
            // down with the line.
            //
            // laneTop is also the top of the visible playfield: notes and beat lines come into
            // being there rather than at the screen edge, so a pinned lane length really does
            // decide how much of the fall you get to see.
            float laneLen = Cfg.LaneLengthPx > 0f ? Cfg.LaneLengthPx * refScale : judgeY;
            float laneTop = judgeY - laneLen;

            // The overlay's centre line: every lane's centre converges on this as it approaches the
            // vanishing point, so it is also the x the 3D mode tapers towards.
            float cx = w * 0.5f + Cfg.OffsetXPx * refScale;

            // ---- the 3D projection ----------------------------------------------
            // See View: with 3D on, everything that falls along a lane is placed by one pinhole
            // camera whose vanishing point is exactly the lane top, and with 3D off the view is the
            // plain linear placement the overlay has always used.
            Field field = new Field();
            field.View = View.Make(Cfg.ThreeDLanes, judgeY, laneLen);
            field.X0 = x0;
            field.Width = laneW;
            field.Gap = laneGap;
            field.Cx = cx;
            field.JudgeY = judgeY;
            field.Top = laneTop;
            field.NoteH = noteH;

            // ---- lanes ----------------------------------------------------------
            if (Cfg.ShowLanes)
            {
                if (Cfg.ThreeDLanes)
                {
                    DrawLanes3D(field);
                }
                else
                {
                    for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                    {
                        Matrix4x4 laneMatrix = GUI.matrix;
                        if (Cfg.FunPetals)
                            GUIUtility.RotateAroundPivot(i * 90f,
                                PetalRoot(cx, judgeY));

                        float x = LaneX(i, x0, laneW, laneGap, cx) + LaneWanderX(i);
                        float y = laneTop + LaneWanderY(i);
                        Fill(new Rect(x, y, laneW, laneLen), Fade(LaneBodyColour(i), Cfg.LaneOpacity));
                        Color edge = LaneEdgeColour(i);
                        Fill(new Rect(x, y, 2f, laneLen), edge);
                        Fill(new Rect(x + laneW - 2f, y, 2f, laneLen), edge);
                        GUI.matrix = laneMatrix;
                    }
                }
            }

            double now = Chart.RenderTime() + Cfg.TimeOffsetSeconds;

            // ---- beat lines -----------------------------------------------------
            // Drawn right after the lanes so they sit behind the line, the boxes and the notes:
            // they are a reading aid, not a playfield element. They scroll with the clock, so the
            // pre-start prompt (which draws the frame only) leaves them out.
            if (drawNotes && Cfg.ShowBeatLines && Chart.Ready)
                DrawBeatLines(field, refScale, w, x0, total, judgeY, laneTop, speed, noteH, now);

            // ---- judgement line -------------------------------------------------
            if (Cfg.ShowJudgeLine)
                Fill(new Rect(0f, judgeY, w, Mathf.Max(2f, 3f * refScale)), Cfg.JudgeLineColour);

            // ---- key labels -----------------------------------------------------
            // Never change GUI.skin.font here: the panel text has to stay on the game's own font.
            if (Cfg.ShowKeyLabels)
            {
                var label = new GUIStyle(GUI.skin.label);
                label.fontSize = Mathf.RoundToInt(18f * refScale);
                label.alignment = TextAnchor.LowerCenter;
                label.normal.textColor = Cfg.LabelColour;
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    Matrix4x4 laneMatrix = GUI.matrix;
                    if (Cfg.FunPetals)
                        GUIUtility.RotateAroundPivot(i * 90f,
                            PetalRoot(cx, judgeY));

                    float x = LaneX(i, x0, laneW, laneGap, cx) + LaneWanderX(i);
                    float y = judgeY + 6f * refScale + LaneWanderY(i);
                    GUI.Label(new Rect(x, y, laneW, 26f * refScale),
                        FingeringPlanner.KeyName(i), label);
                    GUI.matrix = laneMatrix;
                }
            }

            // ---- judgement boxes -------------------------------------------------
            // One rectangle per lane, same size as a note, sitting so its CENTRE LINE is the
            // judgement line. Notes are positioned against that centre line, which is what makes
            // the strike pop grow symmetrically about the line instead of off one edge.
            if (Cfg.ShowJudgeBoxes)
            {
                float t = Mathf.Max(2f, 2f * refScale);
                Color box = Cfg.JudgeBoxColour;
                for (int i = 0; i < FingeringPlanner.FingerCount; i++)
                {
                    Matrix4x4 laneMatrix = GUI.matrix;
                    if (Cfg.FunPetals)
                        GUIUtility.RotateAroundPivot(i * 90f,
                            PetalRoot(cx, judgeY));

                    float x = LaneX(i, x0, laneW, laneGap, cx) + LaneWanderX(i);
                    float top = judgeY - noteH * 0.5f + LaneWanderY(i);
                    Fill(new Rect(x, top, laneW, t), box);                       // top edge
                    Fill(new Rect(x, top + noteH - t, laneW, t), box);           // bottom edge
                    Fill(new Rect(x, top, t, noteH), box);                       // left edge
                    Fill(new Rect(x + laneW - t, top, t, noteH), box);           // right edge
                    GUI.matrix = laneMatrix;
                }
            }

            // ---- frame rate ------------------------------------------------------
            // Under the judgement line, so it never covers the playfield. Drawn with the frame, so
            // it is there on the pre-start prompt too. It sits below the key labels when those are
            // on; notes and the strike pop all stay at boxBottom and above, so nothing is hidden by
            // it.
            if (Cfg.ShowFps)
            {
                var fpsStyle = new GUIStyle(GUI.skin.label);
                fpsStyle.fontSize = Mathf.RoundToInt(16f * refScale);
                fpsStyle.alignment = TextAnchor.UpperCenter;
                fpsStyle.normal.textColor = Cfg.FpsColour;

                float fpsTop = judgeY + (Cfg.ShowKeyLabels ? 34f : 6f) * refScale;
                GUI.Label(new Rect(0f, fpsTop, w, 22f * refScale), FrameRateText(), fpsStyle);
            }

            // Frame only: the pre-start prompt stops here.
            if (!drawNotes || !Chart.Ready || Chart.Count == 0)
                return;

            int count = Chart.Count;
            IList<Note> notes = Chart.Notes;

            // ---- notes ----------------------------------------------------------
            // In petal mode the notes are drawn once per lane, inside that lane's rotation, so each
            // petal's notes fall outward along their own petal. Otherwise this is one pass over them
            // all. The single-pass case is byte-for-byte what it always was.
            int passes = Cfg.FunPetals ? FingeringPlanner.FingerCount : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                Matrix4x4 laneMatrix = GUI.matrix;
                if (Cfg.FunPetals)
                    GUIUtility.RotateAroundPivot(pass * 90f,
                        PetalRoot(cx, judgeY));

                for (int i = 0; i < count; i++)
                {
                    Note note = notes[i];

                    if (Cfg.FunPetals && note.Lane != pass)
                        continue;

                    // Skip what the game has not generated, and what it has already played. A hold
                    // note outlives its own hit (see ChartReader.ShouldDraw): its bar is still
                    // travelling towards the line, and its head keeps its strike feedback up, until
                    // the tile after it arrives.
                    if (!Chart.ShouldDraw(i))
                        continue;

                    // Bottom edge of the judgement box. Both the falling note and the strike pop are
                    // positioned against it, so the note arrives exactly where the pop starts - any
                    // other reference makes it look like the note snaps backwards on contact.
                    float boxBottom = judgeY + noteH * 0.5f;

                    // How far above the judgement line the note is, in flat pixels of fall: its
                    // CENTRE is exactly (hit time - now) * speed above the line, so at the hit it sits
                    // centred on the line - which is where the strike pop grows from.
                    float fall = (float)((note.HitTime - now) * speed);
                    float noteScale = field.View.Scale(fall);
                    float noteRise = field.View.Rise(fall);
                    float centreY = judgeY - noteRise;
                    float y = centreY - noteH * 0.5f;             // the note's flat top edge
                    double age = now - note.HitTime;

                    // A hold is not over when its head is struck. HoldSeconds is the whole gap to the
                    // next tile (see ChartReader), so the bar's far end is that tile's own arrival:
                    // the bar stays on screen and is eaten by the judgement line until then, and the
                    // head's strike feedback stays up for as long as the key is held.
                    bool holding = Cfg.HoldSupport && note.HoldSeconds > 0.0;
                    double holdEnd = holding ? note.HoldSeconds : 0.0;

                    // Coming into being: flat, at the top of the lane, so a note simply appears there
                    // fully formed (the same thing the lane bodies do). Under 3D there is no such edge
                    // - the lane runs all the way to the vanishing point - so a note shrinks towards
                    // nothing as it climbs and is culled once it is too small to read. Without that
                    // every note left in the chart would be drawn as a speck around that point.
                    //
                    // A note that has already been struck is not culled here: what is left of it on
                    // screen is its strike pop, its trail running out, and the rest of a hold's bar,
                    // and each of those decides for itself whether it is still worth drawing.
                    if (age < 0.0)
                    {
                        if (Cfg.ThreeDLanes)
                        {
                            if (noteH * noteScale < 1.5f)
                                continue;
                        }
                        else if (y < laneTop || y > boxBottom + noteH * 2f)
                        {
                            continue;
                        }
                    }

                    float x = LaneX(note.Lane, x0, laneW, laneGap, cx);
                    Color c = NoteBodyColour(note.Lane);
                    Color edge = NoteEdgeColour(note.Lane);

                    // ---- what goes behind the note: its hold bar, then its trail ------------
                    if (Cfg.ShowNotes)
                    {
                        // A hold's body, first, so it sits behind the head and behind the trail. It
                        // used to be drawn last, which painted over the trail: the bar rises from the
                        // head's top edge and every ghost is up there too, so a hold note looked like
                        // it had no trail while every other note did.
                        //
                        // DrawHoldBar clamps the bar's near end to the judgement line, which is what
                        // keeps it on screen after the head has been struck: the line eats the bar
                        // from the bottom until the tail (the next note) arrives.
                        if (holding)
                            DrawHoldBar(field, note.Lane, fall + noteH * 0.5f,
                                fall + noteH * 0.5f + (float)(holdEnd * speed));

                        // The trail only makes sense while the clock is moving: it is the distance the
                        // clock advanced since the previous frame, so a frozen clock (pause, death
                        // screen) produces none rather than a smear of a note that is standing still.
                        // It keeps running after the hit as well, so the line eats the ghosts from the
                        // bottom up instead of the whole trail blinking out with the note.
                        if (Cfg.SmoothFalling && !Chart.ClockFrozen && _lastTrailTime > 0.0)
                        {
                            float travel = (float)((now - _lastTrailTime) * speed);
                            if (travel > 0.05f && travel < h * 0.5f)
                                DrawTrail(field, c, fall, travel, note.Lane);
                        }
                    }

                    // ---- the falling note ------------------------------------------------
                    if (age < 0.0)
                    {
                        if (!Cfg.ShowNotes)
                            continue;

                        // The note is a plate lying on its lane, so it is projected as one: its near
                        // edge is at the bottom of the note and its far edge at the top. Under 3D that
                        // squeezes it along the lane as it climbs and it converges with its lane's
                        // edges; with 3D off the two edges are the plain flat rectangle it always was.
                        FillNotePlate(field, note.Lane, fall - noteH * 0.5f, fall + noteH * 0.5f,
                            laneW - 4f, c, edge, refScale, true);
                        continue;
                    }

                // ---- strike pop ----------------------------------------------------
                // Reaching the line pins the note there. It then swells and shrinks, but its
                // BOTTOM EDGE stays welded to the judgement line throughout, so the note reads as
                // resting on the line rather than floating over it. Purely time driven: the
                // game's judgement is never consulted.
                //
                // A HOLD's pop does not shrink while the key is still down: it swells to the strike
                // size and then stands still for the whole hold, and only shrinks away once the tail
                // (the next note) arrives. The feedback for a hold has to say "still holding", not
                // "done".
                double popAge = age - holdEnd;                      // < 0 while the hold is held
                if (!Cfg.ShowHitEffects || popAge > ChartReader.StrikePopSeconds)
                    continue;

                float popScale;
                float fade;
                if (popAge < 0.0)
                {
                    // Held: swell over the first quarter of a pop window, then hold that size.
                    float grow = (float)(age / (ChartReader.StrikePopSeconds * 0.25));
                    popScale = 1f + StrikeGrow * Mathf.Clamp01(grow);
                    fade = 1f;
                }
                else if (holding)
                {
                    // Released: shrink from the full strike size, fading as it goes.
                    float u = (float)(popAge / ChartReader.StrikePopSeconds);   // 0..1
                    popScale = (1f + StrikeGrow) - (float)((1f + StrikeGrow - StrikeShrink2) * u);
                    fade = 1f - u * u;
                }
                else
                {
                    float t = (float)(popAge / ChartReader.StrikePopSeconds);   // 0..1

                    // Quick swell, then a long contraction that goes well past the original size
                    // so the shrink is unmistakable.
                    if (t < 0.25f)
                        popScale = 1f + StrikeGrow * (t / 0.25f);
                    else
                    {
                        float u = (t - 0.25f) / 0.75f;
                        popScale = (1f + StrikeGrow) - (float)((1f + StrikeGrow - StrikeShrink2) * u);
                    }
                    fade = 1f - t * t;                                 // fade out
                }

                float w2 = laneW * popScale;
                float h2 = noteH * popScale;
                Color pop = Fade(c, fade);
                Color popEdge = Fade(edge, fade);

                // Bottom edge welded to the bottom of the judgement box, so the note grows
                // upward and never drifts off the line as its size changes.
                FillNote(new Rect(x + (laneW - w2) * 0.5f, boxBottom - h2, w2, h2),
                    pop, popEdge, refScale);
                continue;
                }

                if (Cfg.FunPetals)
                    GUI.matrix = laneMatrix;
            }

            _lastTrailTime = now;
        }

        /// <summary>
        /// The experimental motion-blur trail: the note, drawn again where it was on the previous
        /// frames, faded. <paramref name="travel"/> is how far it moved since the last frame, so the
        /// copies land exactly on the previous frames' positions rather than on a guess - and they go
        /// through the same projection as the note itself, so under 3D the ghosts stay on the note's
        /// own lane (offsetting them by <c>travel</c> pixels straight up would trail them off the lane
        /// as it narrows).
        ///
        /// The trail outlives the note. Once the note has been struck its ghosts are still above the
        /// line and still falling, so they are drawn until the judgement line has eaten them - each
        /// ghost is clipped at the line instead of being dropped whole, which is what makes the trail
        /// sink into the line rather than blink out with the note.
        /// </summary>
        private static void DrawTrail(Field field, Color c, float fall, float travel, int lane)
        {
            int layers = Mathf.Clamp(Cfg.SmoothGhosts, 1, 10);
            float strength = Mathf.Clamp01(Cfg.SmoothStrength);
            float width = field.Width - 4f;

            // Far layers first, so the nearest one ends up on top of them and directly under the note.
            for (int k = layers; k >= 1; k--)
            {
                float alpha = strength * (1f - (k - 1f) / layers);
                if (alpha <= 0.004f)
                    continue;

                // The ghost is the same plate, one frame further up the lane, so it flattens and
                // narrows with its lane exactly like the note does.
                float d = fall + travel * k;
                float near = d - field.NoteH * 0.5f;
                float far = d + field.NoteH * 0.5f;

                if (!Cfg.ThreeDLanes && far > field.JudgeY - field.Top)
                    continue;                       // above the playfield, same gate as the note

                // Eaten by the judgement line: everything below the line is behind it. The ghost is
                // clipped rather than dropped, so the trail sinks into the line instead of blinking
                // out with the note.
                if (near < 0f)
                    near = 0f;
                if (far <= near)
                    continue;

                FillNotePlate(field, lane, near, far, width, Fade(c, alpha), c, 1f, false);
            }
        }

        /// <summary>
        /// Fills a note, with its outline when the outline is enabled. The block is drawn in the
        /// outline colour and the note's own colour is inset on top of it, so a note keeps exactly
        /// the footprint it has without an outline. The outline is the note's own border colour
        /// (per lane, when the lane colours are separate) and it fades with the note, so a strike
        /// pop does not leave a hard-edged rectangle behind.
        ///
        /// With a custom sprite the outline is a RING drawn around the sprite rather than a filled
        /// rectangle behind it. That distinction is the whole point: a backing rectangle shows
        /// through every transparent pixel of the sprite, which turns them into the outline colour
        /// (visibly white with the default border colour) instead of leaving them transparent.
        /// The sprite is drawn with its own colours - only the note's alpha, which is what makes a
        /// struck note fade out, is applied on top of it.
        /// </summary>
        private static void FillNote(Rect r, Color c, Color edge, float refScale)
        {
            Texture2D image = Cfg.NoteImageEnabled ? NoteImage.Get(Cfg.NoteImagePath) : null;

            float b = Mathf.Max(1f, Cfg.NoteBorderPx * refScale);
            bool border = Cfg.ShowNoteBorder && r.width > b * 2f && r.height > b * 2f;

            if (image == null)
            {
                if (!border)
                {
                    Fill(r, c);
                    return;
                }

                Fill(r, edge);
                Fill(new Rect(r.x + b, r.y + b, r.width - 2f * b, r.height - 2f * b), c);
                return;
            }

            if (border)
            {
                float innerW = r.width - 2f * b;
                float innerH = r.height - 2f * b;

                Fill(new Rect(r.x, r.y, r.width, b), edge);                     // top
                Fill(new Rect(r.x, r.y + r.height - b, r.width, b), edge);      // bottom
                Fill(new Rect(r.x, r.y + b, b, innerH), edge);                  // left
                Fill(new Rect(r.x + r.width - b, r.y + b, b, innerH), edge);    // right

                r = new Rect(r.x + b, r.y + b, innerW, innerH);
            }

            if (Cfg.NoteImageKeepAspect)
            {
                float aspect = image.height > 0 ? (float)image.width / image.height : 1f;
                float iw = r.height * aspect;
                float ih = r.height;
                if (iw > r.width)
                {
                    iw = r.width;
                    ih = aspect > 0f ? r.width / aspect : r.height;
                }
                r = new Rect(r.x + (r.width - iw) * 0.5f, r.y + (r.height - ih) * 0.5f, iw, ih);
            }

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(c.a));
            GUI.DrawTexture(r, image, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        /// <summary>
        /// The local x a lane is drawn at, before any petal rotation. Normally that is its own slot on
        /// the judgement line; in petal mode every lane is drawn at the middle of the overlay and then
        /// rotated outwards, so they all share the same local x.
        /// </summary>
        private static float LaneX(int lane, float x0, float laneW, float laneGap, float cx)
        {
            if (Cfg.FunPetals)
                return cx - laneW * 0.5f;
            return x0 + lane * (laneW + laneGap);
        }

        /// <summary>
        /// The point every petal turns about: the middle of the judgement line, which is also where
        /// all four lanes have their bottom centre. There is no centre square any more - the four
        /// lanes simply radiate from this one point, each rotated a quarter turn further than the
        /// last, so their bases share a centre and their bodies point (up, right, down, left).
        /// </summary>
        private static Vector2 PetalRoot(float cx, float judgeY)
        {
            return new Vector2(cx, judgeY);
        }

        // ---- experimental fun ------------------------------------------------

        /// <summary>
        /// Where lane <paramref name="lane"/> has wandered off to, in reference pixels. Perlin noise
        /// per lane (and per axis) so each lane drifts smoothly and independently instead of
        /// jittering, and unscaledTime so it keeps moving while the game is paused - it is a toy, and
        /// freezing it on pause would look like a bug.
        /// </summary>
        private static float LaneWanderX(int lane)
        {
            if (!Cfg.FunFly)
                return 0f;
            float t = Time.unscaledTime * Mathf.Max(0.01f, Cfg.FunFlySpeed);
            float seed = lane * 13.37f + 5.1f;
            return (Mathf.PerlinNoise(seed, t) * 2f - 1f) * Cfg.FunFlyAmplitudePx * RefScale();
        }

        private static float LaneWanderY(int lane)
        {
            if (!Cfg.FunFly)
                return 0f;
            float t = Time.unscaledTime * Mathf.Max(0.01f, Cfg.FunFlySpeed);
            float seed = lane * 27.11f + 91.7f;
            return (Mathf.PerlinNoise(seed, t) * 2f - 1f) * Cfg.FunFlyAmplitudePx * 0.6f * RefScale();
        }

        // ---- experimental 3D -------------------------------------------------

        /// <summary>
        /// The projection of the playfield, resolved once per frame (see <see cref="Field"/>).
        ///
        /// 3D puts the vanishing point exactly at the lane top - the "lane length" setting is its
        /// height above the judgement line - and projects everything that falls along a lane through
        /// one pinhole camera. A point that is <c>d</c> pixels of fall above the judgement line ends
        /// up
        /// <code>
        ///     rise(d)  = L * d / (d + eye)       pixels above the judgement line
        ///     scale(d) = eye / (d + eye)         times its flat size
        /// </code>
        /// where L is the lane length and eye = L / strength is the fall distance at which a point has
        /// shrunk to half size and climbed to half the height of the lane.
        ///
        /// That pair is the whole model, and it is what makes the lane edges straight: scale = 1 -
        /// rise / L, so a point's distance from the lane group's centre line is proportional to its
        /// height above the line. That IS the vanishing-point property, and it is why the four lanes
        /// meet at one point, why a note slides along its lane's edges instead of beside them, and why
        /// the lanes can be drawn as plain triangles instead of the sliced stacks the first attempt
        /// used. Notes are also genuinely slower up high and faster near the line, which is what a
        /// receding lane does; the hit moment is exact either way, because rise(0) = 0.
        ///
        /// The first attempt instead scaled each element by a hand-written taper and left every y
        /// linear, so the lanes converged but nothing that fell along them did - the notes ran on a
        /// flat plane and only their width shrank, which read as four lines crossing in mid-air.
        /// </summary>
        private struct View
        {
            /// <summary>The judgement line's y.</summary>
            public float JudgeY;

            /// <summary>The lane length: judgement line to lane top, which is the vanishing point.</summary>
            public float LineLen;

            /// <summary>Fall distance at which a point is half size. Unused when flat.</summary>
            public float Eye;

            /// <summary>True with 3D off, or when there is no lane height to taper over.</summary>
            public bool Flat;

            /// <summary>
            /// The view for this frame. <paramref name="laneLen"/> is the lane's own length, so a
            /// degenerate geometry (the judgement line at the very top of the screen) falls back to
            /// the flat rules rather than dividing by zero.
            /// </summary>
            public static View Make(bool threeD, float judgeY, float laneLen)
            {
                var view = new View();
                view.JudgeY = judgeY;
                view.LineLen = Mathf.Max(1f, laneLen);
                view.Flat = !threeD || laneLen <= 1f;
                view.Eye = view.LineLen / Mathf.Clamp(Cfg.ThreeDStrength, 0.3f, 3f);
                return view;
            }

            /// <summary>
            /// How far above the judgement line a point that far up the fall sits.
            ///
            /// The projection is exact, and deliberately extends BELOW the judgement line too: a note
            /// is a plate lying on its lane, so for the last half note of its fall its near edge is
            /// genuinely below the line, and the height of the plate is whatever the projection says
            /// the distance between its two edges is - not a scale factor applied to a rectangle. That
            /// distinction is the difference between a 3D note and a small flat one.
            ///
            /// The only thing guarded against is the camera plane itself (d = -eye), where the
            /// projection is infinite: it takes a short lane and a strong taper to get anywhere near
            /// it, but there is no reason to leave that to chance.
            /// </summary>
            public float Rise(float d)
            {
                if (Flat)
                    return d;

                float u = CameraDepth(d);
                return LineLen * (u - Eye) / u;
            }

            /// <summary>What a flat size has to be multiplied by at that distance.</summary>
            public float Scale(float d)
            {
                if (Flat)
                    return 1f;
                return Eye / CameraDepth(d);
            }

            /// <summary>d + eye, kept away from the camera plane (and therefore from zero).</summary>
            private float CameraDepth(float d)
            {
                float u = d + Eye;
                float closest = Eye * 0.05f;
                return u < closest ? closest : u;
            }

            /// <summary>
            /// The far end of the visible fall: where things have shrunk to a tenth. The lane's own
            /// far end is the vanishing point, which is infinitely far away, so a horizon has to be
            /// picked somewhere - this is the one the beat grid and the note cull use.
            /// </summary>
            public float Horizon
            {
                get { return Flat ? float.MaxValue : Eye * 9f; }
            }
        }

        /// <summary>
        /// Where the four lanes are, in one value. The note, trail and hold-bar helpers all need the
        /// same handful of numbers plus the projection, and passing them loose made every call site a
        /// wall of floats.
        /// </summary>
        private struct Field
        {
            public float X0;        // left edge of lane 0
            public float Width;     // one lane's width
            public float Gap;       // between lanes
            public float Cx;        // the overlay's centre line: where the vanishing point sits
            public float JudgeY;    // the judgement line
            public float Top;       // the lane top, i.e. the vanishing point's height
            public float NoteH;     // a note's flat height
            public View View;

            /// <summary>A lane's flat centre line, before the perspective is applied.</summary>
            public float SlotCentre(int lane)
            {
                return LaneX(lane, X0, Width, Gap, Cx) + Width * 0.5f;
            }

            /// <summary>That centre line at perspective scale <paramref name="p"/>.</summary>
            public float Centre(int lane, float p)
            {
                return Cx + (SlotCentre(lane) - Cx) * p;
            }
        }

        /// <summary>
        /// Paints a note-shaped plate lying on its lane: the piece of the flat note box between
        /// <paramref name="dNear"/> and <paramref name="dFar"/> (flat pixels of fall above the
        /// judgement line, near edge first), <paramref name="flatWidth"/> wide when seen flat.
        ///
        /// Two things come out of doing it this way round rather than scaling the flat rectangle:
        ///
        ///   * the plate's height is the projected distance between its two edges, so a note high up
        ///     the lane is squeezed along the lane - it flattens out, which is what a plate seen from
        ///     a low angle does, and what "drawn smaller" never looks like;
        ///   * it is painted in horizontal bands, so its sides follow the lane's converging edges
        ///     instead of being parallel.
        ///
        /// The band count is 1 when there is no perspective, where the slab IS a rectangle: that path
        /// is then the exact rectangle the overlay has always drawn, band for band and pixel for
        /// pixel. Only the border is inset per band (the first and last bands carry the top and bottom
        /// edges, the middle ones only the sides), which is what keeps a bordered plate from growing a
        /// horizontal line at every band boundary.
        /// </summary>
        private static void FillNotePlate(Field field, int lane, float dNear, float dFar, float flatWidth,
            Color c, Color edge, float refScale, bool bordered)
        {
            if (dFar <= dNear)
                return;

            int bands = field.View.Flat ? 1 : 3;
            float b = Mathf.Max(1f, Cfg.NoteBorderPx * refScale);
            float wanderX = LaneWanderX(lane);
            float wanderY = LaneWanderY(lane);

            for (int band = 0; band < bands; band++)
            {
                float d0 = dNear + (dFar - dNear) * band / bands;
                float d1 = dNear + (dFar - dNear) * (band + 1) / bands;

                float top = field.View.JudgeY - field.View.Rise(d1) + wanderY;
                float bottom = field.View.JudgeY - field.View.Rise(d0) + wanderY;
                float height = bottom - top;
                if (height <= 0f)
                    continue;

                float p = field.View.Scale((d0 + d1) * 0.5f);
                float width = Mathf.Max(1f, flatWidth * p);
                float centre = field.Centre(lane, p) + wanderX;
                Rect r = new Rect(centre - width * 0.5f, top, width, height + 0.5f);

                bool border = bordered && Cfg.ShowNoteBorder && r.width > b * 2f && r.height > b * 2f;
                if (!border)
                {
                    Fill(r, c);
                    continue;
                }

                Fill(r, edge);
                float innerTop = r.y + (band == bands - 1 ? b : 0f);          // the far band carries the top edge
                float innerHeight = r.height - (band == bands - 1 ? b : 0f) - (band == 0 ? b : 0f);
                Fill(new Rect(r.x + b, innerTop, r.width - 2f * b, innerHeight), c);
            }
        }

        /// <summary>
        /// Draws one straight lane edge: a rotated rectangle from the lane's base corner up to the
        /// vanishing point. An axis-aligned rectangle is all IMGUI can fill, and a rotated one is
        /// exactly the line it needs to be - unlike the stack of horizontal bands the body is made of,
        /// which leaves a one-pixel sawtooth on every step and is what read as aliasing.
        /// </summary>
        private static void DrawEdgeLine(float x0, float y0, float x1, float y1, float thickness,
            Color colour)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= 0.01f || thickness <= 0f)
                return;

            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, new Vector2(x0, y0));
            Fill(new Rect(x0, y0 - thickness * 0.5f, length, thickness), colour);
            GUI.matrix = saved;
        }

        /// <summary>
        /// The 3D lane group: four bodies that share one apex - the vanishing point at the lane top,
        /// which is exactly where the "lane length" setting puts the top of the lane.
        ///
        /// The body of a lane is drawn as a stack of horizontal bands whose width is the lane's width
        /// at that band's height. Because the scale falls off linearly with height (see View), the
        /// silhouette of that stack is the triangle it should be, and with a band every ~17 pixels the
        /// steps are sub-pixel once the edges are drawn over them.
        ///
        /// The two EDGES are not bands: each is one rotated rectangle running from the lane's base
        /// corner to the shared apex, which is exact. That is what removed the sawtooth - a banded
        /// edge steps sideways by a pixel every band, and on a 2px line that reads as aliasing.
        /// </summary>
        private static void DrawLanes3D(Field field)
        {
            const int Bands = 96;

            float len = field.JudgeY - field.Top;
            float step = len / Bands;
            if (step <= 0f)
                return;

            for (int i = 0; i < FingeringPlanner.FingerCount; i++)
            {
                Matrix4x4 laneMatrix = GUI.matrix;
                if (Cfg.FunPetals)
                    GUIUtility.RotateAroundPivot(i * 90f, PetalRoot(field.Cx, field.JudgeY));

                float wanderX = LaneWanderX(i);
                float wanderY = LaneWanderY(i);
                Color line = LaneEdgeColour(i);
                Color body = Fade(LaneBodyColour(i), Cfg.LaneOpacity);
                float apexX = field.Cx + wanderX;

                for (int band = 0; band < Bands; band++)
                {
                    // The band's middle, measured above the judgement line. Its height is the only
                    // thing the scale needs, since scale = 1 - rise / len.
                    float rise = step * (band + 0.5f);
                    float p = 1f - rise / len;
                    float width = field.Width * p;
                    if (width < 0.4f)
                        continue;                       // sub-pixel sliver at the vanishing point

                    float top = field.JudgeY - rise - step * 0.5f + wanderY;   // the band's top edge
                    float centre = field.Centre(i, p) + wanderX;
                    Fill(new Rect(centre - width * 0.5f, top, width, step + 0.5f), body);
                }

                // The edges, on top of the bands and inside the same rotation.
                float half = field.Width * 0.5f;
                float baseCentre = field.Centre(i, 1f) + wanderX;
                float baseY = field.JudgeY + wanderY;
                float apexY = field.Top + wanderY;
                DrawEdgeLine(baseCentre - half, baseY, apexX, apexY, 2f, line);
                DrawEdgeLine(baseCentre + half, baseY, apexX, apexY, 2f, line);

                GUI.matrix = laneMatrix;
            }
        }

        /// <summary>
        /// A hold's body: the bar between the head and the tail of the hold, drawn upwards from the
        /// head's own top edge. <paramref name="headTop"/> and <paramref name="tailTop"/> are flat
        /// pixels of fall, and the tail is the next tile's arrival, so the bar's far end lands exactly
        /// on the next note at every moment - the bar is the whole reach of the hold, which is what
        /// the game itself draws (its hold mesh runs from the held tile to that tile's next floor).
        ///
        /// The near end is clamped to the judgement line: once the head has been struck its top edge
        /// is below the line, and the part of the bar below the line is behind it. That clamp is what
        /// lets a hold outlive its head - the bar stays up and is eaten from the bottom until the tail
        /// arrives.
        ///
        /// It is banded, so under 3D it tapers with the lane it sits in; the width is a fraction of
        /// the lane's width, so it stays proportional when the perspective narrows the note.
        /// </summary>
        private static void DrawHoldBar(Field field, int lane, float headTop, float tailTop)
        {
            if (headTop < 0f)
                headTop = 0f;                       // below the line: eaten by it

            float span = tailTop - headTop;
            if (span <= 0f)
                return;

            Color colour = HoldBodyColour(lane);
            if (colour.a <= 0.002f)
                return;

            float fraction = Mathf.Clamp(Cfg.HoldWidthFraction, 0.05f, 1f);
            float wanderX = LaneWanderX(lane);
            float wanderY = LaneWanderY(lane);

            const int Bands = 24;
            for (int band = Bands - 1; band >= 0; band--)
            {
                float d0 = headTop + span * band / Bands;
                float d1 = headTop + span * (band + 1) / Bands;
                float rise0 = field.View.Rise(d0);
                float rise1 = field.View.Rise(d1);
                float height = rise1 - rise0;
                if (height <= 0f)
                    continue;

                float p = field.View.Scale((d0 + d1) * 0.5f);
                float width = Mathf.Max(1f, (field.Width - 4f) * fraction * p);
                float centre = field.Centre(lane, p) + wanderX;

                // The band's top edge is the farther end of it, i.e. the bigger rise.
                Fill(new Rect(centre - width * 0.5f, field.View.JudgeY - rise1 + wanderY,
                    width, height + 0.5f), colour);
            }
        }

        // ---- colours --------------------------------------------------------

        /// <summary>Lane body colour: the single one, or the lane's own when they are separate.</summary>
        private static Color LaneBodyColour(int lane)
        {
            return Cfg.LaneSeparate ? At(Cfg.LaneColours, lane) : Cfg.LaneColour;
        }

        private static Color LaneEdgeColour(int lane)
        {
            return Cfg.LaneEdgeSeparate ? At(Cfg.LaneEdgeColours, lane) : Cfg.LaneEdgeColour;
        }

        private static Color NoteBodyColour(int lane)
        {
            return Cfg.NoteSeparate ? At(Cfg.NoteColours, lane) : Cfg.NoteColour;
        }

        private static Color NoteEdgeColour(int lane)
        {
            return Cfg.NoteEdgeSeparate ? At(Cfg.NoteEdgeColours, lane) : Cfg.NoteEdgeColour;
        }

        private static Color HoldBodyColour(int lane)
        {
            return Cfg.HoldSeparate ? At(Cfg.HoldColours, lane) : Cfg.HoldColour;
        }

        private static Color At(Color[] colours, int lane)
        {
            if (colours == null || colours.Length == 0)
                return Color.white;
            return colours[((lane % colours.Length) + colours.Length) % colours.Length];
        }

        /// <summary>The colour with its alpha scaled, keeping its RGB.</summary>
        private static Color Fade(Color c, float factor)
        {
            return new Color(c.r, c.g, c.b, c.a * factor);
        }

        /// <summary>
        /// Beat and bar lines: thin on every beat, thick every BeatsPerBar beats.
        ///
        /// The beat times come from the chart itself (ChartReader works out a tile's length in
        /// beats from the planet's turn), so the lines sit on the chart's own beats and follow it
        /// through accelerations and slowdowns instead of being pinned to one tempo. Each line is
        /// positioned against the same bottom edge the notes arrive at, so it crosses the judgement
        /// line exactly on its beat, and it is only drawn from <paramref name="laneTop"/> downwards -
        /// the same top edge the notes appear at.
        /// </summary>
        private static void DrawBeatLines(Field field, float refScale, float screenW, float x0, float total,
            float judgeY, float laneTop, float speed, float noteH, double now)
        {
            IList<BeatLine> lines = Chart.BeatLines;
            if (lines.Count == 0)
                return;

            int perBar = Mathf.Clamp(Cfg.BeatsPerBar, 2, 16);
            int downbeat = Mathf.RoundToInt(Cfg.BeatPhaseBeats);
            float boxBottom = judgeY + noteH * 0.5f;

            // How far ahead in time the lines are drawn. Flat, that is exactly the top of the lane -
            // the same bound the notes come into being at. Under 3D there is no such edge (the lane's
            // far end is the vanishing point, which is infinitely far away), so the grid runs out to
            // the far field instead: the distance at which the projection has shrunk things to a
            // tenth, where a line and its neighbour are a pixel apart.
            double topTime = Cfg.ThreeDLanes
                ? now + field.View.Horizon / speed
                : now + (boxBottom - laneTop) / speed;

            float left = Cfg.BeatLineFullWidth ? 0f : x0;
            float width = Cfg.BeatLineFullWidth ? screenW : total;
            float cx = screenW * 0.5f + Cfg.OffsetXPx * refScale;

            for (int index = FirstLineAtOrAfter(lines, now); index < lines.Count; index++)
            {
                BeatLine line = lines[index];
                if (line.Time > topTime)
                    break;

                int beat = line.Beat + downbeat;
                bool bar = ((beat % perBar) + perBar) % perBar == 0;
                float thick = Mathf.Max(1f,
                    (bar ? Cfg.BarLineThicknessPx : Cfg.BeatLineThicknessPx) * refScale);

                // The master opacity trims the colour's own alpha; bars are still drawn a bit
                // stronger than beats, as they were before the colours were settable.
                Color ink = bar ? Cfg.BarLineColour : Cfg.BeatLineColour;
                float alpha = Mathf.Clamp01(ink.a * Cfg.BeatLineAlpha * (bar ? 1.8f : 1f));

                float fall = (float)((line.Time - now) * speed);
                float y = judgeY - field.View.Rise(fall);

                // Lines are walked in time order and y only decreases, so the first one above the
                // lane top ends the loop.
                if (y < laneTop)
                    break;

                // Under 3D the line is only as wide as the lane group is at that height, unless it
                // was asked to span the whole screen anyway.
                float lineLeft = left;
                float lineWidth = width;
                float scale = field.View.Scale(fall);
                if (!Cfg.BeatLineFullWidth && scale != 1f)
                {
                    lineLeft = cx + (x0 - cx) * scale;
                    lineWidth = total * scale;
                }

                Fill(new Rect(lineLeft, y, lineWidth, thick), new Color(ink.r, ink.g, ink.b, alpha));
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
        /// How often the plan changes hands between windows - the number that says whether a chart
        /// is being played with one hand at a time or with both, and therefore the quickest way to
        /// see what the hard-resist limit is doing.
        /// </summary>
        private static int CountHandSwitches(HandPlan plan)
        {
            int switches = 0;
            for (int i = 1; i < plan.WindowPreferred.Count; i++)
            {
                if (plan.WindowPreferred[i] != plan.WindowPreferred[i - 1])
                    switches++;
            }
            return switches;
        }

        /// <summary>
        /// The chart's BPM, with the fallbacks the hand-split planner uses: the game's own value
        /// when it is usable, otherwise a guess from the note spacing. 0 means neither worked, and
        /// <paramref name="source"/> says which one answered.
        /// </summary>
        private static double ChartBpm(out string source)
        {
            // scrConductor.bpm first: it is the level's own BPM, the field JipperResourcePack reads
            // on this install, and the value the reference macro takes from the chart file.
            double bpm = Chart.ConductorBpm();
            source = "conductor";
            if (bpm < 20.0 || bpm > 1000.0)
            {
                bpm = Chart.LevelBpm();
                source = "level";
            }
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
