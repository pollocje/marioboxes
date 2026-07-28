# MarioBoxes (S&Box)

S&Box remake of "Super Mayhem Boxes" from Garry's Mod. A 2.5D side-view arena
shooter/brawler: the play space is locked to a single X plane, players move and
aim on the YZ plane, box-shaped players roll/jump around and shoot each other.
`marioboxes.sbproj` already declares `GameNetworkType: Multiplayer`, 64 max
players, 50 tick rate — this is meant to be properly multiplayer.

## Working setup on this machine

This checkout is a **sparse clone** — only `Code/`, `Editor/`, `ProjectSettings/`,
and the root config files were pulled (`git sparse-checkout set Code Editor
ProjectSettings`). `Assets/` (prefabs, scenes, materials — the binary/large stuff)
was deliberately left out. That means **none of the prefab wiring can be verified
here** — no S&Box editor, no compiling, no play-testing. Everything in this session
was designed and written by reading the C# only. Treat all of it as "should be
right" rather than "verified," and see the checklist at the bottom before trusting
it.

## Gameplay components (what exists, pre-networking)

- `Movement.cs` — Rigidbody-based move/jump, locks the X axis, does an air-roll
  on jump. Owner-authoritative (`if (IsProxy) return` in `OnFixedUpdate`).
- `GunAim.cs` — projects the mouse cursor onto the play plane, orbits the gun
  pivot around the player, orients the barrel toward the cursor.
- `Shoot.cs` / `Bullet.cs` — mag/reload gun. Bullets are swept-trace projectiles
  (not hitscan), owner-simulated.
- `WeaponData.cs` / `WeaponHolder.cs` / `WeaponPickup.cs` — per-weapon stats live
  on a `WeaponData` component on the weapon prefab; `WeaponHolder` equips a
  weapon onto a player; `WeaponPickup` is a world pickup that grants a weapon.
- `GrapplingHook.cs` / `HookProjectile.cs` — fires a projectile that either hooks
  static geometry (swings the player) or nothing (misses); rope drawn with a
  `LineRenderer`.
- `Health.cs` — HP, death, and a respawn-to-`SpawnPoint` loop.
- `SpawnPoint.cs` — marker component; drop several in the scene.
- Cosmetic/local-only, untouched: `CameraFollow`, `WorldFollow`, `Bobbing`,
  `AutoDestroy`, `CursorDebug`, `MyComponent`.

## Networking pass (this session)

Before this session there was **no networking at all** beyond `IsProxy` checks
gating input — despite the project being configured for multiplayer. Nothing
called `NetworkSpawn`, nothing was `[Sync]`, there were no RPCs. Bullets, HP,
weapon pickups, and even the grappling rope only ever worked correctly on one
machine.

### Authority model

- **Movement**: stays owner-authoritative, unchanged. Relies on S&Box's built-in
  networked-transform sync/interpolation for proxies to see other players move —
  **not independently verified this session**, see checklist.
- **Health/damage**: `[Sync] public float Current`. `TakeDamage` is
  `[Rpc.Owner]` — any client can call it (the shooter does, from `Bullet.cs`),
  but the method body only executes on the connection that *owns* the target,
  so HP is always mutated exactly once, by the victim's own machine, and
  replicates out from there. This is **not cheat-proof** (it trusts whichever
  client detected the hit) — it was chosen to match the existing
  owner-authoritative pattern rather than introduce host-authority everywhere.
  A host-validated hit-confirm pass is a reasonable future hardening step.
- **Weapons**: equipped weapon is synced as a **string ID** (the prefab's
  `Name`), not a GameObject reference — raw prefab/asset references can't be
  synced, only spawned networked GameObjects can. `WeaponHolder.Equip()` now
  calls a `[Rpc.Broadcast]` method so every client (owner and every proxy)
  independently resolves the same ID against its own local `WeaponRegistry`
  list and applies the same visuals + weapon stats at the same time. Late
  joiners pick up the current `[Sync]`'d ID in `OnStart`.
- **Weapon pickups**: made **host-authoritative** (`Networking.IsHost`). This
  fixes a real bug that existed before networking was even considered: the old
  `OnUpdate` looped over *every* `WeaponHolder` in the scene with no ownership
  check, so every client's copy of the pickup would try to grant itself to
  every player it could see. With real networking that would mean any client
  could hand any player a weapon, and two clients could double-claim the same
  pickup in one tick. Now only the host decides; `Available` is `[Sync]` so
  everyone reflects the same state.
