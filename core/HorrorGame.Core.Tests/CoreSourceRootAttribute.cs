using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// Carries the on-disk path of the core sources into the test assembly.
    /// <para>
    /// The value is injected by the .csproj at build time. It lets the suite
    /// assert properties of the source <em>files</em> — most importantly that no
    /// file under Scripts/Core has crept in a <c>UnityEngine</c> reference, which
    /// would compile fine inside Unity and then break every test and the
    /// simulator with it.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class CoreSourceRootAttribute : Attribute
    {
        /// <summary>The path as MSBuild saw it — may be relative and may need normalising.</summary>
        public string Path { get; }

        /// <summary>Creates the attribute. Called only by generated assembly metadata.</summary>
        public CoreSourceRootAttribute(string path) => Path = path;

        /// <summary>
        /// The core source directory, resolved to a full path.
        /// </summary>
        /// <exception cref="InvalidOperationException">The attribute is missing or the directory has moved.</exception>
        public static DirectoryInfo Resolve()
        {
            var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<CoreSourceRootAttribute>()
                ?? throw new InvalidOperationException(
                    "CoreSourceRootAttribute is missing. It is injected by HorrorGame.Core.Tests.csproj — "
                    + "check the AssemblyAttribute item there.");

            var full = System.IO.Path.GetFullPath(attr.Path);
            var dir = new DirectoryInfo(full);
            if (!dir.Exists)
            {
                throw new InvalidOperationException(
                    $"Core source directory not found at '{full}'. The Unity project layout moved — "
                    + "update CoreSourceRoot in HorrorGame.Core.csproj and the AssemblyAttribute in the test project.");
            }

            return dir;
        }

        /// <summary>Every <c>.cs</c> file that makes up the core assembly.</summary>
        public static FileInfo[] EnumerateCoreSources() =>
            Resolve().GetFiles("*.cs", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName, StringComparer.Ordinal)
                .ToArray();
    }
}
