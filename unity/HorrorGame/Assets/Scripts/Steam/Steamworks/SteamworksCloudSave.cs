#nullable enable

#if HORRORGAME_STEAMWORKS

using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace HorrorGame.Steam.SteamworksBackend
{
    /// <summary>
    /// §13's 세이브 row on ISteamRemoteStorage.
    /// <para>
    /// What it stores is small, and that is the design working: §15 discarded
    /// 판 사이 메타 프로그레션 and §08 completes its growth curve inside a single match,
    /// so there is no progress file — only settings. §13 makes the point explicitly, that
    /// 영구 진행도 would need saving, saving would need cheat validation, and cheat
    /// validation would need the server this project does not have.
    /// </para>
    /// <para>
    /// Cloud can be off per-app or per-account, and a write silently going nowhere is worse
    /// than a failed one, so <see cref="IsAvailable"/> checks both and every write reports
    /// its result.
    /// </para>
    /// </summary>
    public sealed class SteamworksCloudSave : ICloudSaveService
    {
        /// <inheritdoc />
        public bool IsAvailable =>
            SteamRemoteStorage.IsCloudEnabledForApp() && SteamRemoteStorage.IsCloudEnabledForAccount();

        /// <summary>
        /// Remaining Steam Cloud quota. Reported so a caller can refuse a write rather than
        /// discover the refusal afterwards; the settings this holds will never approach it.
        /// </summary>
        public long AvailableBytes
        {
            get
            {
                if (!SteamRemoteStorage.GetQuota(out _, out var available))
                {
                    return 0L;
                }

                // Steam reports an unsigned quota; clamp rather than let the cast wrap into
                // a negative "you have no space" that would refuse every write.
                return available > (ulong)long.MaxValue ? long.MaxValue : (long)available;
            }
        }

        /// <inheritdoc />
        public bool Write(string fileName, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(fileName) || data == null)
            {
                return false;
            }

            if (!IsAvailable)
            {
                // Not an error worth interrupting anyone over: a player can turn Cloud off,
                // and the game keeps working with local settings.
                Debug.Log("[Steam] Cloud is disabled, skipping write of " + fileName + ".");
                return false;
            }

            return SteamRemoteStorage.FileWrite(fileName, data, data.Length);
        }

        /// <inheritdoc />
        public bool TryRead(string fileName, out byte[] data)
        {
            data = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(fileName) || !SteamRemoteStorage.FileExists(fileName))
            {
                return false;
            }

            var size = SteamRemoteStorage.GetFileSize(fileName);
            if (size <= 0)
            {
                // A zero-length file is a previous write that failed halfway. Reporting it
                // as absent makes the caller fall back to defaults instead of parsing
                // nothing.
                return false;
            }

            var buffer = new byte[size];
            var read = SteamRemoteStorage.FileRead(fileName, buffer, size);

            if (read != size)
            {
                Debug.LogWarning("[Steam] Cloud read of " + fileName + " returned " + read + " of " + size + " bytes.");
                return false;
            }

            data = buffer;
            return true;
        }

        /// <inheritdoc />
        public bool Exists(string fileName) =>
            !string.IsNullOrWhiteSpace(fileName) && SteamRemoteStorage.FileExists(fileName);

        /// <summary>
        /// Deletes a file from Cloud and from disk. <c>FileDelete</c> rather than
        /// <c>FileForget</c>: forgetting only stops syncing and leaves the local copy, which
        /// would make a "reset my settings" button appear to do nothing after a restart.
        /// </summary>
        public bool Delete(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return !SteamRemoteStorage.FileExists(fileName) || SteamRemoteStorage.FileDelete(fileName);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> List()
        {
            var count = SteamRemoteStorage.GetFileCount();
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var name = SteamRemoteStorage.GetFileNameAndSize(i, out _);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        /// <inheritdoc />
        public string Describe() => IsAvailable ? "Steam Cloud" : "Steam Cloud (disabled by the player or the app)";
    }
}

#endif
