using Sandbox;

public sealed class CursorDebug : Component
{
	[Property] public GameObject Center { get; set; }

	protected override void OnUpdate()
	{
		var camera = Scene.Camera;
		if ( camera is null ) return;

		var origin = Center is not null ? Center.WorldPosition : WorldPosition;

		var ray = camera.ScreenPixelToRay( Mouse.Position );
		if ( System.Math.Abs( ray.Forward.x ) < 0.001f ) return;

		float t = (origin.x - ray.Position.x) / ray.Forward.x;
		var cursorWorld = ray.Position + ray.Forward * t;

		DebugOverlay.Sphere( new Sandbox.Sphere( cursorWorld, 5f ), Color.Red, 0.02f );
		DebugOverlay.Line( origin, cursorWorld, Color.Yellow, 0.02f );

	}
}
