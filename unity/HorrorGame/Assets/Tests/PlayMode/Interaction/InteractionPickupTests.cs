// InteractionPickupTests — RE-POINTED 2026-08-03, not simply deleted.
// DESCENT-PIVOT §7 step 7 「상점/전리품/단서 제거」.
//
// This file held two PlayMode tests:
//
//   Pressing_the_interact_key_at_a_loot_prop_banks_it_with_section_08_weight
//   The_key_does_nothing_when_the_crosshair_is_on_nothing
//
// WHY THEY COULD NOT STAY AS THEY WERE
//
//   Every type the first test names is gone in the same round: HorrorGame.Core.Economy
//   (Inventory, LootId, LootCatalogue), Gameplay/Interaction/LootPropInteractable.cs, and
//   PlayerLoadout.Inventory. §08 has no currency, so a piece of 전리품 has nothing to be
//   worth; §12 still marks 152 막힌 길 and there is now nothing to stand on them. These
//   are compile failures, not stale assertions — there is no version of this test that
//   picks up a thing.
//
// WHY THE COVERAGE DID NOT GO WITH THEM
//
//   What these two measured was never §08's weight table. It was the path from the
//   crosshair ray, through the reach gate and the prompt, to a real Input System key
//   event changing the world — the path this project once shipped completely broken with
//   575 green tests behind it, which is the reason the file exists at all. That path is
//   MORE central to the race than it was to the co-op game: PlayerInteractor.Probe now
//   says outright that "the door is the only interactable", and §12-B's 문 is the runner's
//   only lever on the building and on the nineteen people behind them.
//
//   So both tests were rewritten against a door rather than retired:
//
//     PlayMode/Interaction/InteractionKeyPathTests.cs
//       Holding_the_interact_key_at_a_door_shuts_it_and_blocks_the_corridor
//       The_key_does_nothing_when_the_crosshair_is_on_nothing
//
//   The second is the same test with a different set of things it must not disturb. The
//   first keeps every assertion that was about the PATH — focus, prompt visible, prompt
//   names the key, prompt actually drawn, real key event — and replaces the one that was
//   about §08 (weight landing in the inventory) with the one that is about §12-B: the
//   blocking collider is off while the door is open and on once it is shut. A DoorState
//   that flipped without the collider following would be a door nobody can be stopped by,
//   which is the same class of defect as a rule that is right and a game that ignores it.
//
// WHAT IS GENUINELY LOST, named so it is not rediscovered as a surprise
//
//   · Nothing now covers a HELD-then-RELEASED interaction being abandoned
//     (DoorState.AbandonShut via PlayerInteractor's _holding path). §12-B makes losing
//     1.1초 of effort by letting go the reason shutting a door is a commitment. The core
//     suite covers AbandonShut; the key path to it is untested. It is one more assert in
//     InteractionKeyPathTests and is left as a named gap rather than added blind.
//
// This file is left as a comment so the reason travels with the diff. Remove it and its
// .meta together whenever the tree next has a reason to touch this folder.
