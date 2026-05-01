using Sandbox;

public sealed class WeaponPickup : Component
{
	[Property] public GameObject WeaponPrefab { get; set; }
	[Property] public float RespawnTime { get; set; } = 15f;
	[Property] public float PickupRadius { get; set; } = 40f;
	[Property] public float BobAmplitude { get; set; } = 5f;
	[Property] public float BobSpeed { get; set; } = 2f;

	private bool _available = true;
	private RealTimeSince _pickedUpAt;
	private GameObject _visual;

	protected override void OnStart()
	{
		SpawnVisual();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( WeaponPrefab is null ) return;

		if ( !_available )
		{
			if ( _pickedUpAt >= RespawnTime )
				SetAvailable( true );
			return;
		}

		foreach ( var holder in Scene.GetAllComponents<WeaponHolder>() )
		{
			if ( (holder.WorldPosition - WorldPosition).Length <= PickupRadius )
			{
				holder.Equip( WeaponPrefab );
				SetAvailable( false );
				break;
			}
		}
	}

	private void SetAvailable( bool available )
	{
		_available = available;

		if ( !available )
		{
			_pickedUpAt = 0;
			_visual?.Destroy();
			_visual = null;
		}
		else
		{
			SpawnVisual();
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
