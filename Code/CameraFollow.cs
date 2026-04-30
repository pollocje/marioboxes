using Sandbox;

/// <summary>
/// Place on the camera child of the player.
/// Freezes the camera's initial local offset and rotation in world space,
/// so box spins don't affect the view.
/// </summary>
public sealed class CameraFollow : Component
{
	private Vector3 _localOffset;
	private Rotation _fixedRotation;

	protected override void OnStart()
	{
		_localOffset = LocalPosition;
		_fixedRotation = WorldRotation;
	}

	protected override void OnUpdate()
	{
		WorldPosition = GameObject.Parent.WorldPosition + _localOffset;
		WorldRotation = _fixedRotation;
	}
}
