using UnityEngine;

namespace LaneReader
{
    /// <summary>
    /// Ready-made colour schemes, one button each in the settings panel.
    ///
    /// A theme is just a set of assignments to the colour fields in <see cref="Settings"/>, so
    /// nothing here is special: once applied, the player can keep editing any single colour. The
    /// two entries that matter most are the first two - they are the "dark level, white lines" and
    /// "light level, black lines" pair the old LightBackground toggle used to switch between.
    ///
    /// One rule holds for all of them: a theme never gives the lanes and the notes the same colour.
    /// The lane body is drawn with <see cref="Settings.LaneOpacity"/> on top, so at the default few
    /// percent the two look nothing alike either way - but a player who winds that opacity up to
    /// 100% has to still be able to tell which bar is a lane and which is a note, so every theme
    /// keeps a clear difference in brightness (or hue) between the two.
    /// </summary>
    internal static class Themes
    {
        internal static readonly string[] Names =
        {
            "经典 蓝橙（暗底白线）",
            "亮底黑线",
            "霓虹 紫青",
            "黄金 黑底",
            "四轨彩虹",
            "极简 纯白",
            "纯黑底 经典白线",
        };

        internal static void Apply(Settings s, int index)
        {
            if (s == null)
                return;

            s.Theme = index;

            // A theme is the whole look, the full-screen backdrop included: every theme but the
            // opaque-black one paints over nothing, so picking one of those has to also switch the
            // backdrop off. It used to leave it exactly as it was, which meant one press of the black
            // theme left a solid black wall over every other theme from then on, with no way back but
            // the switch buried in 「显示」. The switch is of course still there, and still wins: apply
            // a theme and then turn the backdrop back on if that is the look you want.
            s.BackgroundEnabled = false;

            switch (((index % Names.Length) + Names.Length) % Names.Length)
            {
                case 0: Classic(s); break;
                case 1: LightLevel(s); break;
                case 2: Neon(s); break;
                case 3: Gold(s); break;
                case 4: Rainbow(s); break;
                case 5: Minimal(s); break;
                default: BlackOpaque(s); break;
            }
        }

        /// <summary>The default look: white lanes and outlines, blue left hand, orange right hand.</summary>
        private static void Classic(Settings s)
        {
            Lines(s, new Color(1f, 1f, 1f, 1f));
            s.LaneEdgeColour = new Color(1f, 1f, 1f, 0.28f);
            s.LaneSeparate = false;
            s.NoteSeparate = true;
            Set(s.NoteColours, new Color(0.40f, 0.70f, 1.00f, 1f), new Color(1.00f, 0.62f, 0.25f, 1f));
            s.NoteColour = s.NoteColours[0];
            s.NoteEdgeColour = new Color(1f, 1f, 1f, 1f);
        }

        /// <summary>For levels whose background is light: everything neutral goes black.</summary>
        private static void LightLevel(Settings s)
        {
            Lines(s, new Color(0f, 0f, 0f, 1f));
            s.LaneEdgeColour = new Color(0f, 0f, 0f, 0.35f);
            s.LaneSeparate = false;
            s.NoteSeparate = true;
            Set(s.NoteColours, new Color(0.05f, 0.35f, 0.85f, 1f), new Color(0.85f, 0.25f, 0.05f, 1f));
            s.NoteColour = s.NoteColours[0];
            s.NoteEdgeColour = new Color(0f, 0f, 0f, 1f);
        }

        private static void Neon(Settings s)
        {
            Lines(s, new Color(0.55f, 1f, 1f, 1f));
            s.LaneColour = new Color(0.25f, 0.05f, 0.45f, 1f);
            s.LaneEdgeColour = new Color(0.7f, 0.2f, 1f, 0.9f);
            s.LaneSeparate = false;
            s.NoteSeparate = true;
            Set(s.NoteColours, new Color(0.35f, 1f, 0.95f, 1f), new Color(1f, 0.25f, 0.85f, 1f));
            s.NoteColour = s.NoteColours[0];
            s.NoteEdgeColour = new Color(1f, 1f, 1f, 0.9f);
        }

