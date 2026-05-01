using Sandbox;

public sealed class WeaponHolder : Component
{
	[Property] public GameObject StartingWeaponPrefab { get; set; }

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

		if ( StartingWeaponPrefab is not null )
			Equip( StartingWeaponPrefab );
	}

	public void Equip( GameObject weaponPrefab )
	{
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
