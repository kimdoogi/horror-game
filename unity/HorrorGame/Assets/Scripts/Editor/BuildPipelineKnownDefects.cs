using System;

namespace HorrorGame.EditorTools
{
    /// <summary>
    /// Build errors that come from a third-party package rather than from this project, are
    /// understood, and cannot be fixed from inside this repository.
    /// <para>
    /// There is exactly one, and adding a second should be hard. An entry here is a
    /// standing decision to ship despite an error, so each one carries the reason it is
    /// harmless and the condition under which it should be deleted.
    /// </para>
    /// <para>
    /// Nothing here suppresses a message. Matching errors are still printed by the pipeline,
    /// still counted, and still listed in <c>build-report.txt</c> under their own heading with
    /// the explanation attached. The only thing an entry changes is whether the error alone
    /// fails the build.
    /// </para>
    /// </summary>
    public static class BuildPipelineKnownDefects
    {
        /// <summary>
        /// Mirror 96.6.4 as published to OpenUPM ships a folder with no <c>.meta</c> beside it.
        /// <para>
        /// The package is a repack of Mirror's GitHub repository, where <c>Mirror</c> is a git
        /// submodule and <c>Mirror/Assets</c> is that submodule's Unity project root. A project
        /// root never has a <c>.meta</c> — nothing is above it to reference it. Repacked into a
        /// UPM package that folder is suddenly an ordinary asset folder, and Unity requires a
        /// <c>.meta</c> for every asset inside a package, so it logs an error on every import
        /// and on every build:
        /// </para>
        /// <code>
        /// Asset Packages/com.mirrornetworking.mirror/Mirror/Assets has no meta file,
        /// but it's in an immutable folder. The asset will be ignored.
        /// </code>
        /// <para>
        /// It is cosmetic. Everything under that folder has its own <c>.meta</c> and imports
        /// normally — <c>Mirror.dll</c>, <c>Mirror.Components</c> and <c>Mirror.Transports</c>
        /// are all compiled into the player, which is why 55 EditMode tests and a working
        /// networked session sit on top of it.
        /// </para>
        /// <para>
        /// It cannot be fixed here. Registry packages live in <c>Library/PackageCache</c>, which
        /// Unity treats as immutable: writing the missing <c>Assets.meta</c> there was tried,
        /// and Unity deleted the file and logged "The following asset(s) located in immutable
        /// packages were unexpectedly altered". The folder is also regenerated from the tarball
        /// whenever packages resolve, so anything written into it is gone on the next clone.
        /// </para>
        /// <para>
        /// Delete this entry when the dependency moves to a version that packs correctly, or
        /// when Mirror is vendored into the repository. Either is a deliberate change to make
        /// on its own; neither belongs in a build fix.
        /// </para>
        /// </summary>
        private const string MirrorPackagePath = "Packages/com.mirrornetworking.mirror/";

        private const string ImmutableMetaSymptom = "has no meta file, but it's in an immutable folder";

        /// <summary>Shown beside the error wherever it is reported.</summary>
        public const string MirrorExplanation =
            "known defect in the OpenUPM package com.mirrornetworking.mirror@96.6.4, not in this project: "
            + "the package repacks Mirror's git submodule, whose project root folder legitimately has no "
            + ".meta. Everything below it has one and is compiled into the player. The package cache is "
            + "immutable and regenerated, so this cannot be corrected from this repository. Not fatal.";

        /// <summary>
        /// True when <paramref name="message"/> is a known third-party defect rather than a
        /// problem with this build.
        /// <para>
        /// Matched narrowly and on purpose: both the symptom and the exact package path have to
        /// be present. A missing <c>.meta</c> anywhere else — including a different immutable
        /// package — is still a build failure, which is the point.
        /// </para>
        /// </summary>
        public static bool IsKnownThirdPartyDefect(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.IndexOf(ImmutableMetaSymptom, StringComparison.Ordinal) >= 0
                && message.IndexOf(MirrorPackagePath, StringComparison.Ordinal) >= 0;
        }

        /// <summary>The one-line explanation for a message <see cref="IsKnownThirdPartyDefect"/> matched.</summary>
        public static string ExplanationFor(string message)
        {
            return IsKnownThirdPartyDefect(message) ? MirrorExplanation : string.Empty;
        }
    }
}
