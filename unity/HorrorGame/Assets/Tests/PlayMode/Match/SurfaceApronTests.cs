// SurfaceApronTests — DELETED 2026-08-03, with §01's 지상 itself.
//
// This file held two PlayMode tests:
//
//   The_surface_boundary_is_painted_at_the_radius_the_rule_measures
//   Crossing_the_line_swells_the_threshold_on_the_way_in_and_dims_it_on_the_way_out
//
// Both asserted that MatchDirector.Apron was not null, and both now fail with "§01's
// 지상 was not painted at all". That message is true and the behaviour behind it is
// gone on purpose, so the tests are removed rather than silenced.
//
// WHY, measured rather than assumed:
//
//   · MatchDirector.PlaceVehicle is the only caller of SurfaceApron.Build, and it is on
//     the co-operative branch of BeginMatch. The race branch returns before it — see
//     MatchDirector.cs ~line 420, "§01's race, or the co-operative recovery match it grew
//     out of". So on §01's race the apron is never built and Apron is null by design.
//   · Map_FirstSketch_Solo.unity is the only scene in the project with a MatchDirector and
//     it serialises _raceMode: 1 (read out of the scene file, not out of the inspector).
//     Bootstrap.unity and Map_FirstSketch.unity have no director at all. There is no
//     shipped configuration in which PlaceVehicle runs.
//   · MatchMap.HasSurface is false on this building — the 출입구 marker is 23.45 m BELOW
//     every player start, because §01 moved it to §02's finish at the middle of B8 — so
//     IsOnSurface can never return true and there is no boundary for paint to be the edge
//     of. Painting a 15 m "safe zone" ring around the finish line would be a picture of a
//     rule that does not exist, which is worse than the invisible boundary the apron
//     originally replaced.
//
// WHAT COVERAGE IS LOST, and who still needs it:
//
//   · SurfaceApron.cs (≈900 lines), MatchDirector.PlaceVehicle and
//     SurfaceVehicleInteractable now have no PlayMode test. Nothing needs one: all three
//     are unreachable on every scene the game ships. They are dead code awaiting the
//     deletion DESCENT-PIVOT §3 already schedules ("deleting it afterwards is a mechanical
//     change nobody has to be brave about"), and a passing test over unreachable code is
//     the exact failure this repo keeps rediscovering.
//   · The QUESTION these tests asked is not dead and is not covered anywhere: "a rule the
//     player can see". §01's race has two such rules — the rim of B1 you start on and the
//     middle of B8 you win at — and neither has a test that anything is drawn there.
//     That gap belongs to §02 and RaceDirector, not to an apron, and it is named here so
//     it is not lost with the file.
//
// ALSO AFFECTED, in a file this agent does not own:
//   Scripts/Gameplay/Match/Editor/SoloMatchLoopTests.cs lines 174-182 still Require() the
//   apron and lines 390-420 still Require() §08's shop at the 차량. That harness is an
//   editor menu verification, not part of the 105-test PlayMode suite, but it fails on the
//   race map for the same reason and the same removal applies to it.
//
// This file is left as a comment so the reason travels with the diff. Remove it, its
// .meta, and SurfaceApron.cs together when the co-operative branch of MatchDirector goes.
