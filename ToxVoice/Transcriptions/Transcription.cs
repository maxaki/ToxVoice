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
    public static void WriteToSinks(this MemoryStream stream, TranscriptionLogSink logSink)
    {
        stream.Position = 0;

        Span<byte> buffer = stream.GetBuffer();
        var position = 0;
        while (position < stream.Length)
        {
            var transcription = new Transcription();

            const int toxVoiceUserIdLength = 32;

            if (position + toxVoiceUserIdLength > stream.Length)
                throw new InvalidDataException("Insufficient data for ToxVoiceUserId.");

            transcription.ToxVoiceUserId = Encoding.UTF8.GetString(buffer.Slice(position, toxVoiceUserIdLength));
            position += toxVoiceUserIdLength;

            if (position + sizeof(float) > stream.Length)
                throw new InvalidDataException("Insufficient data for AudioLength.");

            transcription.AudioLength = BitConverter.ToSingle(buffer.Slice(position, sizeof(float)));
            position += sizeof(float);

            if (position + sizeof(int) > stream.Length)
                throw new InvalidDataException("Insufficient data for text length.");

            var textLength = BitConverter.ToInt32(buffer.Slice(position, sizeof(int)));
            position += sizeof(int);

            if (position + textLength > stream.Length)
                throw new InvalidDataException("Insufficient data for text.");

            transcription.Text = Encoding.UTF8.GetString(buffer.Slice(position, textLength));
            position += textLength;

            if (position + sizeof(int) > stream.Length)
                throw new InvalidDataException("Insufficient data for data length.");

            var dataLength = BitConverter.ToInt32(buffer.Slice(position, sizeof(int)));
            position += sizeof(int);

            if (position + dataLength > stream.Length)
                throw new InvalidDataException("Insufficient data for data.");

            transcription.DataLength = dataLength;
            transcription.Data = ArrayPool<byte>.Shared.Rent(dataLength);
            buffer.Slice(position, dataLength).CopyTo(transcription.Data);

            position += dataLength;

            logSink.TryWrite(transcription);
        }
    }
}