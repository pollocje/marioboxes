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
- **Health/damage**: `[Sync] public float Current`. As of the "host-validated
  hit confirmation" pass below, `TakeDamage` is a two-hop RPC rather than a
  single owner-trusting one — see that section for the current design.
  `attacker` is also used for friendly-fire checking and kill-credit — see the
  Team Deathmatch section below.
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

### Files touched (networking pass)

- New: `Code/PlayerSpawner.cs`
- Rewritten: `Code/Health.cs`, `Code/WeaponHolder.cs`, `Code/WeaponPickup.cs`,
  `Code/GrapplingHook.cs`
- Edited: `Code/Bullet.cs` (added `IsCosmetic`), `Code/Shoot.cs` (split
  `FireEffects`/`PlayReloadSound` out as broadcast RPCs)

## Gamemode: 5v5 Team Deathmatch (this session)

Win condition, chosen deliberately over the alternatives (timed-most-kills,
last-player-standing): **first team to 50 kills wins**, with a 15-minute match
time limit as a backstop (whoever's ahead when time runs out wins; a tie is a
draw). No elimination — death still respawns via the existing `Health` loop,
just now filtered to the player's own team's `SpawnPoint`s.

### New pieces

- **`Team.cs`** — `enum Team { Unassigned, Red, Blue }`.
- **`TeamMember.cs`** — lives on the player prefab. `[Sync(SyncFlags.FromHost)]
  public Team Team` — host-authoritative so players can't assign/change their
  own team.
- **`RoundManager.cs`** — one instance, placed in the scene (not
  `NetworkSpawn`'d — same "pre-placed scene object" assumption as
  `WeaponPickup`, see checklist). Host-authoritative `[Sync(SyncFlags.FromHost)]`
  score/timer/winner state. Exposes `[Rpc.Host] ReportKill(Team attackerTeam,
  Team victimTeam)`, called from `Health` on a kill — runs only on host so a
  kill is counted exactly once no matter which client's `Health` (i.e. which
  victim's owner) reports it. Handles the win check, the time-limit backstop,
  and a 10s intermission before auto-resetting scores for the next round.
- **`PlayerSpawner.cs`** — now balances each joiner onto whichever team has
  fewer players (`PickBalancedTeam`), and only picks among that team's
  `SpawnPoint`s (falling back to `Team.Unassigned` spawns if none exist).
- **`SpawnPoint.cs`** — added a `[Property] public Team Team` so spawns can be
  assigned to a side; unassigned spawns are usable by either team.
- **`Health.cs`** — `TakeDamage` now takes `attacker`, checks it against
  `TeamMember.Team` to skip damage when `FriendlyFire` is off (default) and
  attacker/victim share a team, calls `RoundManager.ReportKill` on a kill, and
  skips damage entirely once `RoundManager.RoundOver` is true. Respawn now
  filters `SpawnPoint`s by the player's own team.
- **`Shoot.cs`** — stops firing while `RoundManager.RoundOver` is true, so
  nobody keeps shooting (and spamming broadcast RPCs) during intermission.

### Known simplifications / not done

- **5v5 is hard-capped as of a later pass this session** — see "Hard 5v5 cap +
  host-validated hit confirmation" further down. Left the note here since this
  section reflects the state at the time of the original gamemode pass.
- **No scoreboard/HUD.** `RoundManager`'s state and the new `PlayerStats` are
  all there to bind to, but no `.razor` UI reads them yet — deliberately
  deferred (asset/editor work).
- **Movement isn't frozen during intermission or warmup** — players can still
  run around, they just can't deal or take damage or fire. Intentional
  simplification, not a bug — flag if you want a hard freeze instead.

### Files touched (gamemode pass)

- New: `Code/Team.cs`, `Code/TeamMember.cs`, `Code/RoundManager.cs`
- Edited: `Code/PlayerSpawner.cs`, `Code/SpawnPoint.cs`, `Code/Health.cs`,
  `Code/Bullet.cs` (passes `Source` into `TakeDamage`), `Code/Shoot.cs`
  (round-over guard)

