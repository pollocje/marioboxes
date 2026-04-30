using Sandbox;

public sealed class Shoot : Component
{
	[Property] public GameObject BulletPrefab { get; set; }
	[Property] public GameObject BarrelPoint { get; set; }
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

		// Use built-in ray from screen pixel to avoid manual projection errors
		var ray = camera.ScreenPixelToRay( Mouse.Position );

		float planeX = WorldPosition.x;
		if ( System.Math.Abs( ray.Forward.x ) < 0.001f ) return;

		float t = (planeX - ray.Position.x) / ray.Forward.x;
		var cursorWorld = ray.Position + ray.Forward * t;

		var spawnPos = BarrelPoint is not null ? BarrelPoint.WorldPosition : WorldPosition;

		// Fire from barrel toward cursor, staying on the 2D plane
		var fireDir = (cursorWorld - spawnPos).WithX( 0f );
		if ( fireDir.LengthSquared < 0.001f ) return;
		fireDir = fireDir.Normal;

		var bullet = BulletPrefab.Clone( spawnPos, Rotation.LookAt( fireDir, Vector3.Up ) );
		var rb = bullet.Components.Get<Rigidbody>();
		if ( rb is not null )
			rb.Velocity = fireDir * BulletSpeed;
	}
}
