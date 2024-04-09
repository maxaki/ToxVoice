using System.Collections.Concurrent;

namespace ToxVoice.Persistence;

public static class ToxVoicePersistence
{
	private static readonly Facepunch.Sqlite.Database Identity = new();
	private static readonly ConcurrentDictionary<string, string> ToxVoiceUserIdCache = new();
	private static readonly object Lock = new();

	static ToxVoicePersistence()
	{
	}

	public static void Init()
	{
		if (!Directory.Exists("HarmonyData"))
			Directory.CreateDirectory("HarmonyData");

		lock (Lock)
		{
			Identity.Open("HarmonyData\\ToxVoice.db");
			Identity.Execute("CREATE TABLE IF NOT EXISTS users (steamid TEXT PRIMARY KEY, toxvoiceuserid TEXT)");
			Identity.Execute("CREATE TABLE IF NOT EXISTS violations (steamid TEXT PRIMARY KEY, violationcount INTEGER)");
		}
	}

	public static void Close()
	{
		try
		{
			lock (Lock)
			{
				Identity.Close();
			}
		}
		catch
		{
		}

		GC.Collect();
		GC.WaitForPendingFinalizers();
	}

	public static void ResetAllViolations()
	{
		lock (Lock)
		{
			Identity.Execute("DELETE FROM violations");
		}
	}

	public static void ResetPlayerViolation(string steamId)
	{
		lock (Lock)
		{
			Identity.Execute("DELETE FROM violations WHERE steamid = ?", steamId);
		}
	}

	public static int IncrementViolationCount(string steamId)
	{
		lock (Lock)
		{
			var existingCount = Identity.Query<int, string>("SELECT violationcount FROM violations WHERE steamid = ?", steamId);
			if (existingCount > 0)
			{
				Identity.Execute("UPDATE violations SET violationcount = violationcount + 1 WHERE steamid = ?", steamId);
				return existingCount + 1;
			}

			Identity.Execute("INSERT INTO violations (steamid, violationcount) VALUES (?, 1)", steamId);
			return 1;
		}
	}

	public static string GetOrGenerateToxVoiceId(string playerID)
	{
		if (Identity == null)
		{
			throw new InvalidOperationException("Identity database is not initialized.");
		}

		var toxVoiceId = ToxVoiceUserIdCache.FirstOrDefault(x => x.Value == playerID).Key;
		if (!string.IsNullOrEmpty(toxVoiceId))
		{
			return toxVoiceId;
		}

		lock (Lock)
		{
			var toxVoiceUserId = Identity.Query<string, string>("SELECT toxvoiceuserid FROM users WHERE steamid = ?", playerID);
			if (!string.IsNullOrEmpty(toxVoiceUserId))
			{
				ToxVoiceUserIdCache[toxVoiceUserId] = playerID;
				return toxVoiceUserId;
			}

			toxVoiceId = GenerateId();
			Identity.Execute("INSERT INTO users (steamid, toxvoiceuserid) VALUES (?, ?)", playerID, toxVoiceId);
			ToxVoiceUserIdCache[toxVoiceId] = playerID;
		}

		return toxVoiceId;
	}

	public static string GetSteamIdFromToxVoiceIdCache(string toxVoiceUserId) => ToxVoiceUserIdCache.GetValueOrDefault(toxVoiceUserId, string.Empty);

	public static string GetSteamIdFromToxVoiceId(string toxVoiceUserId)
	{
		if (ToxVoiceUserIdCache.TryGetValue(toxVoiceUserId, out var steamId))
		{
			return steamId;
		}

		lock (Lock)
		{
			steamId = Identity.Query<string, string>("SELECT steamid FROM users WHERE toxvoiceuserid = ?", toxVoiceUserId);
			if (!string.IsNullOrEmpty(steamId))
			{
				ToxVoiceUserIdCache[toxVoiceUserId] = steamId;
			}
		}

		return steamId;
	}

	public static string GetToxVoiceIdFromSteamId(string userId)
	{
		var toxVoiceUserId = ToxVoiceUserIdCache.FirstOrDefault(x => x.Value == userId).Key;
		if (!string.IsNullOrEmpty(toxVoiceUserId))
		{
			return toxVoiceUserId;
		}

		lock (Lock)
		{
			toxVoiceUserId = Identity.Query<string, string>("SELECT toxvoiceuserid FROM users WHERE steamid = ?", userId);
			if (!string.IsNullOrEmpty(toxVoiceUserId))
			{
				ToxVoiceUserIdCache[toxVoiceUserId] = userId;
			}
		}

		return toxVoiceUserId;
	}

	private static string GenerateId() => Guid.NewGuid().ToString("N");
}