## Per-player stats + match warmup (this session, follow-up)

- **`PlayerStats.cs`** (new) — lives on the player prefab. Host-authoritative
  `[Sync(SyncFlags.FromHost)]` `Kills`/`Deaths` ints. `RoundManager.ReportKill`
  now takes `attacker`/`victim` `GameObject`s alongside the team enums, and
  increments the right player's stats in the same host-only call that already
  updates team scores — one authoritative place, no separate RPC needed.
  A death is still recorded even when there's no kill credit (unattributed/
  environmental damage), but `Kills` only increments when `attackerTeam` is a
  real team different from the victim's.
- **Match warmup** — `RoundManager` no longer starts its countdown at scene
  load. New `[Sync(SyncFlags.FromHost)] bool WarmingUp` (starts `true`), and a
  `MinPlayersToStart` property (default 2, total across both teams). While
  `WarmingUp`, the timer doesn't tick; `Health.TakeDamage` and `Shoot.OnUpdate`
  both now check `WarmingUp` the same way they already check `RoundOver`, so
  nobody can fight before the match has actually started. Re-evaluated at the
  start of every round (`StartNewRound`), not just once at boot — so if
  everyone disconnects during intermission, the next round waits again instead
  of immediately burning its clock with no one there.

### Files touched (this follow-up)

- New: `Code/PlayerStats.cs`
- Edited: `Code/RoundManager.cs` (warmup state, `ReportKill` signature change),
  `Code/Health.cs` (passes attacker/victim into `ReportKill`, warmup guard),
  `Code/Shoot.cs` (warmup guard)

## Spawn protection, overtime, disconnect handling (this session, second follow-up)

- **Spawn protection** — `Health.cs` gained `SpawnProtectionDuration` (default
  2s) and a local `RealTimeSince` timer, checked in `TakeDamage` right after
  the round-state checks. Starts on `OnStart` and resets on every respawn.
  Not synced — it's checked in the same owner-only `TakeDamage` path that
  already decides everything else about the victim's HP, so there's no reason
  for anyone else to need it. Firing your own weapon clears it early
  (`Health.ClearSpawnProtection()`, called from `Shoot.Fire()`) — standard
  "protection ends the moment you take an offensive action" convention, stops
  it being used as a free-damage window.
- **Overtime** — replaces the old "tied at the time limit = draw" behavior.
  New `[Sync(SyncFlags.FromHost)] bool Overtime`. When `TimeRemaining` hits
  zero: if scores differ, round ends immediately as before; if tied, `Overtime`
  goes true instead — the countdown stops entirely and `ReportKill` short-
  circuits to `EndRound` on the *first* kill by either team, regardless of
  `KillsToWin`. `Health`/`Shoot` don't need to know about `Overtime` — combat
  keeps working exactly as it does in a normal round, `ReportKill` is just the
  one place that now treats "any kill" as the win condition while it's active.
- **Disconnect handling** — deliberately *not* built on
  `INetworkListener.OnDisconnected`. Whether a leaving player's `TeamMember`
  is destroyed before or after that callback fires isn't something I could
  verify from this sparse checkout, and getting that ordering wrong would mean
  either double-counting or missing the disconnect entirely. Instead
  `RoundManager.OnUpdate` polls both teams' live counts every frame (right
  next to the existing warmup check, same cost) during an active round: if one
  team hits zero, the other wins immediately; if both hit zero (everyone
  left), it calls `StartNewRound()` — full reset back to warmup rather than
  "ending" a round nobody's playing. This also means a round that never had
  players on both sides can't get stuck.

### Files touched (this follow-up)

- Edited: `Code/Health.cs` (spawn protection), `Code/Shoot.cs`
  (`ClearSpawnProtection` on fire), `Code/RoundManager.cs` (`Overtime` state,
  per-team disconnect polling)

## Weapon reset on death + killfeed plumbing (this session, third follow-up)

