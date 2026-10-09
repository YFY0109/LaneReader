using System;
using System.IO;
using UnityEngine;

namespace LaneReader
{
    /// <summary>
    /// The optional custom sprites: a png (or jpg) on disk, drawn in place of a flat coloured block.
    /// A relative path is resolved against the mod's own folder, so dropping "note.png" next to
    /// LaneReader.dll is enough; an absolute path works too.
    ///
    /// There are two slots - the note's sprite and the hold bar's - and they are independent: either
    /// can be used without the other. A sprite is drawn exactly as authored. Nothing tints it, and
    /// the outline the flat block has is not drawn around it either: an image already carries the
    /// colours and the shape its author wanted.
    ///
    /// A file is re-read while the game runs, but at most once a second and only when its on-disk
    /// timestamp changed, so a player can swap the image without restarting - and so a missing or
    /// unreadable file costs one failed stat per second rather than one per frame. The last outcome is
    /// reported through <see cref="Status"/>, which the settings panel shows.
    /// </summary>
    internal sealed class NoteImage
    {
        /// <summary>The falling note's sprite.</summary>
        internal const int Note = 0;

        /// <summary>The hold bar's sprite.</summary>
        internal const int Hold = 1;

        private const int Slots = 2;

        /// <summary>What each slot is called in the log and in the panel.</summary>
        private static readonly string[] Labels = { "note image", "hold image" };

        private readonly Texture2D[] _texture = new Texture2D[Slots];
        private readonly string[] _loadedFrom = new string[Slots];
        private readonly long[] _loadedStamp = new long[Slots];
        private readonly string[] _status = { "尚未加载", "尚未加载" };
        private readonly float[] _nextCheck = new float[Slots];
        private System.Reflection.MethodInfo _loadImage;

        /// <summary>Shared by both slots: the loader and its status are per-slot state.</summary>
        internal static readonly NoteImage Instance = new NoteImage();

        /// <summary>What happened on the last load attempt, shown in the panel and logged on change.</summary>
        internal string Status(int slot)
        {
            return _status[Clamp(slot)];
        }

        /// <summary>Force the next <see cref="Get"/> for one slot to re-read the file.</summary>
        internal void Invalidate(int slot)
        {
            slot = Clamp(slot);
            _nextCheck[slot] = 0f;
            _loadedFrom[slot] = null;
            _loadedStamp[slot] = -1;
        }

        /// <summary>
        /// The sprite for a slot, or null when none is configured, the file is unreadable, or it did
        /// not decode. Callers fall back to the flat block on null.
        /// </summary>
        internal Texture2D Get(int slot, string configuredPath)
        {
            slot = Clamp(slot);

            if (Time.unscaledTime < _nextCheck[slot])
                return _texture[slot];

            _nextCheck[slot] = Time.unscaledTime + 1f;

            string path = Resolve(configuredPath);
            if (path == null)
            {
                Drop(slot);
                SetStatus(slot, "未设置路径");
                return null;
            }

            try
            {
                if (!File.Exists(path))
                {
                    Drop(slot);
                    SetStatus(slot, "找不到文件: " + path);
                    return null;
                }

                long stamp = File.GetLastWriteTimeUtc(path).Ticks;
                if (_texture[slot] != null && path == _loadedFrom[slot] && stamp == _loadedStamp[slot])
                    return _texture[slot];

                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok;
                try
                {
                    ok = Decode(texture, bytes);
                }
                catch
                {
                    UnityEngine.Object.Destroy(texture);
                    throw;
                }

                if (!ok)
                {
                    UnityEngine.Object.Destroy(texture);
                    Drop(slot);
                    SetStatus(slot, "解码失败（不是有效的 png/jpg？）: " + path);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;

                Drop(slot);
                _texture[slot] = texture;
                _loadedFrom[slot] = path;
                _loadedStamp[slot] = stamp;
                SetStatus(slot, "已加载 " + texture.width + "x" + texture.height + ": " + Path.GetFileName(path));
                return _texture[slot];
            }
            catch (Exception ex)
            {
                Drop(slot);
                SetStatus(slot, "读取失败: " + ex.Message);
                return null;
            }
        }

        private static int Clamp(int slot)
        {
            if (slot < 0)
                return 0;
            return slot >= Slots ? Slots - 1 : slot;
        }

        /// <summary>
        /// Decodes png/jpg bytes into the texture through UnityEngine.ImageConversion.LoadImage.
        ///
        /// The method is looked up by reflection on purpose. Unity 6 also ships an overload taking
        /// ReadOnlySpan&lt;byte&gt;, a type this mod's C# 5 compiler cannot resolve from the game's
        /// assemblies, so naming the method at compile time fails with CS0012 before the byte[]
        /// overload we actually want is ever considered. The lookup is done once and cached.
        /// </summary>
        private bool Decode(Texture2D texture, byte[] bytes)
        {
            if (_loadImage == null)
                _loadImage = FindLoadImage();

            if (_loadImage == null)
                throw new MissingMethodException(
                    "UnityEngine.ImageConversion.LoadImage(Texture2D, byte[])");

            object result = _loadImage.Invoke(null, new object[] { texture, bytes });
            return result is bool && (bool)result;
        }

        private static System.Reflection.MethodInfo FindLoadImage()
        {
            Type type = typeof(ImageConversion);

            // The exact signature first: matching against a Type[] usually resolves without
            // touching the other overloads, one of which may not even be loadable here.
            try
            {
                System.Reflection.MethodInfo exact = type.GetMethod("LoadImage",
                    new Type[] { typeof(Texture2D), typeof(byte[]) });
                if (exact != null)
                    return exact;
            }
            catch (Exception)
            {
                // Fall through to the scan below.
            }

            try
            {
                foreach (System.Reflection.MethodInfo method in type.GetMethods(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (method.Name != "LoadImage")
                        continue;

                    try
                    {
                        System.Reflection.ParameterInfo[] args = method.GetParameters();
                        if (args.Length == 2 && args[0].ParameterType == typeof(Texture2D)
                            && args[1].ParameterType == typeof(byte[]))
                            return method;
                    }
                    catch (Exception)
                    {
                        // An overload whose parameter types will not load - skip it.
                    }
                }
            }
            catch (Exception)
            {
                // Nothing more to try.
            }

            return null;
        }

        /// <summary>Resolves a configured path to an absolute one; null when nothing is configured.</summary>
        private static string Resolve(string path)
        {
            if (path == null)
                return null;

            path = path.Trim().Trim('"');
            if (path.Length == 0)
                return null;

            if (Path.IsPathRooted(path))
                return path;

            string root = Plugin.ModPath;
            return string.IsNullOrEmpty(root) ? path : Path.Combine(root, path);
        }

        private void Drop(int slot)
        {
            if (_texture[slot] == null)
                return;

            try { UnityEngine.Object.Destroy(_texture[slot]); }
            catch { /* the reference is dropped either way */ }
            _texture[slot] = null;
            _loadedFrom[slot] = null;
            _loadedStamp[slot] = -1;
        }

        private void SetStatus(int slot, string status)
        {
            if (status == _status[slot])
                return;
            _status[slot] = status;
            Plugin.Log(Labels[slot] + ": " + status);
        }
    }
}
