#nullable enable

using System.Collections.Generic;

namespace HorrorGame.Steam
{
    /// <summary>
    /// §13's 세이브 row — Steam Cloud, with a local folder standing in offline.
    /// <para>
    /// What this actually holds is worth stating, because it is nearly nothing:
    /// §15 discarded 판 사이 메타 프로그레션 and §08 finishes its growth curve inside
    /// one 25–35 minute match, so there is no progress to save. What is left is
    /// settings — audio levels, mouse sensitivity, §05's FOV, key bindings — which is
    /// exactly the kind of small file Cloud is good at, and which players expect to
    /// follow them between machines.
    /// </para>
    /// <para>
    /// §13 lists 영구 진행도 · 인벤토리 as 없음 and calls that an infrastructure win:
    /// permanent progress would need saving, saving would need cheat validation, and
    /// cheat validation would need a real server. Keeping this interface to opaque
    /// blobs of settings is what stops that door from being reopened by accident.
    /// </para>
    /// </summary>
    public interface ICloudSaveService
    {
        /// <summary>
        /// Whether writes go anywhere durable. The offline implementation is
        /// available too — it writes to the local persistent data path — so this being
        /// true does not mean Steam is involved.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Writes a file, replacing it if it exists. Returns false on failure; never throws.</summary>
        bool Write(string fileName, byte[] data);

        /// <summary>
        /// Reads a file. <paramref name="data"/> is an empty array rather than null
        /// when this returns false, so callers cannot dereference their way into a
        /// crash on a missing settings file.
        /// </summary>
        bool TryRead(string fileName, out byte[] data);

        /// <summary>Whether a file is present.</summary>
        bool Exists(string fileName);

        /// <summary>Deletes a file. True if it is gone afterwards, including when it never existed.</summary>
        bool Delete(string fileName);

        /// <summary>Files currently stored. Cheap: this holds settings, not assets.</summary>
        IReadOnlyList<string> List();

        /// <summary>Remaining quota in bytes, or <see cref="long.MaxValue"/> when the backend does not meter.</summary>
        long AvailableBytes { get; }

        /// <summary>One line naming where saves are actually going, for the log.</summary>
        string Describe();
    }
}
