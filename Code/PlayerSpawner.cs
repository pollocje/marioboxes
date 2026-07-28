using Sandbox;

// Place once in the scene. Handles turning a joining Connection into an actual networked player.
// Nothing else in the project currently calls NetworkSpawn — without this, players never exist
// for anyone but themselves.
public sealed class PlayerSpawner : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }

	void INetworkListener.OnActive( Connection channel )
	{
		if ( PlayerPrefab is null ) return;

		var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToList();
		var spawnPoint = spawnPoints.Count > 0
			? spawnPoints[Game.Random.Int( spawnPoints.Count - 1 )]
			: null;

		var player = PlayerPrefab.Clone(
			spawnPoint?.WorldPosition ?? Vector3.Zero,
			spawnPoint?.WorldRotation ?? Rotation.Identity
		);
		player.Name = $"Player - {channel.DisplayName}";
		player.NetworkSpawn( channel );
	}
}
