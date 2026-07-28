using Sandbox;

public sealed class Bullet : Component
{
	[Property] public float MaxLifetime { get; set; } = 3f;
	[Property] public float MaxRange { get; set; } = 2000f;
	[Property] public float Damage { get; set; } = 10f;

	public Vector3 Velocity { get; set; }
	public GameObject Source { get; set; }

	// Set true on the copies broadcast to non-owning clients — they fly for visual feedback
	// only and never trace or deal damage, so a hit is never resolved more than once.
	public bool IsCosmetic { get; set; }

	private Vector3 _startPosition;
	private float _age;
	private float _planeX;

	protected override void OnStart()
	{
		_startPosition = WorldPosition;
		_planeX = WorldPosition.x;
	}

	protected override void OnFixedUpdate()
	{
		// Orient tracer along travel direction (done here so Velocity is guaranteed set)
		if ( Velocity.LengthSquared > 0f )
			WorldRotation = Rotation.LookAt( Velocity.Normal, Vector3.Up );


		var lastPos = WorldPosition;
		var nextPos = (lastPos + Velocity * Time.Delta).WithX( _planeX );

		if ( !IsCosmetic )
		{
			// Sweep from last to next position to catch hits without tunneling
			var tr = Scene.Trace.Ray( lastPos, nextPos )
				.IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( Source )
				.Run();

			if ( tr.Hit )
			{
				var health = tr.GameObject.Components.Get<Health>()
					?? tr.GameObject.Components.GetInParent<Health>();
				health?.TakeDamage( Damage ); // routes through Health's [Rpc.Owner] — resolves on the victim's machine
				GameObject.Destroy();
				return;
			}
		}

		WorldPosition = nextPos;

		_age += Time.Delta;
		if ( _age >= MaxLifetime || (WorldPosition - _startPosition).Length >= MaxRange )
		{
			GameObject.Destroy();
		}
	}
}
