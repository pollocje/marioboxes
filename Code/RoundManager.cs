using Sandbox;

// Host-authoritative round state for 5v5 Team Deathmatch. Place once in the scene.
// Win condition is first team to KillsToWin; MatchTimeLimit is a backstop so a round
// can't run forever if nobody gets there — whoever's ahead when time runs out wins.
public sealed class RoundManager : Component
{
	[Property] public int KillsToWin { get; set; } = 50;
	[Property] public float MatchTimeLimit { get; set; } = 900f; // 15 min
	[Property] public float IntermissionDuration { get; set; } = 10f;

	// Total players across both teams needed before the match timer starts ticking —
	// without this, the timer burns down from the moment the scene loads, even with
	// nobody connected yet.
	[Property] public int MinPlayersToStart { get; set; } = 2;

	public static RoundManager Instance { get; private set; }

	[Sync( SyncFlags.FromHost )] public int RedScore { get; private set; }
	[Sync( SyncFlags.FromHost )] public int BlueScore { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool RoundOver { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool WarmingUp { get; private set; } = true;
	[Sync( SyncFlags.FromHost )] public Team WinningTeam { get; private set; } = Team.Unassigned;

	private RealTimeSince _roundOverAt;

	protected override void OnStart()
	{
		Instance = this;
		StartNewRound();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		if ( RoundOver )
		{
			if ( _roundOverAt >= IntermissionDuration )
				StartNewRound();
			return;
		}

		if ( WarmingUp )
		{
			if ( CountConnectedPlayers() >= MinPlayersToStart )
				WarmingUp = false;
			return;
		}

		TimeRemaining -= Time.Delta;
		if ( TimeRemaining <= 0f )
		{
			TimeRemaining = 0f;
			var leader = RedScore == BlueScore ? Team.Unassigned : ( RedScore > BlueScore ? Team.Red : Team.Blue );
			EndRound( leader );
		}
	}

	// Called by whichever client's Health resolved a kill — that's always the victim's owner,
	// per Health.TakeDamage's [Rpc.Owner] — but this body only actually runs on the host, so a
	// kill is counted exactly once regardless of who reports it. attacker/victim are used only
	// for per-player PlayerStats; team totals (the actual win condition) key off attackerTeam.
	[Rpc.Host]
	public void ReportKill( Team attackerTeam, Team victimTeam, GameObject attacker, GameObject victim )
	{
		if ( RoundOver ) return;

		var victimStats = victim?.Components.Get<PlayerStats>();
		if ( victimStats is not null ) victimStats.Deaths++;

		// No kill credit for environmental/unattributed deaths or team-kills (the latter
		// shouldn't reach here anyway since Health blocks friendly-fire damage by default).
		if ( attackerTeam == Team.Unassigned || attackerTeam == victimTeam ) return;

		var attackerStats = attacker?.Components.Get<PlayerStats>();
		if ( attackerStats is not null ) attackerStats.Kills++;

		if ( attackerTeam == Team.Red ) RedScore++;
		else if ( attackerTeam == Team.Blue ) BlueScore++;

		if ( RedScore >= KillsToWin ) EndRound( Team.Red );
		else if ( BlueScore >= KillsToWin ) EndRound( Team.Blue );
	}

	private int CountConnectedPlayers()
	{
		return Scene.GetAllComponents<TeamMember>().Count( t => t.Team != Team.Unassigned );
	}

	private void EndRound( Team winner )
	{
		WinningTeam = winner;
		RoundOver = true;
		_roundOverAt = 0;
	}

	private void StartNewRound()
	{
		RedScore = 0;
		BlueScore = 0;
		TimeRemaining = MatchTimeLimit;
		WinningTeam = Team.Unassigned;
		RoundOver = false;
		WarmingUp = CountConnectedPlayers() < MinPlayersToStart;
	}
}
