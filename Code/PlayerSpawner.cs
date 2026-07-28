using Sandbox;

// Place once in the scene. Handles turning a joining Connection into an actual networked player,
// balanced onto whichever team currently has fewer players (5v5 isn't hard-capped yet — see
// CLAUDE.md TODO). Nothing else in the project calls NetworkSpawn — without this, players never
// exist for anyone but themselves.
public sealed class PlayerSpawner : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }

	void INetworkListener.OnActive( Connection channel )
	{
		if ( PlayerPrefab is null ) return;

		var team = PickBalancedTeam();

		var spawnPoints = Scene.GetAllComponents<SpawnPoint>()
			.Where( sp => sp.Team == team || sp.Team == Team.Unassigned )
			.ToList();
		if ( spawnPoints.Count == 0 )
			spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToList();

		var spawnPoint = spawnPoints.Count > 0
			? spawnPoints[Game.Random.Int( spawnPoints.Count - 1 )]
			: null;

		var player = PlayerPrefab.Clone(
			spawnPoint?.WorldPosition ?? Vector3.Zero,
			spawnPoint?.WorldRotation ?? Rotation.Identity
		);
		player.Name = $"Player - {channel.DisplayName}";
		player.NetworkSpawn( channel );

		var teamMember = player.Components.Get<TeamMember>();
		if ( teamMember is not null )
			teamMember.Team = team;
	}

	private Team PickBalancedTeam()
	{
		int redCount = Scene.GetAllComponents<TeamMember>().Count( t => t.Team == Team.Red );
		int blueCount = Scene.GetAllComponents<TeamMember>().Count( t => t.Team == Team.Blue );
		return redCount <= blueCount ? Team.Red : Team.Blue;
	}
}
