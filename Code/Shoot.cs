using Sandbox;

public sealed class Shoot : Component
{
	[Property] public GameObject BulletPrefab { get; set; }
	[Property] public float FireRate { get; set; } = 0.15f;
	[Property] public float BulletSpeed { get; set; } = 1500f;
	[Property] public float Damage { get; set; } = 10f;
	[Property] public int MagSize { get; set; } = 10;
	[Property] public float ReloadTime { get; set; } = 3f;

	public int CurrentAmmo { get; private set; }
	public bool IsReloading { get; private set; }
	public float ReloadFraction => IsReloading ? (float)(_reloadStarted / ReloadTime) : 1f;

	private GunAim _gunAim;
	private RealTimeSince _lastFired;
	private RealTimeSince _reloadStarted;
	private SoundEvent _fireSound;
	private SoundEvent _reloadSound;
	private GameObject _muzzleFlashPrefab;

	protected override void OnStart()
	{
		_gunAim = Components.Get<GunAim>();
		CurrentAmmo = MagSize;
	}

	public void ApplyWeaponData( WeaponData data )
	{
		FireRate = data.FireRate;
		BulletSpeed = data.BulletSpeed;
		Damage = data.Damage;
		MagSize = data.MagSize;
		ReloadTime = data.ReloadTime;
		CurrentAmmo = data.MagSize;
		IsReloading = false;
		_fireSound = data.FireSound;
		_reloadSound = data.ReloadSound;
		_muzzleFlashPrefab = data.MuzzleFlashPrefab;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( BulletPrefab is null || _gunAim?.BarrelTip is null ) return;
		if ( RoundManager.Instance is not null && ( RoundManager.Instance.RoundOver || RoundManager.Instance.WarmingUp ) ) return;

		if ( IsReloading )
		{
			if ( _reloadStarted >= ReloadTime )
			{
				IsReloading = false;
				CurrentAmmo = MagSize;
			}
			return;
		}

		if ( Input.Down( "attack1" ) && _lastFired >= FireRate && CurrentAmmo > 0 )
		{
			Fire();
			_lastFired = 0;
		}
	}

	private void Fire()
	{
		var aimDir = _gunAim.AimDir;
		var barrelPos = _gunAim.BarrelTip.WorldPosition;

		// Firing forfeits spawn protection — see Health.ClearSpawnProtection.
		GameObject.Parent?.Components.Get<Health>()?.ClearSpawnProtection();

		// Authoritative bullet — only ever exists on this client. It resolves the hit and deals
		// damage; see Bullet.IsCosmetic for why other clients get a separate, non-authoritative copy.
		var bullet = BulletPrefab.Clone( barrelPos );
		bullet.WorldRotation = Rotation.LookAt( aimDir, Vector3.Up );
		var bulletComp = bullet.Components.Get<Bullet>();
		if ( bulletComp is not null )
		{
			bulletComp.Velocity = aimDir * BulletSpeed;
			bulletComp.Damage = Damage;
			bulletComp.Source = GameObject.Parent;
			// WeaponHolder.CurrentWeaponId, not this GameObject's own Name — a reliable already-synced
			// id rather than relying on how Clone() happens to name the runtime weapon instance.
			bulletComp.WeaponName = GameObject.Parent?.Components.Get<WeaponHolder>()?.CurrentWeaponId;
		}

		FireEffects( barrelPos, aimDir );

		CurrentAmmo--;
		if ( CurrentAmmo <= 0 )
		{
			IsReloading = true;
			_reloadStarted = 0;
			PlayReloadSound( barrelPos );
		}
	}

	// Broadcasts the parts of firing that everyone should see/hear — without this, only the
	// shooter themselves ever sees a muzzle flash, hears the gun, or sees any sign a bullet fired.
	[Rpc.Broadcast]
	private void FireEffects( Vector3 barrelPos, Vector3 aimDir )
	{
		if ( _muzzleFlashPrefab is not null )
		{
			var flash = _muzzleFlashPrefab.Clone( barrelPos );
			flash.WorldRotation = Rotation.LookAt( aimDir, Vector3.Up );
			if ( flash.Components.Get<AutoDestroy>() is null )
				flash.Components.Create<AutoDestroy>();
		}

		if ( _fireSound is not null )
			Sound.Play( _fireSound, barrelPos );

		// The owner already spawned the authoritative bullet locally in Fire() — everyone else
		// gets a cosmetic-only tracer so the shot is actually visible to them.
		if ( IsProxy && BulletPrefab is not null )
		{
			var bullet = BulletPrefab.Clone( barrelPos );
			bullet.WorldRotation = Rotation.LookAt( aimDir, Vector3.Up );
			var bulletComp = bullet.Components.Get<Bullet>();
			if ( bulletComp is not null )
			{
				bulletComp.Velocity = aimDir * BulletSpeed;
				bulletComp.IsCosmetic = true;
			}
		}
	}

	[Rpc.Broadcast]
	private void PlayReloadSound( Vector3 pos )
	{
		if ( _reloadSound is not null )
			Sound.Play( _reloadSound, pos );
	}
}
