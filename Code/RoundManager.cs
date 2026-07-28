using Sandbox;

// Host-authoritative round state for 5v5 Team Deathmatch. Place once in the scene.
// Win condition is first team to KillsToWin; MatchTimeLimit is a backstop so a round
// can't run forever if nobody gets there — tied at the backstop goes to Overtime
// (sudden death) instead of ending in a draw.
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

	// Fires locally on every client whenever ReportKill processes a death — killerName,
	// victimName, weaponName, killerTeam, victimTeam. Purely a plumbing layer: no UI reads
	// this yet, but a future killfeed just subscribes here instead of needing its own RPC.
	public static event System.Action<string, string, string, Team, Team> OnKillFeedEvent;

	[Sync( SyncFlags.FromHost )] public int RedScore { get; private set; }
	[Sync( SyncFlags.FromHost )] public int BlueScore { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TimeRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool RoundOver { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool WarmingUp { get; private set; } = true;
	[Sync( SyncFlags.FromHost )] public bool Overtime { get; private set; }
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

		// A team disconnecting down to nobody mid-round ends it early, rather than the other
		// team grinding out an uncontested win or Overtime waiting forever for a kill that can
		// never come. Polled here every frame instead of hooking a join/leave callback because
		// it isn't clear from this checkout whether a disconnecting player's TeamMember is
		// destroyed before or after such a callback fires — polling doesn't depend on that
		// ordering at all, and this loop already runs every frame for the warmup check above.
		int redCount = CountTeam( Team.Red );
		int blueCount = CountTeam( Team.Blue );
		if ( redCount == 0 || blueCount == 0 )
		{
			if ( redCount == 0 && blueCount == 0 )
				StartNewRound(); // nobody left at all — don't "end" a round no one's playing, just reset
			else
				EndRound( redCount > 0 ? Team.Red : Team.Blue );
			return;
		}

		if ( Overtime ) return; // sudden death — no clock, waiting on ReportKill to end it

		TimeRemaining -= Time.Delta;
		if ( TimeRemaining <= 0f )
		{
			TimeRemaining = 0f;
			if ( RedScore == BlueScore )
				Overtime = true;
			else
				EndRound( RedScore > BlueScore ? Team.Red : Team.Blue );
		}
	}

	// Called by whichever client's Health resolved a kill — that's always the victim's owner,
	// per Health.TakeDamage's [Rpc.Owner] — but this body only actually runs on the host, so a
	// kill is counted exactly once regardless of who reports it. attacker/victim are used only
	// for per-player PlayerStats; team totals (the actual win condition) key off attackerTeam.
	[Rpc.Host]
	public void ReportKill( Team attackerTeam, Team victimTeam, GameObject attacker, GameObject victim, string weaponName )
	{
		if ( RoundOver ) return;

		var victimStats = victim?.Components.Get<PlayerStats>();
		if ( victimStats is not null ) victimStats.Deaths++;

		// Every death is worth reporting to the killfeed, even ones with no kill credit
		// (team-kills, unattributed damage) — a future UI can decide how to display those.
		BroadcastKillFeed( attacker?.Name ?? "World", victim?.Name ?? "Unknown", weaponName ?? "", attackerTeam, victimTeam );

		// No kill credit for environmental/unattributed deaths or team-kills (the latter
		// shouldn't reach here anyway since Health blocks friendly-fire damage by default).
		if ( attackerTeam == Team.Unassigned || attackerTeam == victimTeam ) return;

		var attackerStats = attacker?.Components.Get<PlayerStats>();
		if ( attackerStats is not null ) attackerStats.Kills++;

		if ( attackerTeam == Team.Red ) RedScore++;
		else if ( attackerTeam == Team.Blue ) BlueScore++;

		if ( Overtime )
		{
			EndRound( attackerTeam ); // sudden death — the first kill in overtime wins outright
			return;
		}

		if ( RedScore >= KillsToWin ) EndRound( Team.Red );
		else if ( BlueScore >= KillsToWin ) EndRound( Team.Blue );
	}

	// Broadcasts so OnKillFeedEvent fires identically on every client, not just the host.
	[Rpc.Broadcast]
	private void BroadcastKillFeed( string killerName, string victimName, string weaponName, Team killerTeam, Team victimTeam )
	{
		OnKillFeedEvent?.Invoke( killerName, victimName, weaponName, killerTeam, victimTeam );
	}

	private int CountTeam( Team team )
	{
		return Scene.GetAllComponents<TeamMember>().Count( t => t.Team == team );
	}

	private int CountConnectedPlayers()
	{
		return CountTeam( Team.Red ) + CountTeam( Team.Blue );
	}

	private void EndRound( Team winner )
	{
		WinningTeam = winner;
		RoundOver = true;
		Overtime = false;
		_roundOverAt = 0;
	}

	private void StartNewRound()
	{
		RedScore = 0;
		BlueScore = 0;
		TimeRemaining = MatchTimeLimit;
		WinningTeam = Team.Unassigned;
		RoundOver = false;
		Overtime = false;
		WarmingUp = CountConnectedPlayers() < MinPlayersToStart;
	}
}
