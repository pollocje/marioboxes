using Sandbox;

/// <summary>
/// Locks world position to parent position + a fixed world-space offset.
/// Ignores parent rotation — use this to keep UI elements stationary above a rolling object.
/// </summary>
public sealed class WorldFollow : Component
{
	[Property] public Vector3 Offset { get; set; } = new Vector3( 0f, 0f, 80f );

	protected override void OnUpdate()
	{
		if ( GameObject.Parent is null ) return;
		WorldPosition = GameObject.Parent.WorldPosition + Offset;
	}
}
