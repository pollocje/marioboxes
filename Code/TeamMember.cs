using Sandbox;

// Lives on the player prefab. Team is assigned by PlayerSpawner on join and is
// host-authoritative — players don't get to pick or change their own team.
public sealed class TeamMember : Component
{
	[Sync( SyncFlags.FromHost )] public Team Team { get; set; } = Team.Unassigned;
}
