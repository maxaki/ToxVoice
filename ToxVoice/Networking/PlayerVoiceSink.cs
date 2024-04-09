using System.Collections.Concurrent;
using System.Threading.Channels;
using ToxVoice.Voice;

namespace ToxVoice.Networking;

public class PlayerVoiceSink : IDisposable
{
	private bool _disposed;

	private readonly VoiceNetworking _voiceNetworking;
	private readonly CancellationTokenSource _shutdownCts;
	private readonly CancellationToken _shutdownToken;
	private readonly List<ulong> _idleEntries = new();
	private readonly ConcurrentDictionary<ulong, PlayerVoiceContext> _playerVoiceContexts = new();

	private readonly Channel<byte[]> _voiceSink = Channel.CreateUnbounded<byte[]>();
	private readonly TimeSpan _idleThreshold = TimeSpan.FromMinutes(1);

	public PlayerVoiceSink(VoiceNetworking voiceNetworking)
	{
		_voiceNetworking = voiceNetworking;
		_shutdownCts = new CancellationTokenSource();
		_shutdownToken = _shutdownCts.Token;
	}

	public void TryWrite(byte[] data)
	{
		_voiceSink.Writer.TryWrite(data);
	}

	public async Task StartAsync()
	{
		await using var timer = new Timer(Callback, null, 1000, 1000);

		try
		{
			while (await _voiceSink.Reader.WaitToReadAsync(_shutdownToken).ConfigureAwait(false))
			{
				while (_voiceSink.Reader.TryRead(out var bytes))
				{
					GetOrAddPlayerVoiceRecording(BitConverter.ToUInt64(bytes, 0)).Enqueue(bytes);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception e)
		{
			Console.WriteLine($"[ToxVoice] Unexpected exception in voice sink: {e.Message}");
		}
	}

	private void Callback(object state)
	{
		if (_disposed)
			return;

		_idleEntries.Clear();
		foreach (var playerVoiceContext in _playerVoiceContexts)
		{
			if (playerVoiceContext.Value.IsIdle(_idleThreshold))
			{
				_idleEntries.Add(playerVoiceContext.Key);
				continue;
			}

			if (!playerVoiceContext.Value.IsReady())
			{
				continue;
			}

			_voiceNetworking.TryWrite(playerVoiceContext.Value);
		}

		foreach (var idleEntry in _idleEntries)
		{
			if (!_playerVoiceContexts.TryRemove(idleEntry, out var removedContext))
				continue;

			if (!removedContext.IsIdle(_idleThreshold))
			{
				_playerVoiceContexts.TryAdd(idleEntry, removedContext);
				continue;
			}

			removedContext.Dispose();
		}
	}

	private PlayerVoiceContext GetOrAddPlayerVoiceRecording(ulong userId)
	{
		if (_playerVoiceContexts.TryGetValue(userId, out var playerVoiceRecording))
			return playerVoiceRecording;

		playerVoiceRecording = new PlayerVoiceContext(userId);
		_playerVoiceContexts[userId] = playerVoiceRecording;
		return playerVoiceRecording;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		foreach (var playerVoiceContext in _playerVoiceContexts)
		{
			playerVoiceContext.Value.Dispose();
		}

		_playerVoiceContexts.Clear();
		_voiceSink.Writer.TryComplete();

		if (!_shutdownCts.IsCancellationRequested)
			_shutdownCts.Cancel();
	}
}