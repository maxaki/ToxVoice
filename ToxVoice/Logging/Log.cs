using System.Threading.Channels;
using UnityEngine;

namespace ToxVoice.Logging;

internal static class Log
{
	private const string Prefix = "[ToxVoice]";

	internal static void Info(string message)
	{
		Facepunch.Threading.QueueOnMainThread(() =>
		{
			Debug.Log($"{Prefix} {message}");
		});
	}

	internal static void Error(string message)
	{
		Facepunch.Threading.QueueOnMainThread(() =>
		{
			Debug.LogError($"{Prefix} {message}");
		});
	}

	internal static void Warning(string message)
	{
		Facepunch.Threading.QueueOnMainThread(() =>
		{
			Debug.LogWarning($"{Prefix} {message}");
		});
	}
}