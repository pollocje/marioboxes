using Sandbox;

public sealed class Bullet : Component
{
	[Property] public float Lifetime { get; set; } = 3f;
	[Property] public float Damage { get; set; } = 10f;

	private float _spawnTime;
	private Rigidbody _rb;
	private Vector3 _lastPosition;

	protected override void OnStart()
	{
		_spawnTime = Time.Now;
		_rb = Components.Get<Rigidbody>();
		_lastPosition = WorldPosition;
	}

	protected override void OnUpdate()
	{
		if ( Time.Now - _spawnTime >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		if ( _rb is null ) return;

		// Keep bullet on the 2D plane
		var vel = _rb.Velocity;
		vel.x = 0f;
		_rb.Velocity = vel;
		WorldPosition = WorldPosition.WithX( _lastPosition.x );

		// Align tracer with travel direction
		if ( vel.LengthSquared > 1f )
			WorldRotation = Rotation.LookAt( vel.Normal, Vector3.Up );

		// Trace from last frame position to current — catches fast bullet hits
		var tr = Scene.Trace
			.Ray( _lastPosition, WorldPosition )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( tr.Hit )
		{
			// TODO: deal damage to tr.GameObject
			GameObject.Destroy();
			return;
		}

		_lastPosition = WorldPosition;
	}
}
