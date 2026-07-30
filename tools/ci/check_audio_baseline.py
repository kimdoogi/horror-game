#!/usr/bin/env python3
"""Gate `tools/audio/verify_audio.py` against a written-down list of known defects.

    usage:  python3 tools/ci/check_audio_baseline.py --audit audit.json
            ... --baseline tools/ci/audio_baseline.json    (default)
            ... --summary $GITHUB_STEP_SUMMARY             (optional markdown out)

Standard library only, on purpose: this runs after the audit, and it must still be
able to report a verdict when numpy or scipy are the thing that broke.

WHY THIS EXISTS INSTEAD OF JUST USING THE AUDIT'S EXIT CODE
═══════════════════════════════════════════════════════════

`verify_audio.py` exits non-zero when any BLOCKING defect is present, and one is
present right now: F-002 in docs/BALANCE-FINDINGS.md, the Listener's clarity table
disagreeing with the measured audio through a wall. So the two obvious wirings are
both wrong:

  * Fail the build on it. The check is red on `main` from the first day and stays
    red until a designer decides between F-002's three options — which is a design
    decision, not a CI task, and may reasonably take weeks. A permanently red gate
    is not a gate: people learn to ignore it, and the *next* blocking defect, the
    one that is a genuine regression, ships behind the same red X.

  * Ignore the exit code (`|| true`). Then the §12 material separation and the
    channel-policy check — the ones that decide whether the Listener role produces
    information at all — stop being enforced entirely, and nothing tells anyone.
    docs/ARCHITECTURE.md §6 forbids exactly this move: a finding gets encoded and
    pinned, not quietly dropped.

So the defect is written down by fingerprint in audio_baseline.json with its finding
id, and the gate is two-sided:

  * a BLOCKING defect that is not in the baseline fails the build — that is a
    regression, and it is the case the job exists for;
  * a baselined defect that has stopped reproducing *also* fails the build, because
    docs/BALANCE-FINDINGS.md says "Every finding is pinned by a test, so a later
    edit that changes the answer fails the build instead of passing silently". If
    F-002 is fixed, the write-up and the baseline have to move in the same commit
    as the fix, while the reasoning is still in someone's head.

Warnings are reported, never gated. F-003's 1.396x is a WARNING and it sits
0.004 from its threshold; gating it would make the build a coin toss on filter
rounding, and it is already pinned by the audit's own output and its write-up.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Any, Dict, List, Tuple

BLOCKING = "BLOCKING"
WARNING = "WARNING"

#: (check, target). The detail string is deliberately excluded — see `_fingerprint`
#: in audio_baseline.json.
Fingerprint = Tuple[str, str]

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_BASELINE = os.path.join(REPO, "tools", "ci", "audio_baseline.json")


def fingerprint(entry: Dict[str, Any]) -> Fingerprint:
    return str(entry.get("check", "")), str(entry.get("target", ""))


def load(path: str, what: str) -> Dict[str, Any]:
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return json.load(handle)
    except FileNotFoundError:
        sys.exit(f"FAIL: {what} not found at {path}")
    except json.JSONDecodeError as exc:
        sys.exit(f"FAIL: {what} at {path} is not valid JSON: {exc}")


def main(argv: List[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--audit", required=True,
                    help="JSON written by verify_audio.py --json")
    ap.add_argument("--baseline", default=DEFAULT_BASELINE,
                    help="known-defect list (default: tools/ci/audio_baseline.json)")
    ap.add_argument("--summary", default=os.environ.get("GITHUB_STEP_SUMMARY"),
                    help="append a markdown verdict here (default: $GITHUB_STEP_SUMMARY)")
    args = ap.parse_args(argv)

    audit = load(args.audit, "audit json")
    baseline = load(args.baseline, "baseline")

    accepted = baseline.get("accepted_blocking", [])
    unnamed = [a for a in accepted if not str(a.get("finding", "")).strip()]
    if unnamed:
        # The baseline's own rule. Without it the file drifts into a mute button.
        for entry in unnamed:
            print(f"  baseline entry {fingerprint(entry)} names no finding id")
        return report(args.summary, False, [], [], [],
                      f"{len(unnamed)} baseline entr(y/ies) name no "
                      f"docs/BALANCE-FINDINGS.md id")

    defects = audit.get("defects", [])
    blocking = {fingerprint(d): d for d in defects if d.get("severity") == BLOCKING}
    warnings = [d for d in defects if d.get("severity") == WARNING]
    expected = {fingerprint(a): a for a in accepted}

    unexpected = [blocking[f] for f in blocking if f not in expected]
    resolved = [expected[f] for f in expected if f not in blocking]
    held = [expected[f] for f in expected if f in blocking]

    print("=" * 92)
    print("AUDIO GATE — blocking defects against tools/ci/audio_baseline.json")
    print("=" * 92)
    print(f"  audit result:        {audit.get('result', '?')}")
    print(f"  blocking defects:    {len(blocking)}")
    print(f"  accepted (baseline): {len(accepted)}")
    print(f"  warnings:            {len(warnings)}")
    print()

    for entry in held:
        print(f"  KNOWN     [{entry['check']}] {entry['target']}  → {entry['finding']}")
        print(f"            still reproducing, still awaiting a decision in "
              f"docs/BALANCE-FINDINGS.md")

    for defect in warnings:
        print(f"  WARNING   [{defect.get('check')}] {defect.get('target')}")
        print(f"            {defect.get('detail', '')}")

    for defect in unexpected:
        print()
        print(f"  REGRESSION  [{defect.get('check')}] {defect.get('target')}")
        print(f"              {defect.get('detail', '')}")
        print("              This blocking defect is not in the baseline. Either it is a")
        print("              real regression — fix it — or it is a genuine design")
        print("              contradiction, in which case write it up in")
        print("              docs/BALANCE-FINDINGS.md and add it here with its id.")

    for entry in resolved:
        print()
        print(f"  RESOLVED    [{entry['check']}] {entry['target']}  → {entry['finding']}")
        print("              This defect no longer reproduces, so the finding it pins is")
        print("              answered. Update docs/BALANCE-FINDINGS.md and delete this")
        print("              baseline entry in the same commit as the change that fixed")
        print("              it. Leaving it listed would silently accept a defect that")
        print("              no longer exists, and the next real one would look known.")
        print(f"              Expected to resolve when: {entry.get('resolves_when', 'unstated')}")

    ok = not unexpected and not resolved
    reason = ""
    if unexpected:
        reason = f"{len(unexpected)} unbaselined blocking defect(s)"
    elif resolved:
        reason = f"{len(resolved)} baselined defect(s) no longer reproduce"

    print()
    print(f"  RESULT: {'PASS' if ok else 'FAIL'}"
          + (f" — {reason}" if reason else ""))
    return report(args.summary, ok, unexpected, resolved, held, reason)


def report(summary_path: str | None, ok: bool, unexpected: List[Dict[str, Any]],
           resolved: List[Dict[str, Any]], held: List[Dict[str, Any]],
           reason: str) -> int:
    """Mirrors the verdict into the run summary so the numbers are visible without
    opening a log, then returns the process exit code."""
    if summary_path:
        lines = ["### §12 asset audit", ""]
        lines.append(f"**{'PASS' if ok else 'FAIL'}**" + (f" — {reason}" if reason else ""))
        lines.append("")
        for entry in held:
            lines.append(f"- known, accepted: `{entry['check']}` / {entry['target']} "
                         f"→ **{entry['finding']}** in `docs/BALANCE-FINDINGS.md`")
        for defect in unexpected:
            lines.append(f"- **new blocking defect**: `{defect.get('check')}` / "
                         f"{defect.get('target')} — {defect.get('detail', '')}")
        for entry in resolved:
            lines.append(f"- **{entry['finding']} no longer reproduces** — update "
                         f"`docs/BALANCE-FINDINGS.md` and drop it from "
                         f"`tools/ci/audio_baseline.json`")
        try:
            with open(summary_path, "a", encoding="utf-8") as handle:
                handle.write("\n".join(lines) + "\n")
        except OSError as exc:
            print(f"  (could not write the run summary: {exc})")

    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
