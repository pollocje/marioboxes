using Sandbox;

public sealed class WeaponPickup : Component
{
	[Property] public GameObject WeaponPrefab { get; set; }
	[Property] public float RespawnTime { get; set; } = 15f;
	[Property] public float PickupRadius { get; set; } = 40f;
	[Property] public float BobAmplitude { get; set; } = 5f;
	[Property] public float BobSpeed { get; set; } = 2f;

	// Host-authoritative: previously every client independently scanned every WeaponHolder in the
	// scene and granted the pickup with no ownership check at all, so any client could hand this
	// weapon to any player, and two clients could both claim it in the same tick. The host is now
	// the only one that decides; everyone else just reflects Available via [Sync].
	[Sync] private bool Available { get; set; } = true;

	private RealTimeSince _pickedUpAt;
	private GameObject _visual;
	private bool _lastVisualState = true;

	protected override void OnStart()
	{
		SpawnVisual();
	}

	protected override void OnUpdate()
	{
		if ( Available != _lastVisualState )
		{
			_lastVisualState = Available;
			if ( Available )
				SpawnVisual();
			else
			{
				_visual?.Destroy();
				_visual = null;
			}
		}

		if ( !Networking.IsHost ) return;
		if ( WeaponPrefab is null ) return;

		if ( !Available )
		{
			if ( _pickedUpAt >= RespawnTime )
				Available = true;
			return;
		}

		foreach ( var holder in Scene.GetAllComponents<WeaponHolder>() )
		{
			if ( (holder.WorldPosition - WorldPosition).Length <= PickupRadius )
			{
				holder.Equip( WeaponPrefab );
				Available = false;
				_pickedUpAt = 0;
				break;
			}
		}
	}

	private void SpawnVisual()
	{
		if ( WeaponPrefab is null ) return;

		_visual = WeaponPrefab.Clone( WorldPosition );
		_visual.Parent = GameObject;
		_visual.LocalPosition = Vector3.Zero;
		_visual.LocalRotation = Rotation.Identity;

		// Remove physics — this is display only
		_visual.Components.Get<Rigidbody>( FindMode.EverythingInSelf )?.Destroy();
		_visual.Components.Get<Collider>( FindMode.EverythingInSelf )?.Destroy();

		// Add bobbing
		var bobbing = _visual.Components.Create<Bobbing>();
		bobbing.Amplitude = BobAmplitude;
		bobbing.Speed = BobSpeed;
	}
}
