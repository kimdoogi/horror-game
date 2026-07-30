using System;
using System.Text;

namespace HorrorGame.Core.Tests
{
    /// <summary>
    /// Crude C# lexing, just enough for tests that inspect source text.
    /// <para>
    /// Needed because the core deliberately <em>discusses</em> the things it must
    /// not use — doc comments explain why <c>Vec3</c> exists instead of
    /// <c>UnityEngine.Vector3</c>, and why <see cref="HorrorGame.Core.Session.IRandomSource"/>
    /// exists instead of <c>UnityEngine.Random</c>. A naive text search flags those
    /// explanations as violations, so prose has to be removed before scanning.
    /// </para>
    /// </summary>
    public static class CSharpSource
    {
        /// <summary>
        /// Returns <paramref name="source"/> with comments, string literals and
        /// character literals blanked out, so only real code remains.
        /// <para>
        /// Raw string literals are not handled — the solution pins
        /// <c>LangVersion</c> to 9.0 for Unity compatibility, so they cannot appear.
        /// </para>
        /// </summary>
        public static string StripCommentsAndLiterals(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var sb = new StringBuilder(source.Length);

            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var hasNext = i + 1 < source.Length;

                // Block comment — also covers /** ... */ documentation.
                if (c == '/' && hasNext && source[i + 1] == '*')
                {
                    var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? source.Length - 1 : end + 1;
                    sb.Append(' ');
                    continue;
                }

                // Line comment — also covers /// documentation, which is where
                // most of the false positives live.
                if (c == '/' && hasNext && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        i++;
                    }

                    sb.Append('\n');
                    continue;
                }

                // Verbatim string: @"..." where "" is an escaped quote.
                if (c == '@' && hasNext && source[i + 1] == '"')
                {
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"')
                        {
                            if (i + 1 < source.Length && source[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }

                            break;
                        }

                        i++;
                    }

                    sb.Append("\"\"");
                    continue;
                }

                // Regular string literal, honouring backslash escapes.
                if (c == '"')
                {
                    i++;
                    while (i < source.Length && source[i] != '"')
                    {
                        if (source[i] == '\\')
                        {
                            i++;
                        }

                        i++;
                    }

                    sb.Append("\"\"");
                    continue;
                }

                // Character literal.
                if (c == '\'')
                {
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    {
                        if (source[i] == '\\')
                        {
                            i++;
                        }

                        i++;
                    }

                    sb.Append("''");
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
