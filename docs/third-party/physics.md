# Physics track: sources and provenance

Code: `src/Lifestone.Data/Physics/`, `src/Lifestone.Client/Movement/`. Tests: `tests/Lifestone.Data.Tests/Physics/`,
`tests/Lifestone.Client.Tests/Movement/`. Design and decisions: [../physics.md](../physics.md).

## Sources

| Source | Version | Licence | How it is used |
|---|---|---|---|
| ACEmulator/ACE | v1.78.4816, `95ef426d` (full SHA in server/versions.json) | AGPL-3.0 | **Ported**, file by file as listed below, from `Source/ACE.Server/Physics/`. Each ported file carries a `Ported from` header naming its sources and its changes. Where ACE differs from the retail client, Lifestone follows retail; every such fix is listed under "ACE deviations" and the behavioural ones have a test-only `PhysicsCompat` switch ([../physics.md](../physics.md) §2). |
| ACE build under `server/runtime/ace` (same commit) | v1.78.4816, `95ef426d` | AGPL-3.0 | **Linked, not ported**, by `tools/ace-compare` (world walk W7), a local helper outside the solution: it holds no ACE source and only calls ACE's physics to compare it with ours |
| Retail client decompile (Mag-nus/acclient-AI-RE) | 11.4186 (PDB-named) | reference only | Behaviour was read in the per-module files under `2013-09 11.4186/acclient-src/src/` and is cited by function name and address next to the code. **No decompiled code is in the repository.** |
| `symbol_transplant_11.6096.tsv` (same repository, pinned in `cache/research/` by the lead, owner decision D4) | 11.6096 | reference only | The 11.6096 address of each cited function. Where the table has none, the code says `TODO(physics): 11.6096`. |

## Files

Ported files (each carries a header), all from ACEmulator/ACE @95ef426d (AGPL-3.0), paths under
`Source/ACE.Server/Physics/` unless stated:

| Lifestone file | ACE source |
|---|---|
| `src/Lifestone.Data/Physics/PhysicsGlobals.cs` | `PhysicsGlobals.cs` (partly: the constants retail was read to use) |
| `src/Lifestone.Data/Physics/PhysicsPositions.cs` | `Common/Position.cs` (GetOffset, LocalToGlobal, LocalToLocal, Distance), `Animation/AFrame.cs` (GlobalToLocal) |
| `src/Lifestone.Data/Physics/Primitives/PhysSphere.cs` | `Sphere.cs` |
| `src/Lifestone.Data/Physics/Primitives/PhysCylSphere.cs` | `CylSphere.cs` |
| `src/Lifestone.Data/Physics/Primitives/PhysPolygon.cs` | `Polygon.cs` |
| `src/Lifestone.Data/Physics/Primitives/BBox.cs` | `Collision/BBox.cs` |
| `src/Lifestone.Data/Physics/Primitives/Ray.cs` | `Ray.cs` |
| `src/Lifestone.Data/Physics/Primitives/PlaneMath.cs` | `Extensions/PlaneExtensions.cs` (partly: with `snap_to_plane` since PH3) |
| `src/Lifestone.Data/Physics/Primitives/VectorMath.cs` | `Extensions/Vector3Extensions.cs`, `Common/Vector.cs` (partly) |
| `src/Lifestone.Data/Physics/Transitions/TransitionTypes.cs` | `Transition.cs` (TransitionState), `ObjectInfo.cs` (ObjectInfoState), `SpherePath.cs` (InsertType) |
| `src/Lifestone.Data/Physics/Transitions/SpherePath.cs` | `SpherePath.cs` |
| `src/Lifestone.Data/Physics/Transitions/CollisionInfo.cs` | `Collision/CollisionInfo.cs` |
| `src/Lifestone.Data/Physics/Transitions/ObjectInfo.cs` | `ObjectInfo.cs` |
| `src/Lifestone.Data/Physics/Transitions/Transition.cs` | `Transition.cs` |
| `src/Lifestone.Data/Physics/Transitions/CellArray.cs` | `Common/CellArray.cs` |
| `src/Lifestone.Data/Physics/Bsp/PhysicsBspNode.cs` | `BSP/BSPNode.cs`, `BSP/BSPLeaf.cs` (the node structure and the physics queries) |
| `src/Lifestone.Data/Physics/Bsp/PhysicsBspTree.cs` | `BSP/BSPTree.cs` (the physics queries) |
| `src/Lifestone.Data/World/Cells/CellBspPhysics.cs` | `BSP/BSPNode.cs` (`sphere_intersects_cell_bsp`, `box_intersects_cell_bsp`) |
| `src/Lifestone.Data/Physics/World/PhysCell.cs` | `Common/ObjCell.cs` (partly: `FindObjCollisions`, `get_water_depth`, `check_entry_restrictions`) |
| `src/Lifestone.Data/Physics/World/PhysLandCell.cs` | `Common/LandCell.cs`, `Common/SortCell.cs` |
| `src/Lifestone.Data/Physics/World/PhysEnvCell.cs` | `Common/EnvCell.cs` |
| `src/Lifestone.Data/Physics/World/PhysBuilding.cs` | `Common/BuildingObj.cs`, `Common/BldPortal.cs` |
| `src/Lifestone.Data/Physics/World/PhysStaticObject.cs` | `PhysicsObj.cs` (partly: `FindObjCollisions`), `PartArray.cs` (`FindObjCollisions`), `PhysicsPart.cs` (`find_obj_collisions`), `Common/GfxObj.cs` (`FindObjCollisions`) |
| `src/Lifestone.Data/Physics/World/CellLists.cs` | `Common/ObjCell.cs` (`find_cell_list`), `Common/LandCell.cs` (`add_all_outside_cells`, `add_cell_block`, `add_outside_cell`, `check_add_cell_boundary`) |
| `src/Lifestone.Data/Physics/World/LandblockCollision.cs` | `Common/LandblockStruct.cs` (partly: `CalcWater`, `CalcCellWater`, `calc_water_depth`, the SurfChar table) |
| `src/Lifestone.Data/Physics/World/LandblockCollisionBuilder.cs` | `Common/Landblock.cs` (partly: `init_static_objs`, `init_buildings`, `PostInit`), `PhysicsObj.cs` (partly: `calc_cross_cells_static`, `add_shadows_to_cells`) |
| `tests/Lifestone.Data.Tests/Physics/SphereTests.cs` (test-only) | `Source/ACE.Server.Tests/Physics/SphereTests.cs` |

