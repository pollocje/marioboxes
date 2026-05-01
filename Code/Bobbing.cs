using Sandbox;

public sealed class Bobbing : Component
{
	[Property] public float Amplitude { get; set; } = 5f;  // units up/down
	[Property] public float Speed { get; set; } = 2f;      // cycles per second

	private Vector3 _startLocalPosition;

	protected override void OnStart()
	{
		_startLocalPosition = LocalPosition;
	}

	protected override void OnUpdate()
	{
		LocalPosition = _startLocalPosition + Vector3.Up * (float)System.Math.Sin( Time.Now * Speed ) * Amplitude;
	}
}
