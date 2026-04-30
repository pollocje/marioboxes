using Sandbox;

public sealed class GunAim : Component
{
	[Property] public float OrbitRadius { get; set; } = 60f;
	[Property] public Vector3 PivotOffset { get; set; } = Vector3.Zero;

	private Vector3 _aimDir = new Vector3( 0f, 1f, 0f );
	private bool _facingRight = false;

	protected override void OnUpdate()
	{
		var camera = Scene.Camera;
		if ( camera is null ) return;

		var parent = GameObject.Parent;
		if ( parent is null ) return;

		// Screen-space angle from player to cursor — matches exactly where the reticle appears
		var playerScreen = camera.PointToScreenPixels( parent.WorldPosition );
		var diff = Mouse.Position - playerScreen;

		if ( diff.LengthSquared > 400f )
		{
			var worldDiff = camera.WorldRotation.Right * diff.x + camera.WorldRotation.Up * (-diff.y);
			var candidate = worldDiff.WithX( 0f );
			if ( candidate.LengthSquared > 0.001f )
				_aimDir = candidate.Normal;
		}

		// Hysteresis on left/right flip to avoid flicker near vertical
		if ( _aimDir.y > 0.15f ) _facingRight = false;
		else if ( _aimDir.y < -0.15f ) _facingRight = true;

		float elevDeg = (float)(System.Math.Atan2( _aimDir.z, System.Math.Abs( _aimDir.y ) ) * (180.0 / System.Math.PI));
		WorldRotation = _facingRight
			? Rotation.From( -elevDeg, -90, 0 )
			: Rotation.From( -elevDeg, 90, 0 );

		WorldPosition = parent.WorldPosition + _aimDir * OrbitRadius + WorldRotation * PivotOffset;
	}
}