Lifestone's own code, citing retail only: `PhysicsCompat.cs`, `PhysicsClock.cs`, `PhysicsState.cs`, `IGroundProbe.cs`,
`RetailFrames.cs` (the `Frame`, `Position` and `Plane` tests the command interpreter uses, PH7),
`Transitions/TransitionPool.cs`, `Transitions/RetailHeadings.cs` (the `Frame` heading functions), `Geometry/*` (decoded
from DatReaderWriter's types, following retail's unpack functions), `World/PhysicsWorld.cs` (with the PH8 ground probe),
`World/IPhysicsCellSource.cs`, `World/ViewerSweepResult.cs`, everything under `Objects/` (`PhysicsObj` re-expressed from
retail `CPhysicsObj`, PH5-PH6; ACE's `PhysicsObj.cs` was not used), and everything under `src/Lifestone.Client/Movement/`:
the player controller re-expressed from retail `CommandInterpreter`, `ACCmdInterp`, `ClientCombatSystem`'s jump bar and
the player branch of `SmartBox::HandleReceivedPosition` (PH7; ACE's `CommandInterpreter.cs` was not used, beyond the
1.875 s it gives decision-table row 7 as ACE's value), and the routes and the movement
audit (PH9), which are Lifestone's test harness with no retail counterpart. The functions cited are listed with both
addresses in [../physics.md](../physics.md) §12-§14 and next to the code. `PhysicsGlobals.cs` (ported)
also carries ACE's values of three constants retail initialises in data: `DefaultFriction`, `DefaultElasticity`,
`DummySphereRadius`. The test-only
`RawPhysicsBspReader.cs` is written from the retail unpack functions and grown from the models track's `RawModelReader`.

## ACE deviations (fixed here)

The behavioural rows are the decision table of [../physics.md](../physics.md) §2 (each with a `PhysicsCompat` switch).
The fixes below have no switch; each was read in the 11.4186 function named, and most have a test (the others name the
step whose objects reach the code).

| # | ACE @95ef426d | Retail (Lifestone) | Test |
|---|---|---|---|
| 1 | `Sphere.FindTimeOfCollision` returns the negation of both roots (Sphere.cs:246, 248) | `CSphere::find_time_of_collision` (0x005368F0): the smaller root when not negative, else the larger, so an approaching sphere gets its positive time of first contact | `SphereTests.Sphere_FindTimeOfCollision`, `Retail_find_time_of_collision_is_the_first_contact_when_approaching` |
| 2 | `Sphere.CollideWithPoint` adds the old displacement to the check position (Sphere.cs:199) and scales the normal by 1 / radius sum (Sphere.cs:196) | `CSphere::collide_with_point` (0x00537230) adds the clipped offset `movement * t - movement` and divides by the sum widened by the epsilon | `Retail_collide_with_point_clips_the_check_position_to_the_moment_of_contact` |
| 3 | `Sphere.SlideSphere(ref normal, currPos)` returns OK when nothing can slide (Sphere.cs:603) | `CSphere::slide_sphere` (0x00537440) returns 2, collided | `SphereTests.Sphere_SlideSphere` |
| 4 | `CylSphere.CollideWithPoint`, uncertain hit: the vertical time uses the movement's z (CylSphere.cs:120, 125) and the clip subtracts the scaled movement (CylSphere.cs:151) | `CCylSphere::collide_with_point` (0x0053ACB0): the old displacement's z, and `centre + movement * t - check` | `CylSphereTests.Retail_clips_a_sphere_falling_onto_the_top_to_rest_on_it` |
| 5 | `CylSphere.StepSphereDown` leaves the contact plane's cell unset (CylSphere.cs:418) | `CCylSphere::step_sphere_down` (0x0053A9B0) records the check position's cell | `CylSphereTests.Stepping_down_onto_the_top_records_its_plane_and_cell` |
| 6 | `CylSphere.IntersectsSphere` slides the second sphere with the first sphere's displacement (CylSphere.cs:279) | `CCylSphere::intersects_sphere` (0x0053B440) passes the second sphere's | `CylSphereTests.The_second_sphere_slides_with_its_own_displacement` |
| 7 | `Polygon.check_walkable` refuses a polygon whose normal points away from up (Polygon.cs:136) and returns false at the first edge the centre is outside of beyond its ends (Polygon.cs:165) | `CPolygon::check_walkable` (0x00538E60) refuses only an absolute dot(N, up) below the epsilon, and keeps testing the other corners | `PolygonTests.Check_walkable_keeps_testing_corners_after_an_edge_the_centre_is_outside_of`, `Check_walkable_accepts_a_polygon_facing_away_from_up` |
| 8 | `Polygon.adjust_to_placement_poly` negates the radius once for a solid centre or a centre behind the plane (Polygon.cs:119) | `CPolygon::adjust_to_placement_poly` (0x00538CE0) negates for each, so both together cancel | `PolygonTests.Adjust_to_placement_poly_negates_twice_for_a_solid_centre_behind_the_plane` |
| 9 | `Polygon.polygon_hits_sphere_precise` accepts an empty polygon before the plane test (Polygon.cs:333) | `CPolygon::polygon_hits_sphere_slow_but_sure` (0x00538A10) tests the plane distance first | (cannot arise: PH2's census finds every physics polygon has 3 to 12 corners) |
| 10 | `BBox.GetCenter` returns the half extent (BBox.cs:78); `LocalToGlobal`/`LocalToLocal` start the maximum at the transformed maximum corner (BBox.cs:105, 118), so a rotated box can miss a corner; `ConvertToGlobal` transforms two corners (BBox.cs:71) | `BBox::GetCenter` (0x005B2790) the midpoint; `LocalToLocal`/`LocalToGlobal` (0x005B1E60, 0x005B2120) start both corners at the minimum corner and add the other seven; `ConvertToGlobal` (0x005B23E0) uses seven corners, never the maximum one (a retail quirk, kept) | `PrimitiveTests` |
| 11 | `Ray(start, offset)` compares the offset with the start point and leaves the ray unset for a short one (Ray.cs:26) | `Ray::InitFromOffset` (0x0050D330) compares with zero; a short offset gives length 0 and direction +Y | `PrimitiveTests.A_ray_from_a_tiny_offset_has_no_length_and_points_north` |
| 12 | `PlaneExtensions.GetSide` adds the epsilon to the distance as a bias (PlaneExtensions.cs:22) | `Plane::which_side` (0x00444720) uses it as a band | `PrimitiveTests.Which_side_uses_the_epsilon_as_a_band` |
| 13 | `CollisionInfo.SetCollisionNormal`/`SetSlidingNormal` store the vector as given (CollisionInfo.cs:53, 61) | `COLLISIONINFO::set_collision_normal`/`set_sliding_normal` (0x0050A000, 0x0050A060) store it normalised | `SphereTests.Sphere_CollideWithPoint` |
| 14 | `SpherePath.StepUpSlide` leaves `StepUp` set (SpherePath.cs:309) | `SPHEREPATH::step_up_slide` (0x0050C3B0) clears it | `CylSphereTests.A_step_up_with_no_ground_in_reach_becomes_a_slide_along_the_post` |
| 15 | `Transition.EdgeSlide` evaluates `CliffSlide` and `PrecipiceSlide` as arguments of `SetEdgeSlide`, before it restores the check position, so the restore discards the slide (Transition.cs:278, 284) | `CTransition::edge_slide` (0x0050B3D0) restores first, then slides | `TransitionSyntheticTests.An_edge_sliding_object_slides_along_a_high_ledge_and_one_without_EdgeSlide_walks_off`, the slope test |
| 16 | `ValidatePlacementTransition` clears the collision record only for an object without a target (Transition.cs:974, "added target id") | `CTransition::validate_placement_transition` (0x0050ADC0) clears it whenever placement may slide | (no target before PH5) |
| 17 | `BSPTree.adjust_to_plane` runs its loop body only when the contact time is 1 and gives up otherwise; its bisection keeps the blocked half and fails once converged (BSPTree.cs:43-85) | `BSPTREE::adjust_to_plane` (0x00539BF0): contact times polygon by polygon until one is free, then halving that keeps the free end; success when converged | `BspQueryTests.A_path_clipped_sphere_is_moved_back_to_its_first_contact_with_a_polygon`, the camera sweep test |
| 18 | `BSPTree.placement_insert` keeps the hit polygon and the doubled radius after a push (BSPTree.cs:267) | `BSPTREE::placement_insert` (0x00539960) resets both after each push | `BspQueryTests.Placement_pushes_a_sphere_out_of_a_box_and_leaves_a_free_one_alone` |
| 19 | `BSPNode.sphere_intersects_solid_poly` takes a centre on the splitting plane to the negative side first (BSPNode.cs:309); `point_intersects_solid` sends a point on the plane to the negative child (BSPNode.cs:213) | `BSPNODE::sphere_intersects_solid_poly` (0x0053CD50) and `point_intersects_solid` (0x0053C310): the negative side only for a point strictly behind | `BspQueryTests` |
| 20 | `BSPTree.find_collisions` lets a path-clipped object's second sphere collide when the first misses (BSPTree.cs:221) | `BSPTREE::find_collisions` (0x0053A440) tests only the first sphere of a path-clipped object | (missiles, PH5) |
| 21 | `SpherePath.CheckWalkables` halves a copy of the walkable sphere (SpherePath.cs:213) | `SPHEREPATH::check_walkables` (0x0050C3E0) halves the stored sphere in place, so each call halves it again | the edge-slide test |
| 22 | `Polygon.adjust_sphere_to_poly` negates the contact distance when the move is no longer than the radius (Polygon.cs:110) | `CPolygon::adjust_sphere_to_poly` (0x00538170): the decompile keeps only the flags; read as the start side ([../physics.md](../physics.md) §4), since ACE's reading sends the camera through walls | the camera sweep test |
| 23 | `LandCell.add_all_outside_cells(spheres)` never marks the outside cells added and drops a cell whose landblock is not loaded (LandCell.cs:84, 217); `ObjCell.find_cell_list` resets the count but not its dictionary (ObjCell.cs:337) | `CLandCell::add_all_outside_cells` (0x00533630) marks them and `add_outside_cell` (0x00532EC0) keeps the id with no cell; `CELLARRAY` is an ordered array emptied per search | `TransitionSyntheticTests.On_a_cell_corner_the_last_cell_found_that_holds_the_centre_is_the_cell` |
| 24 | `LandCell.add_all_outside_cells(parts)` repeats the block search from every part's own cell (LandCell.cs:135) | `CLandCell::add_all_outside_cells` (0x00533360) searches once, from the first part's cell, over all parts' boxes (camera-facing parts by their spheres) | the straddling-static test |
| 25 | `ObjCell.FindObjCollisions` skips ethereal objects during placement ("custom", ObjCell.cs:185) | `CObjCell::find_obj_collisions` (0x0052B750) has no such skip | (no ethereal statics) |
| 26 | `Transition.FindPlacementPos` spirals with `Math.PI` (Transition.cs:384) | `CTransition::find_placement_pos` (0x0050BA50) uses the float 3.1415999 | (placement spirals) |
