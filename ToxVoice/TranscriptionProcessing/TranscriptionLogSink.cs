using System.Buffers;
using System.Threading.Channels;
using ToxVoice.Extensions;
using ToxVoice.Persistence;
using ToxVoice.ToxVoiceConfiguration;

namespace ToxVoice.TranscriptionProcessing;


public class TranscriptionLogSink : IDisposable
{
	private bool _disposed;
	private readonly DiscordHttpClient? _discordAlertLogsHttpClient;
	private readonly DiscordHttpClient? _defaultDiscordClient;
	private readonly TranscriptionFilter? _transcriptionFilter;
	private readonly ViolationWeightThreshold _violationWeightThreshold;
	private readonly int _discordAlertThreshold;
	private readonly bool _consoleLogsEnabled;
	private readonly Channel<Transcription.Transcription> _transcriptionSink = Channel.CreateUnbounded<Transcription.Transcription>();

	public TranscriptionLogSink(ConfigurationFile configuration, CancellationToken abortToken)
	{
		if (configuration.TryCreateDiscordLogsHttpClient(out _defaultDiscordClient))
		{
		}

		if (configuration.TryCreateDiscordAlertHttpClient(out _discordAlertLogsHttpClient))
		{
			_discordAlertThreshold = configuration.WeightConfiguration.DiscordWeightThreshold.TriggerAlertWeightThreshold;
		}

		if (!configuration.TryCreateTranscriptionFilter(out _transcriptionFilter))
		{
		}

		_consoleLogsEnabled = configuration.TranscriptionLogs.ConsoleLog.Enabled;
		_violationWeightThreshold = configuration.WeightConfiguration.ViolationWeightThreshold;

		_ = TranscriptionSinkTask(abortToken);
	}

	public void TryWrite(Transcription.Transcription transcription) => _transcriptionSink.Writer.TryWrite(transcription);

	private async Task TranscriptionSinkTask(CancellationToken cancellationToken)
	{
		while (await _transcriptionSink.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
		{
			while (_transcriptionSink.Reader.TryRead(out var transcription))
			{
				try
				{
					if (cancellationToken.IsCancellationRequested)
						return;

					var steamId = ToxVoicePersistence.GetSteamIdFromToxVoiceIdCache(transcription.ToxVoiceUserId);

					var messageSent = false;
					var violatedFilterWeight = 0;
					if (_transcriptionFilter is not null)
					{
						violatedFilterWeight = _transcriptionFilter.GetViolatedFilterWeight(transcription.Text);
					}

					string? violationAction = null;
					if (_violationWeightThreshold.Enabled)
					{
						if (_violationWeightThreshold.TriggerActionWeightThreshold <= violatedFilterWeight)
						{
							var violations = ToxVoicePersistence.IncrementViolationCount(steamId);
							violationAction = _violationWeightThreshold.GetViolationAction(violations);
						}
					}

					transcription.ConsoleCommand(violationAction, steamId, _consoleLogsEnabled);
					if (_discordAlertThreshold <= violatedFilterWeight && _discordAlertLogsHttpClient is not null)
					{
						var response = await _discordAlertLogsHttpClient.SendMessageWithRetryAsync(steamId, transcription, violatedFilterWeight, cancellationToken).ConfigureAwait(false);
						if (response.IsSuccessStatusCode)
						{
							messageSent = true;
						}
						else
						{
							Console.WriteLine($"[ToxVoice] Failed to upload discord file. Status code: {response.StatusCode}");
						}
					}

					if (!messageSent && _defaultDiscordClient != null)
					{
						var response = await _defaultDiscordClient.SendMessageWithRetryAsync(steamId, transcription, violatedFilterWeight, cancellationToken).ConfigureAwait(false);
						if (!response.IsSuccessStatusCode)
						{
							Console.WriteLine($"[ToxVoice] Failed to upload discord file to default webhook. Status code: {response.StatusCode}");
						}
					}
				}
				catch (Exception exception)
				{
					Console.WriteLine($"[ToxVoice] Unexpected transcription log exception: {exception.Message}");
				}
				finally
				{
					ArrayPool<byte>.Shared.Return(transcription.Data);
				}
			}
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_transcriptionSink.Writer.TryComplete();
		_discordAlertLogsHttpClient?.Dispose();
	}
}