- **Shooting feedback**: hit resolution (`Bullet`'s trace + damage) stays
  owner-simulated — cheap, consistent with movement. But muzzle flash, fire
  sound, and reload sound are now wrapped in `[Rpc.Broadcast]` methods, because
  before this pass **nobody but the shooter could see or hear them fire at
  all**. Non-owning clients also get a cosmetic-only bullet (`Bullet.IsCosmetic
  = true`, skips tracing/damage) so the shot is visible to them too.
- **Grappling hook**: found and fixed a real bug — `OnUpdate` used to `return`
  immediately on `IsProxy`, *before* the rope-drawing code ran, so nobody but
  the hook's owner ever saw their own rope. Hook state/points are now `[Sync]`
  (`State`, `HookPoint`, `FiringHeadPoint`); physics simulation (`OnFixedUpdate`)
  stays owner-only, but the rope visual in `OnUpdate` now runs for owner and
  proxies alike, driven off synced state. Known simplification: the in-flight
  "Firing" phase (hook projectile traveling before it lands/misses) is
  synced once per `OnUpdate` frame, not physically networked as its own object
  — fine for a fast, sub-second flight, but it's not simulated identically on
  every machine.
- **Player spawning**: didn't exist. Added `PlayerSpawner.cs`, which implements
  `Component.INetworkListener.OnActive(Connection)` — clones the player prefab
  at a random `SpawnPoint` and calls `.NetworkSpawn(channel)`.

### Files touched

- New: `Code/PlayerSpawner.cs`
- Rewritten: `Code/Health.cs`, `Code/WeaponHolder.cs`, `Code/WeaponPickup.cs`,
  `Code/GrapplingHook.cs`
- Edited: `Code/Bullet.cs` (added `IsCosmetic`), `Code/Shoot.cs` (split
  `FireEffects`/`PlayReloadSound` out as broadcast RPCs)
- Untouched: everything else in `Code/`

## TODO — wiring this up at home

**Scene/prefab wiring (needs the editor, can't be done from Code/ alone):**

- [ ] Add a `PlayerSpawner` GameObject to the startup scene (`minimal.scene`,
      and any other playable scene), set its `PlayerPrefab` to `_player.prefab`.
- [ ] Drop several `SpawnPoint` components around the arena (currently unknown
      how many exist — `Health.cs` and `PlayerSpawner.cs` both now pick
      *randomly* among all of them instead of always using the first one).
- [ ] On the player prefab's `WeaponHolder` component, populate the new
      `WeaponRegistry` list with every weapon prefab in `Assets/Prefabs/Weapons/`
      (laser, smg, sword, ar15, burstRifle, grenadeLauncher, pistol) — equips
      will silently fail to resolve for any weapon not in this list.
- [ ] Double check `_player.prefab`'s existing component wiring (`GunAim.
      PlayerCenter`/`BarrelTip`, `GrapplingHook.PlayerCenter`, etc.) — none of
      that was visible from this sparse checkout, only inferred from the C#.

**Verify against current S&Box docs — API surface I used with moderate-to-high
but not certain confidence, since s&box's networking API has shifted before:**

- [ ] `[Sync]`, `[Rpc.Broadcast]`, `[Rpc.Owner]` attribute names/usage.
- [ ] `Component.INetworkListener.OnActive(Connection channel)` as the
      join-spawn hook.
- [ ] `GameObject.Clone(...)` + `.NetworkSpawn(Connection)` signature.
- [ ] `Networking.IsHost` as the host-check.
- [ ] `Game.Random.Int(max)` — used in `PlayerSpawner.cs` and `Health.cs` for
      picking a random spawn point.
- [ ] Whether pre-placed scene objects (like `WeaponPickup` instances already
      sitting in a `.scene` file) get `[Sync]`/RPC working automatically, or
      need something explicit.
- [ ] Whether networked Rigidbody transform sync/interpolation for proxies is
      automatic, or needs an explicit opt-in on the player prefab —
      `Movement.cs` wasn't changed on the assumption this is automatic.

**Test plan once it compiles:**

- [ ] Two local clients (S&Box supports easily launching a second client
      instance against your own listen server) — verify:
  - Both players see each other move smoothly (not teleporting/stuttering).
  - Shooting: shooter sees a real bullet, the other client sees a cosmetic
    tracer + hears the shot + sees the muzzle flash.
  - Getting hit reduces HP for the victim and that's visible on both screens;
    death hides the player on both screens; respawn un-hides at a spawn point.
  - Weapon pickups: only one client can claim a given pickup, both clients see
    the same equipped weapon model on the player who grabbed it, the other
    client's copy of the pickup disappears too.
  - Grappling hook: swinging shows the rope on *both* the swinger's screen and
    the other client's screen.

## Not yet started — gamemode logic

No round timer, win condition, scoring, or team system exists yet at all. This
session was scoped to *networking what already exists*; the gamemode loop
itself is the next piece of work once the above is verified working.
