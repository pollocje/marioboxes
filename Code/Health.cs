using Sandbox;

public sealed class Health : Component
{
	[Property] public float MaxHealth { get; set; } = 100f;
	[Property] public float RespawnDelay { get; set; } = 3f;

	public float Current { get; private set; }

	private bool _isDead;
	private RealTimeSince _deathTime;

	protected override void OnStart()
	{
		Current = MaxHealth;
	}

	public void TakeDamage( float amount )
	{
		if ( Current <= 0f || _isDead ) return;

		Current -= amount;

		if ( Current <= 0f )
		{
			Current = 0f;
			_isDead = true;
			_deathTime = 0;
			SetVisible( false );
		}
	}

	protected override void OnUpdate()
	{
		if ( !_isDead ) return;
		if ( _deathTime < RespawnDelay ) return;

		var spawnPoint = Scene.GetAllComponents<SpawnPoint>().FirstOrDefault();
		if ( spawnPoint is not null )
			GameObject.WorldPosition = spawnPoint.WorldPosition;

		Current = MaxHealth;
		_isDead = false;
		SetVisible( true );
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