        private static void Gold(Settings s)
        {
            Lines(s, new Color(1f, 0.85f, 0.35f, 1f));
            s.LaneColour = new Color(0.08f, 0.06f, 0.02f, 1f);
            s.LaneEdgeColour = new Color(1f, 0.78f, 0.25f, 0.55f);
            s.LaneSeparate = false;
            s.NoteSeparate = true;
            Set(s.NoteColours, new Color(1f, 0.86f, 0.35f, 1f), new Color(1f, 0.55f, 0.10f, 1f));
            s.NoteColour = s.NoteColours[0];
            s.NoteEdgeColour = new Color(0.35f, 0.22f, 0f, 1f);
        }

        /// <summary>Four different lane and note colours - the per-lane arrays, switched on.</summary>
        private static void Rainbow(Settings s)
        {
            Lines(s, new Color(1f, 1f, 1f, 1f));
            s.LaneColour = new Color(1f, 1f, 1f, 1f);
            s.LaneEdgeColour = new Color(1f, 1f, 1f, 0.28f);

            // The lanes are the same four hues as the notes but around half the brightness, so the
            // notes still stand out if the lane opacity is wound up.
            s.LaneSeparate = true;
            s.LaneColours = new Color[]
            {
                new Color(0.55f, 0.18f, 0.18f, 1f), new Color(0.55f, 0.45f, 0.14f, 1f),
                new Color(0.16f, 0.50f, 0.28f, 1f), new Color(0.20f, 0.34f, 0.55f, 1f),
            };

            s.NoteSeparate = true;
            s.NoteColours = new Color[]
            {
                new Color(1.00f, 0.45f, 0.45f, 1f), new Color(1.00f, 0.88f, 0.40f, 1f),
                new Color(0.45f, 0.95f, 0.60f, 1f), new Color(0.45f, 0.70f, 1.00f, 1f),
            };
            s.NoteColour = s.NoteColours[0];
            s.NoteEdgeColour = new Color(1f, 1f, 1f, 1f);
        }

        private static void Minimal(Settings s)
        {
            Lines(s, new Color(1f, 1f, 1f, 1f));

            // The lane body is a dim grey rather than the note's white. It used to be the same
            // colour: with the default 4% lane opacity that made no difference, but at 100% the two
            // were indistinguishable.
            s.LaneColour = new Color(0.62f, 0.62f, 0.62f, 1f);
            s.LaneEdgeColour = new Color(1f, 1f, 1f, 1f);
            s.LaneSeparate = false;
            s.NoteSeparate = false;
            s.NoteColour = new Color(1f, 1f, 1f, 1f);
            s.NoteEdgeColour = new Color(1f, 1f, 1f, 1f);

            s.LaneOpacity = 0.04f;
            s.BeatLineAlpha = 0.10f;
        }

        /// <summary>
        /// The classic look on an opaque black backdrop - the theme for getting rid of the level's own
        /// background altogether, which the overlay can now paint over (see
        /// <see cref="Settings.BackgroundEnabled"/>).
        ///
        /// Everything else is <see cref="Classic"/>, i.e. white lanes and lines with a blue left hand
        /// and an orange right hand: that is the palette this mod's default look was designed for a
        /// dark level to begin with, so a solid black screen is where it reads best.
        /// </summary>
        private static void BlackOpaque(Settings s)
        {
            Classic(s);
            s.BackgroundEnabled = true;
            s.BackgroundColour = new Color(0f, 0f, 0f, 1f);
        }

        /// <summary>Every line-ish element in one colour: judgement line, boxes, beats, bars, labels, fps.</summary>
        private static void Lines(Settings s, Color line)
        {
            s.JudgeLineColour = new Color(line.r, line.g, line.b, 0.85f);
            s.JudgeBoxColour = new Color(line.r, line.g, line.b, 0.22f);
            s.BeatLineColour = new Color(line.r, line.g, line.b, 1f);
            s.BarLineColour = new Color(line.r, line.g, line.b, 1f);
            s.LabelColour = new Color(line.r, line.g, line.b, 0.75f);
            s.FpsColour = new Color(line.r, line.g, line.b, 0.70f);
            s.LaneColour = new Color(line.r, line.g, line.b, 1f);
        }

        /// <summary>Left-hand colour into lanes 1/2 and right-hand colour into lanes 3/4.</summary>
        private static void Set(Color[] lanes, Color left, Color right)
        {
            lanes[0] = left;
            lanes[1] = left;
            lanes[2] = right;
            lanes[3] = right;
        }
    }
}
