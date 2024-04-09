using UnityEngine;

namespace ToxVoice.Extensions;

public static class ConsoleExtensions
{

	public static void ConsoleCommand(this Transcription.Transcription transcription, string? command, string steamId, bool printConsole)
	{
		var text = transcription.Text;

		command = command?.Replace("{steamid}", steamId);
		Facepunch.Threading.QueueOnMainThread(() =>
		{
			if (command is not null)
				ConsoleSystem.Run(ConsoleSystem.Option.Server, command);

			if (printConsole)
				DebugEx.Log($"[VOICE] [{steamId}] : {text}");
		});
	}
}