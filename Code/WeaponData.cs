using Sandbox;

public sealed class WeaponData : Component
{
	[Property] public GameObject BarrelTip { get; set; }
	[Property] public float Damage { get; set; } = 10f;
	[Property] public float FireRate { get; set; } = 0.15f;
	[Property] public float BulletSpeed { get; set; } = 1500f;
	[Property] public int MagSize { get; set; } = 10;
	[Property] public float ReloadTime { get; set; } = 3f;
	[Property] public float OrbitRadius { get; set; } = 0f; // 0 = use GunAim default
	[Property] public Angles RotationOffset { get; set; } = Angles.Zero;
	[Property] public SoundEvent FireSound { get; set; }
	[Property] public SoundEvent ReloadSound { get; set; }
	[Property] public GameObject MuzzleFlashPrefab { get; set; }
}
