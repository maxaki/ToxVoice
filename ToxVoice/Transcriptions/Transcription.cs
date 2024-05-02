using System.Buffers;
using System.Text;
using ToxVoice.TranscriptionProcessing;

namespace ToxVoice.Transcriptions;

public class Transcription
{
    public string ToxVoiceUserId { get; set; }
    public float AudioLength { get; set; }
    public string Text { get; set; }

    public int DataLength { get; set; }

    public byte[] Data { get; set; }
}
public static class TranscriptionExtensions
{
    private const int ToxVoiceUserIdLength = 32;
    public static void WriteToSinks(this MemoryStream stream, TranscriptionLogSink logSink)
    {
        stream.Position = 0;

        Span<byte> buffer = stream.GetBuffer();
        var position = 0;
        while (position < stream.Length)
        {
            var transcription = new Transcription
            {
                ToxVoiceUserId = buffer.ReadString(ref position, ToxVoiceUserIdLength),
                AudioLength = buffer.ReadFloat(ref position),
                Text = buffer.ReadString(ref position),
                DataLength = buffer.ReadBytes(ref position, out var data, usePool: true),
                Data = data
            };

            logSink.TryWrite(transcription);
        }
    }

    private static string ReadString(this Span<byte> buffer, ref int position, int? length = null)
    {
        int stringLength;
        if (length.HasValue)
        {
            stringLength = length.Value;
        }
        else
        {
            if (position + sizeof(int) > buffer.Length)
                throw new InvalidDataException("Insufficient data for string length.");

            stringLength = BitConverter.ToInt32(buffer.Slice(position, sizeof(int)));
            position += sizeof(int);
        }

        if (position + stringLength > buffer.Length)
            throw new InvalidDataException("Insufficient data for string.");

        var value = Encoding.UTF8.GetString(buffer.Slice(position, stringLength));
        position += stringLength;
        return value;
    }

    private static int ReadBytes(this Span<byte> buffer, ref int position, out byte[] value, bool usePool = false)
    {
        if (position + sizeof(int) > buffer.Length)
            throw new InvalidDataException("Insufficient data for byte array length.");

        var length = BitConverter.ToInt32(buffer.Slice(position, sizeof(int)));
        position += sizeof(int);

        if (position + length > buffer.Length)
            throw new InvalidDataException("Insufficient data for byte array.");

        if (usePool)
        {
            value = ArrayPool<byte>.Shared.Rent(length);
            buffer.Slice(position, length).CopyTo(value);
        }
        else
        {
            value = buffer.Slice(position, length).ToArray();
        }

        position += length;
        return length;
    }
    private static int ReadInt(this Span<byte> buffer, ref int position)
    {
        if (position + sizeof(int) > buffer.Length)
            throw new InvalidDataException("Insufficient data for int.");

        var value = BitConverter.ToInt32(buffer.Slice(position, sizeof(int)));
        position += sizeof(int);
        return value;
    }

    private static float ReadFloat(this Span<byte> buffer, ref int position)
    {
        if (position + sizeof(float) > buffer.Length)
            throw new InvalidDataException("Insufficient data for float.");

        var value = BitConverter.ToSingle(buffer.Slice(position, sizeof(float)));
        position += sizeof(float);
        return value;
    }
}