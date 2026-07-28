using Sandbox;

// Place once in the scene. Handles turning a joining Connection into an actual networked player,
// balanced onto whichever team has fewer players and hard-capped at MaxPlayersPerTeam. If both
// teams are already full when someone joins, they're queued instead of spawned — no player
// GameObject exists for them at all until a slot opens. Note this is queue logic only: there's no
// spectator camera/UI here, no asset support for that in this checkout — a queued player currently
// just... waits, with nothing to look at. See CLAUDE.md.
public sealed class PlayerSpawner : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public int MaxPlayersPerTeam { get; set; } = 5;

	private readonly List<Connection> _spectatorQueue = new();

	void INetworkListener.OnActive( Connection channel )
	{
		if ( PlayerPrefab is null ) return;

		var team = PickTeamWithRoom();
		if ( team == Team.Unassigned )
		{
			_spectatorQueue.Add( channel );
			return;
		}

		SpawnPlayer( channel, team );
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _spectatorQueue.Count == 0 ) return;

		// Drop anyone who disconnected while still queued — Connection.All is the live set of
		// connected players, so anything missing from it left before ever getting a slot.
		_spectatorQueue.RemoveAll( c => !Connection.All.Contains( c ) );

		while ( _spectatorQueue.Count > 0 )
		{
			var team = PickTeamWithRoom();
			if ( team == Team.Unassigned ) break; // both teams still full

			var next = _spectatorQueue[0];
			_spectatorQueue.RemoveAt( 0 );
			SpawnPlayer( next, team );
		}
	}

	private void SpawnPlayer( Connection channel, Team team )
	{
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

	// Returns Team.Unassigned if both teams are already at MaxPlayersPerTeam.
	private Team PickTeamWithRoom()
	{
		int redCount = CountTeam( Team.Red );
		int blueCount = CountTeam( Team.Blue );

		bool redOpen = redCount < MaxPlayersPerTeam;
		bool blueOpen = blueCount < MaxPlayersPerTeam;

		if ( !redOpen && !blueOpen ) return Team.Unassigned;
		if ( redOpen && ( !blueOpen || redCount <= blueCount ) ) return Team.Red;
		return Team.Blue;
	}

	private int CountTeam( Team team )
	{
		return Scene.GetAllComponents<TeamMember>().Count( t => t.Team == team );
	}
}
