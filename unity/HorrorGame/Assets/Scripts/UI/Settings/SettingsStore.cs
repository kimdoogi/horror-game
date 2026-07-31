#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace HorrorGame.UI.Settings
{
    /// <summary>
    /// Reads and writes <see cref="GameSettings"/> as one JSON file in
    /// <see cref="Application.persistentDataPath"/>.
    /// <para>
    /// <b>Why a file and not <c>PlayerPrefs</c>.</b> Four reasons, in the order they
    /// bite:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The key rebinds do not fit.</b> The Input System's override blob is one
    /// opaque string that grows with every binding a player moves. <c>PlayerPrefs</c>
    /// would hold it as a string among fifteen unrelated keys, and a half-applied
    /// rebind — some keys written, the process killed before the rest — is a control
    /// scheme nobody can play and nobody can explain. A single file is one write.
    /// </description></item>
    /// <item><description>
    /// <b>A player has to be able to throw it away.</b> §14 puts two humans in front of
    /// this game before anything else, and the first thing that happens when a setting
    /// gets stuck is "delete your settings and try again". A path is an instruction; a
    /// Windows registry key under <c>HKCU\Software\…</c> is a support ticket.
    /// </description></item>
    /// <item><description>
    /// <b>A balance report has to be reproducible.</b> §13 makes a match replay from
    /// its seed, and the two things that change what a player saw and could not be
    /// replayed are the FOV they were on and how bright their screen was. Both are in
    /// this file, and a file can be attached to a bug report.
    /// </description></item>
    /// <item><description>
    /// <b><c>PlayerPrefs</c> has three types.</b> Every bool becomes an int and every
    /// enum becomes an int, so the file on disk stops saying what it means exactly when
    /// somebody has to read it by hand.
    /// </description></item>
    /// </list>
    /// <para>
    /// The write is atomic — a temporary file, then a replace — because the alternative
    /// is truncating the only copy and then crashing, which turns a settings change
    /// into a lost control scheme.
    /// </para>
    /// </summary>
    public static class SettingsStore
    {
        /// <summary>File name inside <see cref="Application.persistentDataPath"/>.</summary>
        public const string FileName = "settings.json";

        private static string? _directoryOverride;

        /// <summary>
        /// Where the file lives. Absolute, and the string a support answer can quote.
        /// </summary>
        public static string Path
        {
            get { return System.IO.Path.Combine(Directory, FileName); }
        }

        /// <summary>Folder holding the file — <see cref="Application.persistentDataPath"/> unless a test moved it.</summary>
        public static string Directory
        {
            get { return _directoryOverride ?? Application.persistentDataPath; }
        }

        /// <summary>
        /// Points the store at another folder, for tests.
        /// <para>
        /// A test that wrote to the real <see cref="Application.persistentDataPath"/>
        /// would overwrite the settings of whoever is running it, and would then pass or
        /// fail depending on what they had configured. Null restores the real path.
        /// </para>
        /// </summary>
        public static void OverrideDirectory(string? directory)
        {
            _directoryOverride = string.IsNullOrEmpty(directory) ? null : directory;
        }

        /// <summary>Whether anything has ever been saved. False on a first launch.</summary>
        public static bool Exists()
        {
            return File.Exists(Path);
        }

        /// <summary>
        /// The settings on disk, clamped, or a fresh default set when there is no file
        /// or the file cannot be understood.
        /// <para>
        /// A file that fails to parse is renamed rather than deleted. It is the only
        /// evidence of whatever wrote it, and the player is about to lose their bindings
        /// either way — keeping the bytes costs nothing and is occasionally the whole
        /// bug report.
        /// </para>
        /// </summary>
        public static GameSettings Load()
        {
            var path = Path;

            if (!File.Exists(path))
            {
                return new GameSettings();
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Settings] Could not read " + path + ": " + error.Message + ". Using defaults.");
                return new GameSettings();
            }

            GameSettings? loaded = null;
            try
            {
                loaded = JsonUtility.FromJson<GameSettings>(text);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Settings] " + path + " is not valid settings JSON: " + error.Message);
            }

            if (loaded == null)
            {
                Quarantine(path);
                return new GameSettings();
            }

            // JsonUtility writes the private fields straight through, so this is the
            // only thing standing between a hand-edited file and a 400-degree FOV.
            return loaded.Clamp();
        }

        /// <summary>
        /// Writes <paramref name="settings"/>, creating the folder if it is missing.
        /// </summary>
        /// <returns>False when the write failed; the reason is logged. Play continues.</returns>
        public static bool Save(GameSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            var path = Path;
            var temporary = path + ".tmp";

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(temporary, JsonUtility.ToJson(settings.Clamp(), prettyPrint: true));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning(
                    "[Settings] Could not write " + path + ": " + error.Message
                    + ". The settings are live for this session and will not survive a restart.");
                return false;
            }
        }

        /// <summary>Removes the file, so the next load returns defaults. Used by the screen's 초기화.</summary>
        public static void Delete()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Settings] Could not delete " + Path + ": " + error.Message);
            }
        }

        private static void Quarantine(string path)
        {
            try
            {
                var broken = path + ".broken";
                if (File.Exists(broken))
                {
                    File.Delete(broken);
                }

                File.Move(path, broken);
                Debug.LogWarning("[Settings] Unreadable settings moved to " + broken + "; defaults restored.");
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Settings] Could not set aside the unreadable file: " + error.Message);
            }
        }
    }
}
