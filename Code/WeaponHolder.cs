using Sandbox;

public sealed class WeaponHolder : Component
{
	[Property] public GameObject StartingWeaponPrefab { get; set; }

	// Every weapon prefab this holder might ever equip (starting weapon + every pickup it can reach).
	// GameObject *asset* references don't sync — only spawned/networked GameObjects do — so instead
	// we sync a weapon's name as an id and every client resolves it against its own local copy of
	// this list, which is identical across clients since it comes from the same project.
	[Property] public List<GameObject> WeaponRegistry { get; set; } = new();

	[Sync] public string CurrentWeaponId { get; private set; }

	public GameObject CurrentWeapon { get; private set; }

	private GunAim _gunAim;
	private Shoot _shoot;
	private float _defaultOrbitRadius;

	protected override void OnStart()
	{
		_gunAim = Components.Get<GunAim>();
		_shoot = Components.Get<Shoot>();

		if ( _gunAim is not null )
			_defaultOrbitRadius = _gunAim.OrbitRadius;

		if ( !string.IsNullOrEmpty( CurrentWeaponId ) )
		{
			// Late join, or this component starting after another player already has a weapon
			// synced — apply what's already replicated instead of re-granting the starting weapon.
			EquipLocal( ResolveWeaponId( CurrentWeaponId ) );
		}
		else if ( !IsProxy && StartingWeaponPrefab is not null )
		{
			Equip( StartingWeaponPrefab );
		}
	}

	// Call from whichever client has authority to grant the weapon: the owner for their starting
	// weapon, or the host once it has arbitrated a WeaponPickup claim.
	public void Equip( GameObject weaponPrefab )
	{
		if ( weaponPrefab is null ) return;
		EquipBroadcast( weaponPrefab.Name );
	}

	// Broadcasts so every client — including whichever one called it — applies the same visuals
	// and weapon stats at the same time, instead of only the caller's machine knowing about it.
	[Rpc.Broadcast]
	private void EquipBroadcast( string weaponId )
	{
		CurrentWeaponId = weaponId;
		EquipLocal( ResolveWeaponId( weaponId ) );
	}

	private GameObject ResolveWeaponId( string id )
	{
		if ( string.IsNullOrEmpty( id ) ) return null;

		if ( StartingWeaponPrefab is not null && StartingWeaponPrefab.Name == id )
			return StartingWeaponPrefab;

		return WeaponRegistry.FirstOrDefault( w => w is not null && w.Name == id );
	}

	private void EquipLocal( GameObject weaponPrefab )
	{
		if ( weaponPrefab is null ) return;

		CurrentWeapon?.Destroy();

		CurrentWeapon = weaponPrefab.Clone( Vector3.Zero );
		CurrentWeapon.Parent = GameObject;
		CurrentWeapon.LocalPosition = Vector3.Zero;
		CurrentWeapon.LocalRotation = Rotation.Identity;

		// Disable physics on the equipped weapon — only needed for world pickups
		CurrentWeapon.Components.Get<Rigidbody>( FindMode.EverythingInSelf )?.Destroy();
		CurrentWeapon.Components.Get<Collider>( FindMode.EverythingInSelf )?.Destroy();

		var data = CurrentWeapon.Components.Get<WeaponData>();
		if ( data is null ) return;

		if ( _gunAim is not null )
		{
			_gunAim.BarrelTip = data.BarrelTip;
			_gunAim.OrbitRadius = data.OrbitRadius > 0f ? data.OrbitRadius : _defaultOrbitRadius;
			_gunAim.RotationOffset = data.RotationOffset;
		}

		_shoot?.ApplyWeaponData( data );
	}
}
