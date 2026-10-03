This subsystem uses Netcode for GameObjects.

Server authority owns world-state mutations.

Do not move authoritative state changes to clients merely to simplify implementation.

Preserve NetworkVariable and RPC ownership semantics unless explicitly changing the networking architecture.