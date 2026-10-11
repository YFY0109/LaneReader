using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LaneReader
{
    /// <summary>
    /// Panel text in more than one language.
    ///
    /// The key IS the Chinese text: every label in the panel goes through <see cref="Tr"/>, which
    /// looks the whole string up in the table and hands back the translation when there is one. That
    /// keeps the code readable (the Chinese stays where it was) and makes a language file something
    /// anybody can write - a flat `中文 = translation` list, no keys to invent and no code to touch.
    ///
    /// Files live in `&lt;mod&gt;\lang\&lt;code&gt;.txt`: UTF-8, one entry per line, `#` starts a comment,
    /// the first `=` splits key from value, and `\n` in a value becomes a line break. Chinese needs
    /// no file at all - the Chinese text is what is already in the code - so `lang\en.txt` doubles as
    /// the template for any other language: copy it, keep the left side, translate the right side,
    /// save as `lang\jp.txt` and it shows up in the panel's language button.
    /// </summary>
    public static class L10n
    {
        /// <summary>The language the code itself is written in; needs no file.</summary>
        public const string DefaultLanguage = "zh";

        private const string Folder = "lang";

        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>();
        private static readonly List<string> Codes = new List<string>();

        private static string _dir;
        private static string _code = DefaultLanguage;
        private static Action<string> _log;

        /// <summary>The language in use, e.g. "zh" or "en".</summary>
        public static string Code { get { return _code; } }

        /// <summary>What the language button shows for the language in use.</summary>
        public static string Name
        {
            get
            {
                if (_code == "zh") return "中文";
                if (_code == "en") return "English";
                return _code;
            }
        }

        /// <summary>Language codes that can be switched to: "zh" plus every file in lang\\.</summary>
        public static IList<string> Available { get { return Codes; } }

        /// <summary>Loads the language list. <paramref name="log"/> may be null.</summary>
        public static void Init(string modPath, Action<string> log)
        {
            _log = log;
            _dir = Path.Combine(modPath, Folder);
            Rescan();
        }

        /// <summary>Re-reads the folder, so a language file dropped in mid-session is picked up.</summary>
        public static void Rescan()
        {
            Codes.Clear();
            Codes.Add(DefaultLanguage);

            try
            {
                if (_dir != null && Directory.Exists(_dir))
                {
                    string[] files = Directory.GetFiles(_dir, "*.txt");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        string code = Path.GetFileNameWithoutExtension(file);
                        if (code.Length > 0 && !Codes.Contains(code))
                            Codes.Add(code);
                    }
                }
            }
            catch (Exception ex)
            {
                Say("lang: cannot list " + _dir + " (" + ex.Message + ")");
            }
        }

        /// <summary>
        /// Switches language. The Chinese default needs no file (the code's own text is the table);
        /// a code with no file falls back to Chinese rather than showing nothing.
        /// </summary>
        public static void Use(string code)
        {
            Table.Clear();

            if (string.IsNullOrEmpty(code))
                code = DefaultLanguage;

            _code = code;
            if (code == DefaultLanguage)
                return;

            string path = Path.Combine(_dir ?? "", code + ".txt");
            if (!File.Exists(path))
            {
                Say("lang: no " + code + ".txt, keeping the Chinese text");
                return;
            }

            try
            {
                int entries = 0;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.TrimEnd();
                    string head = line.TrimStart();
                    if (head.Length == 0 || head[0] == '#' || head[0] == ';')
                        continue;

                    // Tab separated, because plenty of the Chinese keys contain " = " themselves
                    // ("轨道长度 (px, 0=自动到屏幕顶)", "间隔小于窗口 = 轮指；大于 = 食指交替。").
                    // " = " works too, for a file written by hand, as long as the key has no " = "
                    // in it. Leading spaces are kept: they are the panel's own indentation.
                    string key;
                    string value;
                    int tab = line.IndexOf('\t');
                    if (tab > 0)
                    {
                        key = line.Substring(0, tab);
                        value = line.Substring(tab + 1);
                    }
                    else
                    {
                        int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                        if (eq <= 0)
                            continue;
                        key = line.Substring(0, eq);
                        value = line.Substring(eq + 3);
                    }

                    if (key.Length == 0)
                        continue;

                    Table[key] = value.Replace("\\n", "\n");
                    entries++;
                }

                Say("lang: " + code + " loaded (" + entries + " strings)");
            }
            catch (Exception ex)
            {
                Say("lang: cannot read " + code + ".txt (" + ex.Message + ")");
            }
        }

        /// <summary>
        /// The text to show: the translation when the language has one, otherwise the text as it is
        /// (which is always Chinese, so a chart or a label with no entry still reads).
        /// </summary>
        public static string Tr(string text)
        {
            if (string.IsNullOrEmpty(text) || Table.Count == 0)
                return text;

            string value;
            return Table.TryGetValue(text, out value) ? value : text;
        }

        private static void Say(string message)
        {
            if (_log != null)
                _log(message);
        }
    }
}
