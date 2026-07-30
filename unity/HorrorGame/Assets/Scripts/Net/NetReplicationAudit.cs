#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HorrorGame.Net.Host;
using Mirror;

namespace HorrorGame.Net
{
    /// <summary>
    /// Walks every surface of this assembly that can put bytes on the wire and
    /// fails if any of them could carry §13's answers.
    /// <para>
    /// This exists because the rule it enforces is one that reads as obviously
    /// satisfied and breaks silently. §13: "단서 내용 · 목표물 위치 — 호스트만 보유.
    /// 클라이언트에 보내면 메모리에서 읽힌다." A single <c>[SyncVar] SiteLabel</c>
    /// added months from now, by somebody solving a legitimate UI problem, would
    /// compile, run, look correct on screen, and quietly delete §03 — because the
    /// answer would be sitting in every client's memory and §03's entire structure
    /// ("그 자리에서 보고, 기억해서, 말로 전달해야 한다") assumes it is not.
    /// </para>
    /// <para>
    /// <b>What is checked.</b> Every <c>[SyncVar]</c> field, every sync collection's
    /// element types, every parameter of every <c>[Command]</c>, <c>[ClientRpc]</c>
    /// and <c>[TargetRpc]</c>, and every field of every <c>NetworkMessage</c>. Each
    /// one is walked transitively — a struct that holds a struct that holds a
    /// <c>ClueGlyph</c> is caught at the same depth as the glyph itself.
    /// </para>
    /// <para>
    /// <b>What is forbidden.</b> Anything in <c>HorrorGame.Core.Clues</c>, and
    /// anything marked <see cref="HostOnlyAttribute"/>. The first set is where the
    /// answers live; the second is where this assembly keeps them.
    /// </para>
    /// <para>
    /// Written as a runtime scan rather than a Roslyn analyser so it can run inside
    /// the PlayMode suite alongside the behavioural test that proves the same thing
    /// dynamically. Two proofs of one rule is the right number for the rule the game
    /// rests on.
    /// </para>
    /// </summary>
    public static class NetReplicationAudit
    {
        /// <summary>Namespace whose types encode §03's answers. Nothing from it may cross the wire.</summary>
        public const string ForbiddenNamespace = "HorrorGame.Core.Clues";

        private const BindingFlags AllMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Runs the audit over the Net assembly. An empty list is the passing result.
        /// Each violation is one sentence naming the member and the type it would
        /// have carried.
        /// </summary>
        public static IReadOnlyList<string> Scan() => Scan(typeof(NetReplicationAudit).Assembly);

        /// <summary>Runs the audit over an arbitrary assembly. Tests use this to check the checker.</summary>
        public static IReadOnlyList<string> Scan(Assembly assembly)
        {
            var violations = new List<string>();

            if (assembly == null)
            {
                return violations;
            }

            foreach (var type in assembly.GetTypes())
            {
                ScanSyncVars(type, violations);
                ScanSyncObjects(type, violations);
                ScanRemoteCalls(type, violations);
                ScanNetworkMessages(type, violations);
            }

            return violations;
        }

        private static void ScanSyncVars(Type type, List<string> violations)
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                if (field.GetCustomAttribute<SyncVarAttribute>() == null)
                {
                    continue;
                }

