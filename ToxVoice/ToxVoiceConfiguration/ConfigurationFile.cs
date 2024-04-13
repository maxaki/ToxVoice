using Newtonsoft.Json;
using ToxVoice.TranscriptionProcessing;

namespace ToxVoice.ToxVoiceConfiguration;

public class ToxVoiceConfiguration
{
	public string Token { get; set; } = "YOUR_TOXVOICE_TRANSCRIPTION_TOKEN";

	public bool IsValid() => Guid.TryParse(Token, out _);
}

public class TranscriptionLogs
{
	public DiscordLogs DiscordLog { get; set; } = new();
	public ConsoleLogs ConsoleLog { get; set; } = new();

	public class DiscordLogs
	{
		public bool Enabled { get; set; } = true;
		public bool HideSteamId { get; set; } = false;
		public string WebhookUrl { get; set; } = "YOUR_DISCORD_WEBHOOK_URL";
	}

	public class ConsoleLogs
	{
		public bool Enabled { get; set; } = true;
	}
}

public class WeightConfiguration
{
	[JsonProperty(PropertyName = "DiscordWeightThreshold", ObjectCreationHandling = ObjectCreationHandling.Replace)]
	public DiscordWeightThreshold DiscordWeightThreshold { get; set; } = new();

	[JsonProperty(PropertyName = "ViolationWeightThreshold", ObjectCreationHandling = ObjectCreationHandling.Replace)]
	public ViolationWeightThreshold ViolationWeightThreshold { get; set; } = new();
}

public class DiscordWeightThreshold
{
	public bool Enabled { get; set; } = true;
	public int TriggerAlertWeightThreshold { get; set; } = 20;
	public string AlertWebhookUrl { get; set; } = "WEBHOOK_URL";
}

public class TriggerFilter
{
	public int Weight { get; set; }
	public bool Regex { get; set; }
	public List<string> Triggers { get; set; }
}

public class TriggerFilterConfiguration
{
	public bool Enabled { get; set; } = true;

	[JsonProperty(PropertyName = "TriggerFilters", ObjectCreationHandling = ObjectCreationHandling.Replace)]
	public List<TriggerFilter> Filters { get; set; } = ConfigurationFile.GetDefaultFilters();
}

public class ViolationWeightThreshold
{
	public bool Enabled { get; set; } = true;
	public int TriggerActionWeightThreshold { get; set; } = 10;
	[JsonProperty(PropertyName = "ViolationActions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
	public Dictionary<int, ViolationAction> ViolationActions { get; set; } = new()
	{
		{1, new ViolationAction("warn {steamid} \"First warning for violating the rules\"", 30)},
		{2, new ViolationAction("warn {steamid} \"Second warning for violating the rules\"", 30)},
		{3, new ViolationAction("mute {steamid} 30s \"Muted for repeated rule violations\"", 30)},
		{4, new ViolationAction("mute {steamid} 1m \"Muted for continued rule violations\"", 60)},
		{5, new ViolationAction("mute {steamid} 5m \"Muted for persistent rule violations\"", 300)},
		{6, new ViolationAction("mute {steamid} 1h \"Muted for ongoing rule violations\"", 3600)},
		{7, new ViolationAction("mute {steamid} 3h \"Muted for frequent rule violations\"", 10800)},
		{8, new ViolationAction("mute {steamid} 12h \"Muted for excessive rule violations\"", 43200)},
		{9, new ViolationAction("mute {steamid} 1d \"Muted for numerous rule violations\"", 86400)},
		{10, new ViolationAction("ban {steamid} 1d \"Banned for repeated and severe rule violations\"", 86400)}
	};



	public ViolationAction GetViolationAction(int violationCount)
	{
		if (ViolationActions.TryGetValue(violationCount, out var violationAction))
		{
			return violationAction;
		}

		return ViolationActions[ViolationActions.Keys.Max()];
	}
}
public class ViolationAction
{
	[JsonProperty("Action")]
	public string Action { get; set; }

	[JsonProperty("CooldownSeconds")]
	public int CooldownSeconds { get; set; }

	public ViolationAction(string action, int cooldownSeconds)
	{
		Action = action;
		CooldownSeconds = cooldownSeconds;
	}
}

public class ConfigurationFile
{
	public const string ConfigDirectory = "HarmonyConfig";
	public const string ToxVoiceConfigFileName = $"{ConfigDirectory}/ToxVoice.json";

	public ToxVoiceConfiguration ToxVoice { get; set; } = new();
	public TranscriptionLogs TranscriptionLogs { get; set; } = new();
	public WeightConfiguration WeightConfiguration { get; set; } = new();
	public TriggerFilterConfiguration TriggerFilter { get; set; } = new();

	public static List<TriggerFilter> GetDefaultFilters() => new()
	{
		new TriggerFilter {Regex = false, Triggers = new List<string> {"word"}, Weight = 8},
		new TriggerFilter {Regex = false, Triggers = new List<string> {"word1", "word2"}, Weight = 3},
		new TriggerFilter {Regex = false, Triggers = new List<string> {"testing"}, Weight = 50},
		new TriggerFilter {Regex = true, Triggers = new List<string> {@"\bword1\b.*\bword2\b.*\bword3\b"}, Weight = 8}
	};

	public bool TryCreateDiscordLogsHttpClient(out DiscordHttpClient? discordHttpClient)
	{
		if (TranscriptionLogs.DiscordLog.Enabled)
		{
			if (Uri.TryCreate(TranscriptionLogs.DiscordLog.WebhookUrl, UriKind.Absolute, out var uri))
			{
				discordHttpClient = new DiscordHttpClient(uri, TranscriptionLogs.DiscordLog.HideSteamId);
				return true;
			}
		}

		discordHttpClient = default;
		return false;
	}
	public bool TryCreateTranscriptionFilter(out TranscriptionFilter? transcriptionFilter)
	{
		if (TriggerFilter.Enabled)
		{
			transcriptionFilter = new TranscriptionFilter(this);
			return true;
		}

		transcriptionFilter = default;
		return false;
	}
	public bool TryCreateDiscordAlertHttpClient(out DiscordHttpClient? discordHttpClient)
	{
		if (WeightConfiguration.DiscordWeightThreshold.Enabled)
		{
			if (Uri.TryCreate(WeightConfiguration.DiscordWeightThreshold.AlertWebhookUrl, UriKind.Absolute, out var uri))
			{
				discordHttpClient = new DiscordHttpClient(uri, TranscriptionLogs.DiscordLog.HideSteamId);
				return true;
			}
		}

		discordHttpClient = default;
		return false;
	}

	public static void SaveConfiguration(ConfigurationFile config)
	{
		var json = JsonConvert.SerializeObject(config, Formatting.Indented);
		if (!Directory.Exists(ConfigDirectory))
		{
			Directory.CreateDirectory(ConfigDirectory);
		}

		File.WriteAllText(ToxVoiceConfigFileName, json);
	}

	public static ConfigurationFile LoadConfiguration()
	{
		if (!File.Exists(ToxVoiceConfigFileName))
		{
			var config = new ConfigurationFile();
			SaveConfiguration(config);
			return config;
		}

		var json = File.ReadAllText(ToxVoiceConfigFileName);
		var existingConfig = JsonConvert.DeserializeObject<ConfigurationFile>(json) ?? new ConfigurationFile();

		return existingConfig;
	}
}