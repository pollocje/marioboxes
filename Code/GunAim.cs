using Sandbox;

public sealed class GunAim : Component
{
	[Property] public GameObject PlayerCenter { get; set; }
	[Property] public GameObject BarrelTip { get; set; }
	[Property] public float OrbitRadius { get; set; } = 60f;

	// The aim direction on the YZ plane, used by Shoot
	public Vector3 AimDir { get; private set; } = new Vector3( 0f, 1f, 0f );
	public Angles RotationOffset { get; set; } = Angles.Zero;

	protected override void OnUpdate()
	{
		var camera = Scene.Camera;
		if ( camera is null ) return;

		var center = PlayerCenter is not null ? PlayerCenter.WorldPosition : WorldPosition;

		// Project cursor onto the game plane (X = player X)
		var ray = camera.ScreenPixelToRay( Mouse.Position );
		if ( System.Math.Abs( ray.Forward.x ) < 0.001f ) return;

		float t = (center.x - ray.Position.x) / ray.Forward.x;
		var cursorWorld = ray.Position + ray.Forward * t;

		// Direction from player center to cursor on the YZ plane
		var delta = (cursorWorld - center).WithX( 0f );
		if ( delta.LengthSquared > 100f )
			AimDir = delta.Normal;

		// Rotate so the barrel points outward, applying per-weapon offset
		WorldRotation = Rotation.LookAt( AimDir, Vector3.Up ) * RotationOffset.ToRotation();

		// Place pivot on the circle
		WorldPosition = center + AimDir * OrbitRadius;

		// Shift gun perpendicular to AimDir until BarrelTip lies on the aim line
		if ( BarrelTip is not null )
		{
			var barrelPos = BarrelTip.WorldPosition.WithX( 0f );
			var centerYZ = center.WithX( 0f );
			var onLine = centerYZ + Vector3.Dot( barrelPos - centerYZ, AimDir ) * AimDir;
			WorldPosition -= barrelPos - onLine;
		}
	}
}
