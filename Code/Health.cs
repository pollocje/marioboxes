using Sandbox;

public sealed class Health : Component
{
	[Property] public float MaxHealth { get; set; } = 100f;
	[Property] public float RespawnDelay { get; set; } = 3f;

	// Synced from this object's owner — the victim is authoritative over their own HP,
	// matching the owner-authoritative pattern the rest of the project uses (see Movement).
	[Sync] public float Current { get; private set; }
	[Sync] private bool IsDead { get; set; }

	private RealTimeSince _deathTime;
	private bool _lastVisible = true;

	protected override void OnStart()
	{
		if ( !IsProxy )
			Current = MaxHealth;
	}

	// Any client can call this (e.g. the shooter that detected a hit), but the body only ever
	// runs on the connection that owns this Health — so HP mutation always happens exactly once,
	// on the victim's machine, and replicates out from there via [Sync].
	[Rpc.Owner]
	public void TakeDamage( float amount )
	{
		if ( Current <= 0f || IsDead ) return;

		Current -= amount;

		if ( Current <= 0f )
		{
			Current = 0f;
			IsDead = true;
			_deathTime = 0;
		}
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

		if ( IsProxy ) return; // respawn timing/position stays owner-authoritative
		if ( !IsDead ) return;
		if ( _deathTime < RespawnDelay ) return;

		var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToList();
		if ( spawnPoints.Count > 0 )
		{
			var spawnPoint = spawnPoints[Game.Random.Int( spawnPoints.Count - 1 )];
			GameObject.WorldPosition = spawnPoint.WorldPosition;
		}

		Current = MaxHealth;
		IsDead = false;
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
