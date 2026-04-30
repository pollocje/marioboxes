using Sandbox;

public sealed class Shoot : Component
{
	[Property] public GameObject BulletPrefab { get; set; }
	[Property] public float FireRate { get; set; } = 10f;
	[Property] public float BulletSpeed { get; set; } = 4000f;

	private float _nextFireTime;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( Input.Down( "attack1" ) && Time.Now >= _nextFireTime )
		{
			Fire();
			_nextFireTime = Time.Now + 1f / FireRate;
		}
	}

	private void Fire()
	{
		if ( BulletPrefab is null ) return;

		var camera = Scene.Camera;
		if ( camera is null ) return;

		// Perspective ray through cursor to find cursor world position on the 2D play plane
		float tanHalfFov = (float)System.Math.Tan( camera.FieldOfView * System.Math.PI / 360.0 );
		float aspect = (float)Screen.Width / Screen.Height;
		var ndc = new Vector2(
			Mouse.Position.x / Screen.Width * 2f - 1f,
			1f - Mouse.Position.y / Screen.Height * 2f
		);
		var rayDir = (camera.WorldRotation.Forward
			+ camera.WorldRotation.Right * (ndc.x * aspect * tanHalfFov)
			+ camera.WorldRotation.Up * (ndc.y * tanHalfFov)).Normal;

		float planeX = WorldPosition.x;
		if ( System.Math.Abs( rayDir.x ) < 0.001f ) return;

		float t = (planeX - camera.WorldPosition.x) / rayDir.x;
		var cursorWorld = camera.WorldPosition + rayDir * t;

		// Fire from gun toward cursor, staying on the 2D plane
		var fireDir = (cursorWorld - WorldPosition).WithX( 0f );
		if ( fireDir.LengthSquared < 0.001f ) return;
		fireDir = fireDir.Normal;

		var bullet = BulletPrefab.Clone( WorldPosition, Rotation.LookAt( fireDir, Vector3.Up ) );
		var rb = bullet.Components.Get<Rigidbody>();
		if ( rb is not null )
			rb.Velocity = fireDir * BulletSpeed;
	}
}
