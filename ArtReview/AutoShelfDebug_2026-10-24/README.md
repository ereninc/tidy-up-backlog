# Auto shelf debug tool

Before Play Mode, add `GameCaseAutoShelfDebugTool` to a new **root** GameObject in GameplayScene (e.g. `AutoShelfDebug`). Unity also adds `NetworkObject` and `GameCaseShelfCompletionGroup`. Keep it outside other NetworkObjects. The in-scene object must be spawned by the host before its buttons are available.

Inspector setup:

- `Batch Size`: 50 by default; this limits eligible candidates examined, not successful placements.
- `Placement Interval`: 0.1 seconds by default. Zero still yields between candidates.
- `Case Registry`: optional; defaults to the existing `GameCaseInstanceRegistry.Instance` after binding is ready.
- `Target Shelves`: defaults to the completion group on the same object.
- On **Shelf Completion Group**, assign `Shelf Roots` in desired order. One `CozyStoreShelves` root targets all its slots; individual shelf/slot roots target a subset. Empty roots use this scene's hierarchy order.

The custom Unity Inspector shows exactly one **Place Next Batch** and **Cancel Current Batch** pair. They require a spawned server/host object in Play Mode. Cursor advances past examined candidates, resumes after cancellation, and wraps on the next click after the list end. Registry rebuild starts a fresh round. Cancellation never rolls back committed items or interrupts their item-owned flight.

**Shift + G** toggles the same start/cancel methods from the tool's `Update` method. Either Shift key works. The chord does not also fire Drop; plain G retains normal drop behavior. Clients cannot use the shortcut to request server placements.

Placement reuses `NetworkItemReceiver.TryBuildPlacementPlanServer` and the shared `NetworkItemTransferService` commit. The server-only world entry neither creates a carrier nor changes held placement rules. It sends the existing motion cue and uses the existing parenting, slot layout, row lock, occupancy, completion and reward chain. There is no new client placement RPC or NetworkTransform.

`GameCaseShelfCompletionGroup` is independent of the debug runner. `IsComplete` is server-written and replicated, including late join; zero valid spawned slots are incomplete. Listen to `Completed` on the server (false → true) or `CompletionChanged(bool)` for replicated presentation. Its default listener logs `All target shelves completed.` after the existing final slot reward. This is separate from existing `AllCasesPlaced`, whose target is the session case count rather than all configured slots.

Choose a target scope that the available cases can fill. If the selected shelves have spare capacity beyond the session case count, placing every session case correctly does not complete this stricter all-target-slots condition.

Changed runtime files: new `GameCaseAutoShelfDebugTool.cs` and `GameCaseShelfCompletionGroup.cs`; `GameCaseInstanceRegistry.cs` exposes its existing rebuild order as a read-only list; `NetworkItemTransferService.cs` adds the narrow world entry and shares its existing placement commit. Editor files provide the exact two-button Inspector and explicit isolated host checks.

Run focused checks with **Tools > Game Cases > Debug > Run Auto Shelf Host Checks** from a saved single scene in Edit Mode. This opens a temporary empty scene, starts a real NGO/UTP host, then restores the previous scene and Editor launch preferences. Results are in `host-checks.txt`.

Executed: 46 host checks passed, including typed/row/free priority, candidate budget/cursor/wrap, cancel/spam/disable/despawn/shutdown, four layout modes, normal held/world transaction interleaving, zero-target state, one completion transition and existing wallet rewards. Unity C# compilation and source whitespace checks passed.

The fixture uses injected bound AppIds and empty visual transforms, not Steam covers or full player visuals. A separate client, real simultaneous network requests, rendered flight and transport late join still require multiplayer Play Mode/build checks. No production scene, prefab, PC or UI is modified by this tool's implementation or validation.
