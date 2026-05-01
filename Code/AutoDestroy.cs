using Sandbox;

public sealed class AutoDestroy : Component
{
	[Property] public float Lifetime { get; set; } = 0.1f;

	private RealTimeSince _spawnTime;

	protected override void OnStart()
	{
		_spawnTime = 0;
	}

	protected override void OnUpdate()
	{
		if ( _spawnTime >= Lifetime )
			GameObject.Destroy();
	}
}