- **Weapon reset on death** — new `WeaponHolder.ResetToStartingWeapon()`,
  called from `Health.cs` at the exact moment a player respawns (same place
  HP/position/spawn-protection already reset). Whatever weapon they'd picked
  up is discarded — `EquipLocal`'s existing `CurrentWeapon?.Destroy()` handles
  that as a side effect of equipping the starting weapon again, no extra code
  needed for the "discard" part. Reset happens on respawn, not at the moment
  of death.
- **Killfeed plumbing** — no UI, just the networked data path. `Bullet.cs`
  gained a `WeaponName` string (set in `Shoot.Fire()` from `WeaponHolder.
  CurrentWeaponId` — the synced ID, not the runtime clone's `GameObject.Name`,
  to avoid relying on how `Clone()` happens to name instances). It flows
  through `Health.TakeDamage(amount, attacker, weaponName)` into
  `RoundManager.ReportKill`, which broadcasts it via a new static event:
  `RoundManager.OnKillFeedEvent` — `Action<killerName, victimName, weaponName,
  killerTeam, victimTeam>`, fired identically on every client through a
  `[Rpc.Broadcast]` method. Fires for *every* death, including team-kills and
  unattributed ones (kill credit is still team-only, gated separately). A
  future killfeed UI just subscribes to this event — no networking of its own
  required.

### Files touched (this follow-up)

- Edited: `Code/WeaponHolder.cs` (`ResetToStartingWeapon`), `Code/Health.cs`
  (calls it on respawn, threads `weaponName` through), `Code/Bullet.cs`
  (`WeaponName`), `Code/Shoot.cs` (sets it from `WeaponHolder.CurrentWeaponId`),
  `Code/RoundManager.cs` (`OnKillFeedEvent`, `BroadcastKillFeed`)

## Hard 5v5 cap + host-validated hit confirmation (this session, fourth follow-up)

- **Hard 5v5 cap / spectator queue** — `PlayerSpawner` gained
  `MaxPlayersPerTeam` (default 5) and `PickTeamWithRoom()`, which returns
  `Team.Unassigned` once both teams are at the cap instead of always finding
  a team like the old `PickBalancedTeam` did. When that happens, the joining
  `Connection` goes into a local `_spectatorQueue` list instead of getting a
  player GameObject spawned at all — nothing is cloned or `NetworkSpawn`'d for
  them yet. A new `OnUpdate` (host-only) polls that queue every frame and
  spawns the front of the line as soon as a team has room (a disconnect
  elsewhere freeing up a slot). Queued connections are pruned against
  `Connection.All` each check so someone who disconnects before ever getting a
  slot doesn't sit in the queue forever.
  **Important scope note**: this is queue logic only. A queued player has *no
  GameObject, no camera, nothing to look at* — there's no spectator-mode
  camera or UI built here, because that needs asset/editor work this session
  doesn't have access to. They're genuinely just waiting, invisibly, until a
  slot opens. Worth keeping in mind before testing with more than 10 players.
