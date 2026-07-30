#nullable enable

using System;

namespace HorrorGame.Net.Host
{
    /// <summary>
    /// Marks a type that may hold §13's answers — clue contents, the objective's
    /// position — and therefore may never appear anywhere a client can read.
    /// <para>
    /// The attribute is not decoration. <see cref="NetReplicationAudit"/> treats a
    /// marked type as poison: if one ever turns up as the type of a
    /// <c>[SyncVar]</c>, inside a sync collection, in the parameters of a
    /// <c>[Command]</c>, <c>[ClientRpc]</c> or <c>[TargetRpc]</c>, or as a field of a
    /// <c>NetworkMessage</c>, the audit fails and the PlayMode test that runs it
    /// fails with it.
    /// </para>
    /// <para>
    /// Why a marker plus a test rather than a language feature: C# has no way to say
    /// "this type is unserialisable". The nearest available structural defence is
    /// already in place — <c>ClueDef</c> is <c>internal</c> to the core, so this
    /// assembly literally cannot name the type that holds a clue's true content —
    /// but <c>ClueReport</c>, <c>SiteLabel</c> and <c>ClueGlyph</c> are public
    /// because a reader has to be told what they saw. Those are the ones a careless
    /// SyncVar could pick up, and this attribute plus the audit is what stops it.
    /// </para>
    /// <para>
    /// ARCHITECTURE §4: "§03's constraint — see it, remember it, say it out loud —
    /// dies the moment the answer is in a client's memory."
    /// </para>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
        Inherited = true,
        AllowMultiple = false)]
    public sealed class HostOnlyAttribute : Attribute
    {
    }
}
