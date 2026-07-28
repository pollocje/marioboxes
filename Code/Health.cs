using Sandbox;

public sealed class Health : Component
{
	[Property] public float MaxHealth { get; set; } = 100f;
	[Property] public float RespawnDelay { get; set; } = 3f;
	[Property] public bool FriendlyFire { get; set; } = false;
	[Property] public float SpawnProtectionDuration { get; set; } = 2f;
	[Property] public float MaxDamagePerHit { get; set; } = 100f;
	[Property] public float MaxValidHitRange { get; set; } = 2500f;

	// World Z below which a player is considered fallen out of the map and dies instantly.
	// -1000 is a placeholder — tune per map once real maps exist; this checkout has no way to
	// know actual arena scale.
	[Property] public float KillZ { get; set; } = -1000f;

	// Synced from this object's owner — the victim is authoritative over their own HP,
	// matching the owner-authoritative pattern the rest of the project uses (see Movement).
	[Sync] public float Current { get; private set; }
	[Sync] private bool IsDead { get; set; }

	private RealTimeSince _deathTime;
	private RealTimeSince _spawnProtectionStart;
	private bool _lastVisible = true;
	private TeamMember _team;
	private WeaponHolder _weaponHolder;

	protected override void OnStart()
	{
		_team = Components.Get<TeamMember>();
		_weaponHolder = Components.Get<WeaponHolder>();
		_spawnProtectionStart = 0;

		if ( !IsProxy )
			Current = MaxHealth;
	}

	// Entry point — any client can call this (the shooter, from Bullet.cs), but it routes through
	// the host for validation before the victim's own client is ever asked to apply anything. A
	// cheating client can call this with whatever numbers it likes, so nothing here trusts the
	// caller without checking it against something the host can itself observe: the shooter's
	// actual (synced) position, and a hard cap on how much damage a single hit can claim.
	// `weaponName` is passed straight through to RoundManager, purely for the killfeed.
	[Rpc.Host]
	public void TakeDamage( float amount, GameObject attacker, string weaponName )
	{
		if ( Current <= 0f || IsDead ) return;
		if ( RoundManager.Instance is not null && ( RoundManager.Instance.RoundOver || RoundManager.Instance.WarmingUp ) ) return;

		if ( attacker is not null && (attacker.WorldPosition - WorldPosition).Length > MaxValidHitRange )
			return; // implausible — claimed hit came from further away than any weapon can reach

		var myTeam = _team?.Team ?? Team.Unassigned;
		var attackerTeam = attacker?.Components.Get<TeamMember>()?.Team ?? Team.Unassigned;

		if ( !FriendlyFire && myTeam != Team.Unassigned && attackerTeam == myTeam )
			return;

		var clampedAmount = System.MathF.Min( amount, MaxDamagePerHit );

		ApplyValidatedDamage( clampedAmount, attacker, weaponName );
	}

	// Only ever reached via the host-validated path above. Still runs on the victim's own
	// connection — this object's owner stays the source of truth for its own Current/IsDead,
	// same as everything else in the project that's owner-authoritative (see Movement) — the host
	// hop above is purely a validation gate, not a change of who owns this state.
	[Rpc.Owner]
	private void ApplyValidatedDamage( float amount, GameObject attacker, string weaponName )
	{
		if ( Current <= 0f || IsDead ) return; // could have died to something else in the meantime
		if ( _spawnProtectionStart < SpawnProtectionDuration ) return;

		Current -= amount;

		if ( Current <= 0f )
			Kill( attacker, weaponName );
	}

	// Shared by combat death and fall-death (see OnUpdate) so both go through the same
	// respawn/kill-crediting/weapon-reset path instead of two versions that can drift apart.
	private void Kill( GameObject attacker, string weaponName )
	{
		Current = 0f;
		IsDead = true;
		_deathTime = 0;

		var myTeam = _team?.Team ?? Team.Unassigned;
		var attackerTeam = attacker?.Components.Get<TeamMember>()?.Team ?? Team.Unassigned;
		RoundManager.Instance?.ReportKill( attackerTeam, myTeam, attacker, GameObject, weaponName );
	}

	// Firing forfeits spawn protection — a protected player can still choose to shoot, but
	// doing so immediately exposes them, matching the usual "no protected pot-shots" convention.
	public void ClearSpawnProtection()
	{
		_spawnProtectionStart = SpawnProtectionDuration;
	}

	protected override void OnUpdate()
	{
		// Visibility must react for everyone, not just the owner, so other clients see deaths/respawns.
		bool shouldBeVisible = !IsDead;
		if ( shouldBeVisible != _lastVisible )
		{
			SetVisible( shouldBeVisible );
			_lastVisible = shouldBeVisible;
		}

		if ( IsProxy ) return; // fall-death and respawn timing/position stay owner-authoritative

		if ( !IsDead )
		{
			// Fallen out of the map — no attacker, so this skips TakeDamage's host round-trip and
			// spawn-protection check entirely. There's nothing to validate about your own position
			// (it's not a claim from another client), and protection is meant to guard against
			// other players, not the void — also deliberately not gated on RoundOver/WarmingUp,
			// unlike combat damage, so nobody falls forever during warmup/intermission.
			if ( WorldPosition.z < KillZ )
				Kill( null, null );
			return;
		}

		if ( _deathTime < RespawnDelay ) return;

		var myTeam = _team?.Team ?? Team.Unassigned;
		var spawnPoints = Scene.GetAllComponents<SpawnPoint>()
			.Where( sp => sp.Team == myTeam || sp.Team == Team.Unassigned )
			.ToList();
		if ( spawnPoints.Count == 0 )
			spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToList();

		if ( spawnPoints.Count > 0 )
		{
			var spawnPoint = spawnPoints[Game.Random.Int( spawnPoints.Count - 1 )];
			GameObject.WorldPosition = spawnPoint.WorldPosition;
		}

		Current = MaxHealth;
		IsDead = false;
		_spawnProtectionStart = 0;
		_weaponHolder?.ResetToStartingWeapon();
	}

	private void SetVisible( bool visible )
	{
		foreach ( var child in GameObject.Children )
			child.Enabled = visible;

		foreach ( var renderer in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelf ) )
			renderer.Enabled = visible;

		var rb = Components.Get<Rigidbody>();
		if ( rb is not null ) rb.Enabled = visible;
	}
}