                if (TryFindForbidden(field.FieldType, out var offender))
                {
                    violations.Add(
                        "[SyncVar] " + type.FullName + "." + field.Name
                        + " would replicate " + offender + ", which §13 keeps on the host.");
                }
            }
        }

        private static void ScanSyncObjects(Type type, List<string> violations)
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                if (!typeof(SyncObject).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                foreach (var argument in CollectGenericArguments(field.FieldType))
                {
                    if (TryFindForbidden(argument, out var offender))
                    {
                        violations.Add(
                            "Sync collection " + type.FullName + "." + field.Name
                            + " would replicate " + offender + ", which §13 keeps on the host.");
                    }
                }
            }
        }

        private static void ScanRemoteCalls(Type type, List<string> violations)
        {
            foreach (var method in type.GetMethods(AllMembers))
            {
                var kind = RemoteCallKind(method);
                if (kind == null)
                {
                    continue;
                }

                foreach (var parameter in method.GetParameters())
                {
                    // The target of a TargetRpc and the injected sender of a Command
                    // are addresses, not payloads.
                    if (typeof(NetworkConnection).IsAssignableFrom(parameter.ParameterType))
                    {
                        continue;
                    }

                    if (TryFindForbidden(parameter.ParameterType, out var offender))
                    {
                        violations.Add(
                            kind + " " + type.FullName + "." + method.Name
                            + " would carry " + offender + " in parameter '" + parameter.Name
                            + "', which §13 keeps on the host.");
                    }
                }
            }
        }

        private static void ScanNetworkMessages(Type type, List<string> violations)
        {
            if (!typeof(NetworkMessage).IsAssignableFrom(type) || type.IsInterface)
            {
                return;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (TryFindForbidden(field.FieldType, out var offender))
                {
                    violations.Add(
                        "NetworkMessage " + type.FullName + "." + field.Name
                        + " would carry " + offender + ", which §13 keeps on the host.");
                }
            }
        }

        private static string? RemoteCallKind(MethodInfo method)
        {
            if (method.GetCustomAttribute<CommandAttribute>() != null)
            {
                return "[Command]";
            }

            if (method.GetCustomAttribute<ClientRpcAttribute>() != null)
            {
                return "[ClientRpc]";
            }

            return method.GetCustomAttribute<TargetRpcAttribute>() != null ? "[TargetRpc]" : null;
        }

        private static IEnumerable<Type> CollectGenericArguments(Type type)
        {
            var current = type;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType)
                {
                    foreach (var argument in current.GetGenericArguments())
                    {
                        yield return argument;
                    }
                }

                current = current.BaseType;
            }
        }

        /// <summary>
        /// Whether a type, or anything reachable from its fields, is one of the
        /// forbidden ones.
        /// <para>
        /// Transitive because the interesting failure is not <c>[SyncVar] ClueGlyph</c>
        /// — nobody writes that by accident — it is a helpful little struct bundling
        /// "what the player just read" that happens to have a <c>SiteLabel</c> inside
        /// it three levels down.
        /// </para>
        /// </summary>
        private static bool TryFindForbidden(Type type, out string offender)
        {
            var seen = new HashSet<Type>();
            return TryFindForbidden(type, seen, out offender);
        }

        private static bool TryFindForbidden(Type type, HashSet<Type> seen, out string offender)
        {
            offender = string.Empty;

            if (type == null || !seen.Add(type))
            {
                return false;
            }

            if (type.IsByRef || type.IsPointer)
            {
                return TryFindForbidden(type.GetElementType()!, seen, out offender);
            }

            if (type.IsArray)
            {
                return TryFindForbidden(type.GetElementType()!, seen, out offender);
            }

            if (IsForbidden(type))
            {
                offender = type.FullName ?? type.Name;
                return true;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    if (TryFindForbidden(argument, seen, out offender))
                    {
                        return true;
                    }
                }
            }

            // Primitives, strings and enums are the wire's own alphabet and have no
            // fields worth walking. Stopping here also keeps the walk out of the BCL.
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return false;
            }

            if (type.Namespace != null
                && (type.Namespace.StartsWith("System", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)
                    || type.Namespace.StartsWith("Mirror", StringComparison.Ordinal)))
            {
                return false;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (TryFindForbidden(field.FieldType, seen, out offender))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsForbidden(Type type)
        {
            if (type.Namespace != null
                && type.Namespace.StartsWith(ForbiddenNamespace, StringComparison.Ordinal))
            {
                return true;
            }

            return type.GetCustomAttribute<HostOnlyAttribute>(true) != null;
        }
    }
}
