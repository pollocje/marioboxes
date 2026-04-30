using Sandbox;

public sealed class Movement : Component
{
	[Property] public float MoveSpeed { get; set; } = 300f;
	[Property] public float JumpForce { get; set; } = 500f;
	[Property] public float AirRollSpeed { get; set; } = 6f; // radians/sec

	private Rigidbody _rb;
	private bool _isGrounded;
	private bool _wasGrounded;

	protected override void OnStart()
	{
		_rb = Components.Get<Rigidbody>();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		_wasGrounded = _isGrounded;
		CheckGrounded();
		Move();
	}

	private void CheckGrounded()
	{
		var tr = Scene.Trace
			.Ray( WorldPosition, WorldPosition + Vector3.Down * 34f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		_isGrounded = tr.Hit;
	}

	private void Move()
	{
		float input = 0f;
		if ( Input.Down( "Left" ) ) input += 1f;
		if ( Input.Down( "Right" ) ) input -= 1f;

		var vel = _rb.Velocity;
		vel.x = 0f; // lock depth axis

		vel.y = input * MoveSpeed;

		if ( _isGrounded )
		{
			if ( Input.Pressed( "Jump" ) )
			{
				vel.z = JumpForce;

				// Kick off the roll at jump moment — physics carries it, we never touch it again in air
				if ( input != 0f )
					_rb.AngularVelocity = new Vector3( -input * AirRollSpeed, 0f, 0f );
			}

			// Just landed — snap rotation flat
			if ( !_wasGrounded )
				_rb.AngularVelocity = Vector3.Zero;
		}

		_rb.Velocity = vel;
	}
}
