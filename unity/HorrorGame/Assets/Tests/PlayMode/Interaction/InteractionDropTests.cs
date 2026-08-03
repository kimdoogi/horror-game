// InteractionDropTests — DELETED 2026-08-03, with the carry it was about.
// DESCENT-PIVOT §7 step 7 「상점/전리품/단서 제거」.
//
// This file held one PlayMode test (a second, over §08's shop opening at the 차량, had
// already been removed when §01's 지상 went):
//
//   Dropping_a_piece_with_the_key_puts_it_on_the_floor_and_leaves_it_pickable
//
// It was a real bug found by playing — a dropped 궤짝 hanging in the air — and it was
// checked the way it was found: a player standing in the world, a crosshair ray, a real
// key event, and a measurement that the piece reached a surface, was not inside the
// geometry when it got there, and could be picked up again.
//
// WHY IT GOES, measured rather than assumed:
//
//   · The subject is deleted. Gameplay/Interaction/OversizeLootInteractable.cs,
//     LootPropInteractable.cs, HeldPropModels.cs and Core/Economy/ are all removed in this
//     round, and so is the thing under test: Gameplay/Interaction/DropPlacement.cs, whose
//     only callers were those two components.
//   · Nothing in a race is carried, so nothing in a race is dropped. §04's 「전원이 가진
//     것」 is 손전등 · 문 · 질주 · 귀 — four things, none of which occupies a hand you could
//     put down. §02's 목표물 회수 was replaced by 「B8 중심에 먼저 닿기」, which you arrive
//     at rather than pick up.
//   · The file's own remark predicted this and asked for one thing in return: "the
//     coverage has to be re-pointed at whatever the runner actually carries, not dropped".
//     The honest answer on 2026-08-03 is that the runner carries NOTHING. The successor it
//     hoped for — F-006 §5c, battery cells found on the floor — went the same night: the
//     light economy is deleted, the torch simply works, and there are no cells.
//
// WHAT COVERAGE IS LOST, and who still needs it:
//
//   · DropPlacement's surface search (down-cast, penetration push-out, fallback) has no
//     test. It also has no caller and no file. Nothing needs it.
//   · The crosshair-and-key half of what this file measured is NOT lost. It moved, with
//     InteractionPickupTests, to PlayMode/Interaction/InteractionKeyPathTests.cs, which
//     drives the same PlayerInteractor path against §12-B's 문 — the one interactable the
//     race has left.
//   · If a runner is ever given something to hold again, this test is the shape to bring
//     back: stand in the real building, aim with the real ray, press the real key, and
//     then MEASURE where the object ended up. Every cheaper version of it passed while the
//     shipped game was broken.
//
// This file is left as a comment so the reason travels with the diff. Remove it and its
// .meta together whenever the tree next has a reason to touch this folder.
