namespace ToxVoice.Persistence;

public static class ToxVoiceViolationCooldownCache
{
	private static readonly Dictionary<string, long> PlayerViolationCooldowns = new();

	public static void SetPlayerCooldown(string playerId, int cooldownSeconds)
	{
		if (string.IsNullOrEmpty(playerId))
			return;

		var expirationTicks = DateTime.UtcNow.AddSeconds(cooldownSeconds).Ticks;
		PlayerViolationCooldowns[playerId] = expirationTicks;
	}

	public static bool IsPlayerOnCooldown(string playerId)
	{
		if (!PlayerViolationCooldowns.TryGetValue(playerId, out var expirationTicks))
			return false;

		if (DateTime.UtcNow.Ticks < expirationTicks)
			return true;

		PlayerViolationCooldowns.Remove(playerId);
		return false;
	}
}