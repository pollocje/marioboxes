using Sandbox;

// Lives on the player prefab. Host-authoritative per-player kill/death counts —
// separate from RoundManager's team totals, which stay the source of truth for
// the win condition. This exists for a future scoreboard; nothing reads it yet.
public sealed class PlayerStats : Component
{
	[Sync( SyncFlags.FromHost )] public int Kills { get; set; }
	[Sync( SyncFlags.FromHost )] public int Deaths { get; set; }
}
