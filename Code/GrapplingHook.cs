using Sandbox;

public sealed class GrapplingHook : Component
{
	[Property] public float HookSpeed { get; set; } = 1500f;
	[Property] public float MaxRopeLength { get; set; } = 400f;
	[Property] public float HookPullForce { get; set; } = 350f;
	[Property] public GameObject PlayerCenter { get; set; }

	public bool IsHooked => State == HookState.Hooked;

	private enum HookState { Idle, Firing, Hooked }

	// Synced so proxies can draw the rope and know the current phase — only the owner writes
	// these. Previously OnUpdate bailed out entirely for proxies before the rope-drawing code
	// even ran, so nobody but the owner ever saw their own grappling rope.
	[Sync] private HookState State { get; set; } = HookState.Idle;
	[Sync] private Vector3 HookPoint { get; set; }
	[Sync] private Vector3 FiringHeadPoint { get; set; }

	private GameObject _hookGO;
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
		if ( !IsProxy )
		{
			if ( Input.Pressed( "attack2" ) || ( State != HookState.Idle && Input.Pressed( "Jump" ) ) )
			{
				if ( State == HookState.Idle )
					Fire();
				else
					Detach();
			}

			// _hookGO only ever exists on the owner's machine (it's a plain local GameObject, never
			// networked) — mirror its live position into synced state so proxies can draw it too.
			if ( State == HookState.Firing && _hookGO is not null )
				FiringHeadPoint = _hookGO.WorldPosition;
		}

		// Rope visual — driven entirely by synced state, so it renders the same for owner and proxies.
		var ropeStart = PlayerCenter?.WorldPosition ?? WorldPosition;
		if ( State == HookState.Firing )
		{
			_rope.Enabled = true;
			_rope.VectorPoints = new List<Vector3> { ropeStart, FiringHeadPoint };
		}
		else if ( State == HookState.Hooked )
		{
			_rope.Enabled = true;
			_rope.VectorPoints = new List<Vector3> { ropeStart, HookPoint };
		}
		else
		{
			_rope.Enabled = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		if ( State != HookState.Hooked ) return;

		var toHook = HookPoint - WorldPosition;
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

		FiringHeadPoint = origin;
		State = HookState.Firing;
	}

	public void OnHookLanded( Vector3 point )
	{
		HookPoint = point;
		_ropeLength = (HookPoint - WorldPosition).Length;
		_hookGO?.Destroy();
		_hookGO = null;
		State = HookState.Hooked;
	}

	public void OnHookMissed()
	{
		_hookGO = null;
		State = HookState.Idle;
	}

	public void Detach()
	{
		_hookGO?.Destroy();
		_hookGO = null;
		State = HookState.Idle;
	}

	protected override void OnDestroy()
	{
		_hookGO?.Destroy();
	}
}
