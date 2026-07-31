# Overnight — 2026-08-01

What changed while you slept, measured on this machine between 05:55 and 06:40.
Everything below has a command under it. Nothing here is an estimate.

---

## The headline: the 주자 테스트 did not move

This round existed to land the 주자 테스트 inside §12's 5–7/10 band. **It is still
10/10 TooEasy**, exactly where it was yesterday.

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- map
```

```
§12 주자 테스트 — 요양원 지하 5층: 10/10 (100%), TooEasy
§12 실전 검증, every place rather than the ten §12 samples: 164/164 escapable (100%), against §12's 50%~70% band
시야 차단 지점 간격 (§12 수치 규칙 15 m~25 m): 79 corners, nearest-neighbour 2.5 m~10 m, mean 4.1 m, 0 inside the band
```

What the corner-density pass actually delivered was the **measurement**, not the fix.
§12's 시야 차단 지점 간격 is now `MapValidator`'s 17th rule (`66ce930`), it is a good
rule, and it names the cause precisely: 79 bends, mean spacing 4.1 m, **zero** inside
the 15–25 m band. The map was never changed to satisfy it.

**And that has a cost that was not there yesterday.** The map now fails its own
checklist, so the generator refuses to write it — see **[B-007](BLOCKERS.md#b-007)**.
`HorrorGame ▸ Scene Gen ▸ Generate First Map` exits 1 and writes nothing. The
committed scene still works; you just cannot re-roll it.

That is the one thing that is **worse** than it was last night.

---

## What got better, with the numbers

| | Yesterday | Tonight |
|---|---|---|
| Core rules tests | 448 | **451** |
| EditMode | 70 | **71** |
| PlayMode | 42 | **53** |
| Total green | 560 | **575** |
| §12 checklist | 16 rules, all pass | **17 rules, 16 pass** — the 17th fails, correctly |

The +14 tests are the view-motion work (`PlayerViewMotion`, `ViewMotionTuning`, the
settings slider) and the three that pin the new §12 rule. All 575 pass.

Everything else held at its previous value — no regressions:

- `dotnet build core/HorrorGame.sln -c Release` → `경고 0개, 오류 0개` (B-006 stays closed)
- Unity compile → `0` errors
- `MonsterChaseTests` → `total=4 passed=4 failed=0`
- NavMesh → `complete 1830 (100.0 %, need 98 %) · islands 1 · monster reach 19/19`
- Asset imports → `166 inspected, 0 failing` audio · `86 inspected, 0 failing` model
- Monster visibility → **8/8 frames pass**; at 15 m, contrast 0.0592 against a 0.015 floor
- Solo loop → `PASS — §01's loop ran end to end.`
- Standalone macOS arm64 Release, IL2CPP → `exit code : 0`, launches, `[Match] seed 20260731 …`, **0 exceptions**

---

## What is still broken

**F-006 — matches are 7.2 minutes against §01's 25–35.** Unmoved, to the decimal.

```bash
dotnet run -c Release --project core/HorrorGame.Sim -- run --matches 500 --seed 1
```

```
median                                             7.2 min
p10 / p90                                          4.2 min / 32.4 min
inside the window                                  15.8%
ended with every light dead                        40.6%
mean tier at end (0=초저녁 … 4=동트기 전)                 1.12
reached 심야 or later (tier 2, 16 min)               33.6%
reached 새벽 or later (tier 3, 24 min)               17.4%
reached 동트기 전 (tier 4, 32 min)                     13.0%
```

This is still the highest-priority open question and still blocks §14's question 3.
The in-game guidance overlay says so in red, which is the right behaviour.

**A doorway in zone C still opens onto the sky.** Four storeys underground,
`zone_Zone_C_B4_Gravel.png` shows a night skybox with a visible horizon through a
doorway, dead centre of frame. Already on the books —
[STATUS.md §4.4](STATUS.md) and [ART.md §7.4](ART.md) — and **unchanged**: the sky
region is byte-identical across renders from 01:56, 02:19 and 06:19 tonight. Nobody
has touched it. It is a level-seal defect, not a crash, and it is cheap.

**TESTING.md was wrong about the standalone.** It says the player "boots straight into
`Map_FirstSketch_Solo`". Scene 0 is now `Bootstrap` — the front end took that slot. The
menu is properly wired (시작 → `GameShell.LoadMatchRoutine`), so this is a doc fix, and
it is now corrected.

---

## Three things I would do next, in order

**1 · Fix the corner density in `FirstMapSketch`. One change closes two entries.**
B-007 and F-007 are the same defect measured twice. §12 wants 3–4 sight-break
opportunities per 60 m sprint; the map offers a bend every 4.1 m, which is cover so
continuous that aggro is never a threat. Thin the bends until the nearest-neighbour
spacing lands in 15–25 m, then re-run `horrorsim map` — the 주자 테스트 grade and the
17th rule both come from the same geometry and should move together. This unblocks map
authoring, which is currently frozen.

**2 · Then F-006, and only then.** Do not tune match length against the current map.
Corner density changes chase duration, chase duration changes how long a descent takes,
and `chases per match 5.52 · mean aggro seconds 22.73` are inputs to the 7.2-minute
median. Re-measure after step 1 and the recommendation in
[BALANCE-FINDINGS F-006](BALANCE-FINDINGS.md#f-006) may need re-deriving.

**3 · Seal zone C, and audit the other four while you are there.** A skybox visible
from B4 breaks the premise harder than any balance number, it has survived three
passes untouched, and it is in every screenshot of that zone — including anything that
goes near the store page. It is the cheapest item on this list by a wide margin.

---

## Where the numbers live

Every command above, with its full output, is in
**[STATUS.md](STATUS.md) §1**. The reproduction block is
[STATUS.md §8](STATUS.md#8--reproducing-this-document). Blockers are
[BLOCKERS.md](BLOCKERS.md); balance is [BALANCE-FINDINGS.md](BALANCE-FINDINGS.md).
