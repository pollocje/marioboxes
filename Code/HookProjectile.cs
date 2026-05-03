using Sandbox;

public sealed class HookProjectile : Component
{
	[Property] public float MaxLifetime { get; set; } = 2f;

	public Vector3 Velocity { get; set; }
	public GrapplingHook Source { get; set; }
	public float PlaneX { get; set; }

	private float _age;

	protected override void OnFixedUpdate()
	{
		var lastPos = WorldPosition;
		var nextPos = (lastPos + Velocity * Time.Delta).WithX( PlaneX );

		var tr = Scene.Trace.Ray( lastPos, nextPos )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreGameObjectHierarchy( Source?.GameObject )
			.Run();

		if ( tr.Hit )
		{
			// Only hook onto static geometry — players have a Health component
			var health = tr.GameObject.Components.Get<Health>()
				?? tr.GameObject.Components.GetInParent<Health>();

			if ( health is null )
				Source?.OnHookLanded( tr.HitPosition );
			else
				Source?.OnHookMissed();

			GameObject.Destroy();
			return;
		}

		WorldPosition = nextPos;
		_age += Time.Delta;

		if ( _age >= MaxLifetime )
		{
			Source?.OnHookMissed();
			GameObject.Destroy();
		}
	}
}
