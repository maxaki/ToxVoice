using System.Net.WebSockets;
using System.Threading.Channels;
using ToxVoice.ToxVoiceConfiguration;
using ToxVoice.TranscriptionProcessing;
using ToxVoice.Transcriptions;
using ToxVoice.Voice;

namespace ToxVoice.Networking;

public class VoiceNetworking : IDisposable
{
	private bool _disposed;
	private bool _stopping;
	private readonly ConfigurationFile _config;
	private readonly Channel<PlayerVoiceContext> _playerVoiceSegmentSink = Channel.CreateUnbounded<PlayerVoiceContext>();

	private readonly CancellationTokenSource _shutdownCts;
	private readonly CancellationToken _shutDownToken;

	public VoiceNetworking(ConfigurationFile config)
	{
		_config = config;
		_shutdownCts = new CancellationTokenSource();
		_shutDownToken = _shutdownCts.Token;
		ClientWebSocket = new ClientWebSocket();
	}

	private ClientWebSocket ClientWebSocket { get; set; }

	public async Task StartAsync()
	{
		using var sink = new TranscriptionLogSink(_config, _shutdownCts.Token);
		while (!_shutdownCts.IsCancellationRequested)
		{
			if (_stopping)
				return;

			var disconnectCts = CancellationTokenSource.CreateLinkedTokenSource(_shutDownToken);
			ClientWebSocket = await ConnectWebSocket().ConfigureAwait(false)!;
			while (ClientWebSocket is null)
			{
				if (_shutDownToken.IsCancellationRequested)
					return;

				ClientWebSocket = await ConnectWebSocket().ConfigureAwait(false)!;
			}

			try
			{
				var sendTask = SendCog(disconnectCts.Token).ContinueWith(_ => disconnectCts.Cancel(), disconnectCts.Token);
				var receiveTask = ReceiveCog(sink, disconnectCts.Token).ContinueWith(_ => disconnectCts.Cancel(), disconnectCts.Token);
				Console.WriteLine("[ToxVoice] Connected");
				await Task.WhenAll(sendTask, receiveTask).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ToxVoice] Unhandled Exception occured: {ex.Message}");
			}
			finally
			{
				await TryDisconnectAsync().ConfigureAwait(false);
				Console.WriteLine("[ToxVoice] Disconnected");
			}
		}
	}

	public async Task StopAsync()
	{
		_stopping = true;
		await TryDisconnectAsync().ConfigureAwait(false);
		Dispose();
	}

	private async Task<ClientWebSocket?> ConnectWebSocket()
	{
		var token = _config.ToxVoice.Token;
		while (!_shutDownToken.IsCancellationRequested)
		{
			try
			{
				var clientWebSocket = new ClientWebSocket();
				clientWebSocket.Options.SetRequestHeader("Token", token);
				await clientWebSocket.ConnectAsync(new Uri("wss://voice.toxvoice.com:2096/voice-sink"), _shutDownToken).ConfigureAwait(false);
				return clientWebSocket;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ToxVoice] Failed to connect, retrying...");
				await Task.Delay(5000, _shutDownToken).ConfigureAwait(false);
			}
		}

		return default;
	}

	private async Task ReceiveCog(TranscriptionLogSink sink, CancellationToken cancellationToken)
	{
		var buffer = new byte[1024*4];
		using var ms = new MemoryStream();

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				WebSocketReceiveResult receiveResult;
				do
				{
					receiveResult = await ClientWebSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
					ms.Write(buffer, 0, receiveResult.Count);
				} while (!receiveResult.EndOfMessage);

				if (receiveResult.MessageType == WebSocketMessageType.Close)
				{
					await ClientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed by the server.", cancellationToken).ConfigureAwait(false);
					break;
				}

				ms.WriteToSinks(sink);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception)
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				Console.WriteLine($"[ToxVoice] WebSocket Receive Exception: {exception.Message}");
				return;
			}
			finally
			{
				ms.SetLength(0);
			}
		}
	}

	private async Task SendCog(CancellationToken cancellationToken)
	{
		using var ms = new MemoryStream();

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				while (await _playerVoiceSegmentSink.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
				{
					while (_playerVoiceSegmentSink.Reader.TryRead(out var playerVoiceContext))
					{
						try
						{
							playerVoiceContext.WriteToStream(ms);
							if (!ms.TryGetBuffer(out var buffer))
								continue;

							await ClientWebSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
						}
						catch (OperationCanceledException)
						{
							return;
						}
						catch (Exception exception)
						{
							if (cancellationToken.IsCancellationRequested)
								return;

							Console.WriteLine($"[ToxVoice] WebSocket Send Exception: {exception.Message}");
							return;
						}
						finally
						{
							playerVoiceContext.Reset();
							ms.SetLength(0);
						}
					}
				}
			}
			catch (ChannelClosedException)
			{
				break;
			}
		}
	}

	public async Task TryDisconnectAsync()
	{
		try
		{
			if (ClientWebSocket is not null)
			{
				if (ClientWebSocket.State == WebSocketState.Open)
				{
					await ClientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
				}

				ClientWebSocket.Dispose();
			}
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		if (!_shutdownCts.IsCancellationRequested)
			_shutdownCts.Cancel();

		try
		{
			_shutdownCts.Dispose();
		}
		catch
		{
		}

		_playerVoiceSegmentSink.Writer.TryComplete();
		try
		{
			if (!_shutdownCts.IsCancellationRequested)
				_shutdownCts.Cancel();

			ClientWebSocket.Dispose();
		}
		catch
		{
		}
	}

	public void TryWrite(PlayerVoiceContext value)
	{
		_playerVoiceSegmentSink.Writer.TryWrite(value);
	}
}