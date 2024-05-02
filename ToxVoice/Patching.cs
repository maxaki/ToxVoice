using HarmonyLib;
using ToxVoice.Networking;
using ToxVoice.Persistence;
using ToxVoice.RustConsole;
using ToxVoice.ToxVoiceConfiguration;
using UnityEngine;

namespace ToxVoice;

public class Patching : IHarmonyModHooks
{
	private bool _loaded;
	private static VoiceNetworking? VoiceNetworking { get; set; }
	private static PlayerVoiceSink? PlayerVoiceSink { get; set; }

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		ToxVoicePersistence.Init();
		var configuration = ConfigurationFile.LoadConfiguration();
		if (!configuration.ToxVoice.IsValid())
		{
			DebugEx.LogWarning("[ToxVoice] Invalid token in configuration file. Please update config and call harmony.load toxvoice to reload the mod.");
			return;
		}

		_loaded = true;
		VoiceNetworking = new VoiceNetworking(configuration);
		Task.Run(VoiceNetworking.StartAsync).ConfigureAwait(false);
		PlayerVoiceSink = new PlayerVoiceSink(VoiceNetworking);
		Task.Run(PlayerVoiceSink.StartAsync).ConfigureAwait(false);
		if (ConsoleSystem.Index.All is not null)
		{
			ToxVoiceConsole.RegisterCommands();
		}
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		ToxVoicePersistence.Close();

		if (!_loaded)
			return;

		PlayerVoiceSink?.Dispose();
		_ = VoiceNetworking?.StopAsync().ConfigureAwait(false);

		ToxVoiceConsole.UnregisterCommands();
	}

	[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.OnReceivedVoice))]
	public static class OnReceivedVoicePatch
	{
		public static void Postfix(byte[] data, BasePlayer __instance)
		{
			PlayerVoiceSink?.TryWrite(data);
		}
	}
}