- **Host-validated hit confirmation** — `Health.TakeDamage` used to be a
  single `[Rpc.Owner]` call that the victim's machine trusted unconditionally.
  It's now a two-hop RPC: the shooter calls the same `TakeDamage(amount,
  attacker, weaponName)`, but it's now `[Rpc.Host]` — the host runs it first
  and actually checks the claim against something it can observe itself,
  rather than trusting the caller's numbers:
  - **Range plausibility** — rejects the hit if `attacker`'s position is
    further than `MaxValidHitRange` (2500, comfortably above `Bullet`'s
    default 2000 `MaxRange`) from the victim's position. Both positions come
    from normal transform sync, so this reuses the same "proxy transform sync
    is automatic" assumption already flagged elsewhere rather than adding a
    new one.
  - **Damage clamp** — `amount` is capped at `MaxDamagePerHit` (100) before
    anything is applied, so a client can't just claim an arbitrary damage
    number.
  - Round-state and friendly-fire checks that used to live in the single
    method now run here, host-side, since the host is now the one deciding
    whether a hit is even legitimate.
  Once validated, the host calls a new private `[Rpc.Owner]
  ApplyValidatedDamage(...)` — this is the actual HP mutation, and it's
  *still* owner-authoritative, same as before. The host hop is a validation
  gate, not a change of who owns `Current`/`IsDead` — deliberately, so this
  didn't need to touch the `[Sync]` flag on those properties or the
  owner-authoritative respawn logic in `OnUpdate`, both of which would have
  dragged in much bigger, riskier changes (see the "what I didn't do" note
  below). Spawn protection is re-checked in `ApplyValidatedDamage`, not
  `TakeDamage` — it's a local, unsynced timer, so only the owner's own copy of
  it is ever reliably correct; checking it host-side would silently misbehave
  for every player except whichever one the host happens to be.
  **What this doesn't cover**: no lag compensation, no line-of-sight/obstacle
  check (only straight-line distance), no per-attacker fire-rate validation
  against weapon stats. This catches blatant cheating (impossible range,
  absurd damage claims), not a sophisticated aimbot respecting normal ranges.
  Full server-authoritative simulation (host re-running the bullet trace
  itself) would close more of that gap but is a substantially bigger
  architectural change than this session took on.

### Files touched (this follow-up)

- Edited: `Code/PlayerSpawner.cs` (`MaxPlayersPerTeam`, spectator queue),
  `Code/Health.cs` (`TakeDamage`/`ApplyValidatedDamage` split, range/damage
  validation)

## Asset/Editor Checklist — everything to wire up by hand

None of this can be done from `Code/` alone; it all needs the S&Box editor.
Nothing here will throw a compile error if skipped — every one of these fails
*silently* (a missing component just means that feature quietly no-ops), so
it's worth going through deliberately rather than trusting a clean compile.

**`_player.prefab`:**

- [ ] Add a `TeamMember` component. Without it, `PlayerSpawner`/`Health` treat
      the player as `Team.Unassigned` forever — no team assignment, no
      friendly-fire protection, spawns fall back to unassigned points.
- [ ] Add a `PlayerStats` component. Without it, kills/deaths just never
      increment for that player — no crash, no visible symptom.
- [ ] On the `WeaponHolder` component, populate the `WeaponRegistry` list with
      every weapon prefab in `Assets/Prefabs/Weapons/`: `weapon.laser`,
      `weapon.smg`, `weapon.sword`, `weapon_ar15`, `weapon_burstRifle`,
      `weapon_grenadeLauncher`, `weapon_pistol`. Any weapon not in this list
      will fail to resolve on equip.
- [ ] Double-check the existing wiring on `GunAim` (`PlayerCenter`,
      `BarrelTip`) and `GrapplingHook` (`PlayerCenter`) — these were never
      visible from this sparse checkout, only inferred from the C#.

**Scene (`minimal.scene`, and any other playable scene):**

- [ ] Add a `PlayerSpawner` GameObject, set its `PlayerPrefab` to
      `_player.prefab`.
- [ ] Add a `RoundManager` GameObject (exactly one).
- [ ] Add/verify enough `SpawnPoint`s around the arena, and set `Team` on
      each — split them roughly evenly Red/Blue so the two teams don't spawn
      on top of each other. Every `SpawnPoint` currently defaults to
      `Team.Unassigned` (usable by either team as a fallback), which works for
      a first smoke test but isn't what you want for a real match.

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
- [ ] `[Sync(SyncFlags.FromHost)]` — exact attribute/flag name for
      host-authoritative sync (used throughout `TeamMember` and
      `RoundManager`). This is the item I'm least certain of syntactically.
- [ ] `[Rpc.Host]` — same pattern as `[Rpc.Owner]` but targeting the host;
      used by `RoundManager.ReportKill`.
- [ ] Whether an RPC parameter can carry a `GameObject` reference (used by
      `Health.TakeDamage(float, GameObject attacker)`) — should work since the
      referenced object is itself networked via `NetworkSpawn`, but wasn't
      verified.
- [ ] Whether `Scene.GetAllComponents<TeamMember>()` reflects a disconnected
      player's departure immediately or with a frame or two of lag — affects
      how snappy `RoundManager`'s empty-team detection actually is in practice.
- [ ] `Connection.All` — used by `PlayerSpawner` to prune the spectator queue
      of anyone who disconnected before getting a slot. Assumed to be an
      enumerable of currently-connected `Connection`s; not verified.
- [ ] `[Rpc.Host]` being callable from within another `[Rpc.Host]`-adjacent
      call chain, and specifically a `[Rpc.Owner]` method (`ApplyValidatedDamage`)
      being reachable as a plain method call from inside a `[Rpc.Host]` method
      (`TakeDamage`) — i.e. that calling a `[Rpc.X]` method from inside another
      RPC's body still triggers the attribute's routing rather than just
      running inline on whichever machine is currently executing. This is the
      new host-validated damage path's central assumption.

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
  - Teams: joiners alternate/balance Red vs Blue; each spawns at their own
    team's spawn points; shooting a teammate does nothing (with `FriendlyFire`
    off); shooting an enemy counts toward that enemy's team score; reaching 50
    kills ends the round and, after 10s, a new one starts with scores reset.
  - Warmup: with only one client connected, confirm nobody can deal/take
    damage or fire, and `TimeRemaining` isn't counting down. Connect a second
    client (satisfying `MinPlayersToStart`) and confirm the match actually
    starts.
  - Stats: killing an enemy increments the killer's `PlayerStats.Kills` and
    the victim's `Deaths`; team-kills and unattributed deaths still increment
    `Deaths` but never `Kills`.
  - Spawn protection: immediately after respawning, take fire from an enemy —
    confirm no damage applies. Fire your own weapon, then take fire again —
    confirm damage now applies normally.
  - Overtime: force a tied score at the time limit (or temporarily lower
    `MatchTimeLimit`/`KillsToWin` for testing) and confirm `Overtime` goes
    true, the timer stops, and the very next kill (by either team) ends the
    round instead of requiring `KillsToWin`.
  - Disconnect: with two clients in an active round, disconnect one — confirm
    the round ends in favor of the remaining team within roughly a frame or
    two. Disconnect both — confirm it resets to `WarmingUp` instead of
    leaving a stale `RoundOver`/score state.
  - Weapon reset: pick up a non-starting weapon, die, respawn — confirm you're
    back to the starting weapon (pistol), not what you died holding.
  - Killfeed: subscribe something quick to `RoundManager.OnKillFeedEvent`
    (even just a `Log.Info` in a throwaway component) and confirm it fires
    with the right killer/victim/weapon/team names on every client, not just
    the one that landed the kill.
  - 5v5 cap: connect an 11th client (or temporarily lower `MaxPlayersPerTeam`
    to make this easier to reach) and confirm they're queued rather than
    forced onto a full team — no player GameObject for them at all. Disconnect
    someone from a full team and confirm the queued connection gets spawned
    within a frame or two.
  - Hit validation: this is the hardest one to test meaningfully without
    modifying a client to actually cheat. At minimum, confirm normal play
    still works end-to-end (damage still applies, kills still count) now that
    it's routing through an extra host hop — that's the main regression risk.
    If you want to actually exercise the validation, temporarily call
    `TakeDamage` with an absurd `amount` or a far-away `attacker` from a debug
    command and confirm it's rejected.

## Not yet started

- **Scoreboard/timer/killfeed HUD** — `RoundManager`'s state, `PlayerStats`,
  and `OnKillFeedEvent` are all there to build on, no `.razor` UI built yet
  (deliberately deferred).
- **Spectator camera/UI** — the queue in `PlayerSpawner` exists, but a queued
  player has nothing to look at; needs actual spectator-mode camera/UI work.
- **Full server-authoritative hit simulation** — the host validates range and
  clamps damage, but doesn't re-run the bullet trace itself, so it's not a
  full replacement for a client actually lying about *whether* it hit
  something within a plausible range, only about impossible claims.
