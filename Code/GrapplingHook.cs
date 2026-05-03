using Sandbox;

public sealed class GrapplingHook : Component
{
	[Property] public float HookSpeed { get; set; } = 1500f;
	[Property] public float MaxRopeLength { get; set; } = 400f;
	[Property] public float HookPullForce { get; set; } = 350f;
	[Property] public GameObject PlayerCenter { get; set; }

	public bool IsHooked => _state == HookState.Hooked;

	private enum HookState { Idle, Firing, Hooked }
	private HookState _state = HookState.Idle;
	private GameObject _hookGO;
	private Vector3 _hookPoint;
	private float _ropeLength;
	private GunAim _gunAim;
	private Rigidbody _rb;
	private LineRenderer _rope;

	protected override void OnStart()
	{
		_gunAim = Components.Get<GunAim>( FindMode.EverythingInSelfAndDescendants );
		_rb = Components.Get<Rigidbody>();
		_rope = Components.Get<LineRenderer>( FindMode.EverythingInSelfAndDescendants );

		if ( _rope is not null )
		{
			_rope.GameObject.Parent = null; // detach from player so world positions aren't double-offset
			_rope.UseVectorPoints = true;
			_rope.Enabled = false;
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( Input.Pressed( "attack2" ) || ( _state != HookState.Idle && Input.Pressed( "Jump" ) ) )
		{
			if ( _state == HookState.Idle )
				Fire();
			else
				Detach();
		}

		// Update rope visual
		var ropeStart = PlayerCenter?.WorldPosition ?? WorldPosition;
		if ( _state == HookState.Firing && _hookGO is not null )
		{
			_rope.Enabled = true;
			_rope.VectorPoints = new System.Collections.Generic.List<Vector3> { ropeStart, _hookGO.WorldPosition };
		}
		else if ( _state == HookState.Hooked )
		{
			_rope.Enabled = true;
			_rope.VectorPoints = new System.Collections.Generic.List<Vector3> { ropeStart, _hookPoint };
		}
		else
		{
			_rope.Enabled = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		if ( _state != HookState.Hooked ) return;

		var toHook = _hookPoint - WorldPosition;
		float dist = toHook.Length;
		var ropeDir = toHook.Normal;

		// Constant pull toward hook — pulls you into the swing naturally
		_rb.Velocity += ropeDir * HookPullForce * Time.Delta;

		// Cancel outward radial velocity when rope is taut — preserves tangential swing
		if ( dist > _ropeLength )
		{
			float velAlongRope = Vector3.Dot( _rb.Velocity, ropeDir );
			if ( velAlongRope < 0f )
				_rb.Velocity -= ropeDir * velAlongRope;
		}
	}

	private void Fire()
	{
		if ( _gunAim is null ) return;

		var origin = PlayerCenter?.WorldPosition ?? WorldPosition;

		_hookGO = new GameObject( true, "HookProjectile" );
		_hookGO.WorldPosition = origin;

		var proj = _hookGO.Components.Create<HookProjectile>();
		proj.Velocity = _gunAim.AimDir * HookSpeed;
		proj.Source = this;
		proj.PlaneX = origin.x;

		_state = HookState.Firing;
	}

	public void OnHookLanded( Vector3 point )
	{
		_hookPoint = point;
		_ropeLength = (_hookPoint - WorldPosition).Length;
		_hookGO?.Destroy();
		_hookGO = null;
		_state = HookState.Hooked;
	}

	public void OnHookMissed()
	{
		_hookGO = null;
		_state = HookState.Idle;
	}

	public void Detach()
	{
		_hookGO?.Destroy();
		_hookGO = null;
		_state = HookState.Idle;
	}

	protected override void OnDestroy()
	{
		_hookGO?.Destroy();
	}
}
