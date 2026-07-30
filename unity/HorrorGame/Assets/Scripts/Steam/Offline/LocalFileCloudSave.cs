#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HorrorGame.Steam.Offline
{
    /// <summary>
    /// Steam Cloud's stand-in: the same files, in
    /// <see cref="Application.persistentDataPath"/>.
    /// <para>
    /// Fully functional, and deliberately so. What §13 puts in Cloud is settings (§15
    /// removed the between-match progression that would otherwise have needed saving),
    /// and settings must survive a restart whether or not Steam is running — a
    /// contributor re-binding keys in an offline build and losing them on every launch
    /// would be a worse bug than having no cloud at all.
    /// </para>
    /// <para>
    /// The file names are the same ones the Steam implementation uses, so a player who
    /// launches offline once and through Steam afterwards gets their settings read back
    /// by the other implementation without a migration step.
    /// </para>
    /// </summary>
    public sealed class LocalFileCloudSave : ICloudSaveService
    {
        private const string FolderName = "cloud";

        /// <summary>Always available: it is just a folder.</summary>
        public bool IsAvailable => true;

        /// <summary>Unmetered — the local disk is not our quota to enforce.</summary>
        public long AvailableBytes => long.MaxValue;

        /// <inheritdoc />
        public bool Write(string fileName, byte[] data)
        {
            var path = ResolvePath(fileName);
            if (path == null || data == null)
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(path, data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Local cloud write failed for " + fileName + ": " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryRead(string fileName, out byte[] data)
        {
            data = Array.Empty<byte>();
            var path = ResolvePath(fileName);
            if (path == null)
            {
                return false;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                data = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Local cloud read failed for " + fileName + ": " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public bool Exists(string fileName)
        {
            var path = ResolvePath(fileName);
            try
            {
                return path != null && File.Exists(path);
            }
            catch (Exception)
            {
                // An unreadable path is an absent file as far as a settings load is
                // concerned, and the load is about to write a default over it anyway.
                return false;
            }
        }

        /// <inheritdoc />
        public bool Delete(string fileName)
        {
            var path = ResolvePath(fileName);
            if (path == null)
            {
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Local cloud delete failed for " + fileName + ": " + ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> List()
        {
            try
            {
                if (!Directory.Exists(Root))
                {
                    return Array.Empty<string>();
                }

                var files = Directory.GetFiles(Root);
                var names = new List<string>(files.Length);
                foreach (var file in files)
                {
                    names.Add(Path.GetFileName(file));
                }

                return names;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Steam] Local cloud list failed: " + ex.Message);
                return Array.Empty<string>();
            }
        }

        /// <inheritdoc />
        public string Describe() => "Local files at " + Root;

        private static string Root => Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>
        /// Maps a cloud file name onto a local path, rejecting anything that is not a
        /// plain file name. Steam Cloud has a flat namespace, so a name containing a
        /// separator is a caller bug — and refusing it here means a name that ever comes
        /// from outside the process cannot walk out of the folder.
        /// </summary>
        private static string? ResolvePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                Debug.LogWarning("[Steam] Rejected cloud file name '" + fileName
                    + "': Steam Cloud names are flat, so separators are always a mistake.");
                return null;
            }

            return Path.Combine(Root, fileName);
        }
    }
}
