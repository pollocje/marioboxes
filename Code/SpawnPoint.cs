using Sandbox;

public sealed class SpawnPoint : Component
{
	// Position comes from the GameObject's WorldPosition. Team.Unassigned spawns are
	// usable by either team (fallback if a team has no dedicated spawns of its own).
	[Property] public Team Team { get; set; } = Team.Unassigned;
}
