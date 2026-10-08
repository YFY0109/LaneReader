using System;
using System.IO;
using UnityEngine;

namespace LaneReader
{
    /// <summary>
    /// The optional custom note sprite: a png (or jpg) on disk, drawn in place of the flat
    /// coloured block. A relative path is resolved against the mod's own folder, so dropping
    /// "note.png" next to LaneReader.dll is enough; an absolute path works too.
    ///
    /// The file is re-read while the game runs, but at most once a second and only when its
    /// on-disk timestamp changed, so a player can swap the image without restarting - and so a
    /// missing or unreadable file costs one failed stat per second rather than one per frame.
    /// The failure is reported through <see cref="Status"/>, which the settings panel shows.
    /// </summary>
    internal static class NoteImage
    {
        private static Texture2D _texture;
        private static System.Reflection.MethodInfo _loadImage;
        private static string _loadedFrom;
        private static long _loadedStamp = -1;
        private static string _status = "尚未加载";
        private static float _nextCheck;

        /// <summary>What happened on the last load attempt, shown in the panel and logged on change.</summary>
        internal static string Status
        {
            get { return _status; }
        }

        /// <summary>Force the next <see cref="Get"/> to re-read the file.</summary>
        internal static void Invalidate()
        {
            _nextCheck = 0f;
            _loadedFrom = null;
            _loadedStamp = -1;
        }

        /// <summary>
        /// The note sprite, or null when none is configured, the file is unreadable, or it did not
        /// decode. Callers fall back to the flat block on null.
        /// </summary>
        internal static Texture2D Get(string configuredPath)
        {
            if (Time.unscaledTime < _nextCheck)
                return _texture;

            _nextCheck = Time.unscaledTime + 1f;

            string path = Resolve(configuredPath);
            if (path == null)
            {
                Drop();
                SetStatus("未设置路径");
                return null;
            }

            try
            {
                if (!File.Exists(path))
                {
                    Drop();
                    SetStatus("找不到文件: " + path);
                    return null;
                }

                long stamp = File.GetLastWriteTimeUtc(path).Ticks;
                if (_texture != null && path == _loadedFrom && stamp == _loadedStamp)
                    return _texture;

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
                    Drop();
                    SetStatus("解码失败（不是有效的 png/jpg？）: " + path);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;

                Drop();
                _texture = texture;
                _loadedFrom = path;
                _loadedStamp = stamp;
                SetStatus("已加载 " + texture.width + "x" + texture.height + ": " + Path.GetFileName(path));
                return _texture;
            }
            catch (Exception ex)
            {
                Drop();
                SetStatus("读取失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Decodes png/jpg bytes into the texture through UnityEngine.ImageConversion.LoadImage.
        ///
        /// The method is looked up by reflection on purpose. Unity 6 also ships an overload taking
        /// ReadOnlySpan&lt;byte&gt;, a type this mod's C# 5 compiler cannot resolve from the game's
        /// assemblies, so naming the method at compile time fails with CS0012 before the byte[]
        /// overload we actually want is ever considered. The lookup is done once and cached.
        /// </summary>
        private static bool Decode(Texture2D texture, byte[] bytes)
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

        private static void Drop()
        {
            if (_texture == null)
                return;

            try { UnityEngine.Object.Destroy(_texture); }
            catch { /* the reference is dropped either way */ }
            _texture = null;
            _loadedFrom = null;
            _loadedStamp = -1;
        }

        private static void SetStatus(string status)
        {
            if (status == _status)
                return;
            _status = status;
            Plugin.Log("note image: " + status);
        }
    }
}
