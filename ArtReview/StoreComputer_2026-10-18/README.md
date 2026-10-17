PC statistics / HUD update

- CounterComputer.prefab now includes StoreComputerStatisticsPresenter, a persistent balance display and Statistics content in the existing StoreInfo app. Existing app enum/network state stays compatible.
- The scene instance references 12 physical shelf modules. A shelf is complete when every logical slot in it is complete. Completed slots are shown separately.
- Placement counts use all spawned slot snapshots in the gameplay scene, including completed/frozen cases. The progress target and distinct title count come from GameCaseSessionPlan, not shelf capacity.
- Wallet/slot spawn events, wallet balance events, snapshot changes and session-plan changes update the UI. No frame polling or new RPC was added for statistics.
- ComputerLocalPresenter.hudGroups references GameScreen's CanvasGroup. Wallet, skill bar and stack HUD become invisible and non-interactive during local PC use; existing restoration preserves previous group values.
- Skill input/click paths respect GameplayInputGate. The skill service also checks the RPC sender's server-side PC reservation, so another player's PC use does not block the sender's skills.

Changed runtime files: StoreComputerStatisticsPresenter.cs (new), NetworkStoreComputer.cs, NetworkSharedSkillService.cs, SharedSkillInputRouter.cs, NetworkSharedWallet.cs, NetworkGameCaseShelfSlotState.cs. The existing one-time Editor setup/Odin support, CounterComputer.prefab and 03_GameplayScene were updated through Unity Editor APIs. No further Inspector wiring is needed for the current scene.

statistics-validation.txt records Editor reference and progress-bar checks (0%, 50%, 100%). Unity compilation passed. Play Mode/co-op remains to be checked: local HUD restore after exit/disconnect, 1/2/3 and UI skill blocking, another player's skill use, live wallet changes, placement/pickup percentage and late join.
