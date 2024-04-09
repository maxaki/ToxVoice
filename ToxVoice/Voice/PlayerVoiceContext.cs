using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using ToxVoice.Persistence;

namespace ToxVoice.Voice;

public class PlayerVoiceContext : IDisposable
{
	private bool _disposed;
	private long _lastProcessedTimeTicks;
	private const long TimeoutThresholdTicks = TimeSpan.TicksPerSecond*5;
	private int _recording;
	private readonly byte[] _toxVoiceUserIdBytes;
	private readonly ConcurrentQueue<byte[]> _voiceQueue = new();

	public PlayerVoiceContext(ulong userId)
	{
		var toxVoiceUserId = ToxVoicePersistence.GetOrGenerateToxVoiceId(userId.ToString());
		_toxVoiceUserIdBytes = Encoding.UTF8.GetBytes(toxVoiceUserId);
	}

	public void Enqueue(byte[] data)
	{
		Interlocked.Exchange(ref _recording, 1);
		Interlocked.Exchange(ref _lastProcessedTimeTicks, DateTime.UtcNow.Ticks);
		_voiceQueue.Enqueue(data);
	}

	public void Reset()
	{
		Interlocked.Exchange(ref _recording, 0);
		Interlocked.Exchange(ref _lastProcessedTimeTicks, DateTime.UtcNow.Ticks);
	}

	public bool IsReady()
	{
		if (Interlocked.CompareExchange(ref _recording, 0, 0) == 0)
			return false;

		var count = _voiceQueue.Count;
		if (count > 600)
			return true;

		var lastProcessedTicks = Interlocked.Read(ref _lastProcessedTimeTicks);
		return count > 12 && DateTime.UtcNow.Ticks - lastProcessedTicks >= TimeoutThresholdTicks;
	}

	public bool IsIdle(TimeSpan idleThreshold)
	{
		if (Interlocked.CompareExchange(ref _recording, 0, 0) == 1)
			return false;

		var lastProcessedTicks = Interlocked.Read(ref _lastProcessedTimeTicks);
		return DateTime.UtcNow.Ticks - lastProcessedTicks >= idleThreshold.Ticks;
	}
	public int WriteToStream(MemoryStream stream)
	{
		var length = 12;
		stream.Write(_toxVoiceUserIdBytes, 0, 32);

		stream.Position += 4;

		Span<byte> sizeSpan = stackalloc byte[4];

		var packetCount = 0;

		while (_voiceQueue.TryDequeue(out var voicePacket))
		{
			if (voicePacket.Length <= 12)
			{
				continue;
			}

			var newPacketLength = voicePacket.Length - 12;
			BinaryPrimitives.WriteInt32LittleEndian(sizeSpan, newPacketLength);
			length += 4 + newPacketLength;
			foreach (var byteValue in sizeSpan)
			{
				stream.WriteByte(byteValue);
			}

			stream.Write(voicePacket, 8, newPacketLength);
			packetCount++;
		}

		Span<byte> countSpan = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(countSpan, packetCount);
		stream.Position = 32;
		foreach (var byteValue in countSpan)
		{
			stream.WriteByte(byteValue);
		}

		return length;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_voiceQueue.Clear();
	}
}