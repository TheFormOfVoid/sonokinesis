using System;
using System.IO;

namespace Beatform.Loader.Runtime;

/// <summary>Decoded PCM audio: interleaved float samples in [-1, 1].</summary>
internal sealed class PcmAudio
{
    public float[] Samples;
    public int Channels;
    public int SampleRate;
    public int Frames => Samples.Length / Channels;
    public double LengthMs => Frames * 1000.0 / SampleRate;
}

/// <summary>
/// Decodes WAV, Ogg Vorbis and MP3 files in managed code. The game's build strips Unity's runtime
/// audio decoding, so levels bring their audio through here. Safe to call off the main thread.
/// </summary>
internal static class AudioDecoder
{
    public static PcmAudio Decode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        using var stream = File.OpenRead(path);
        return ext switch
        {
            ".wav" => DecodeWav(stream),
            ".ogg" => DecodeOgg(stream),
            ".mp3" => DecodeMp3(stream),
            _ => throw new NotSupportedException($"unsupported audio format '{ext}' (use .ogg, .wav or .mp3)"),
        };
    }

    private static PcmAudio DecodeOgg(Stream stream)
    {
        using var reader = new NVorbis.VorbisReader(stream, false);
        int channels = reader.Channels;
        long total = reader.TotalSamples * channels;
        var buffer = new float[total > 0 && total < int.MaxValue ? total : 1 << 20];
        int filled = 0;
        while (true)
        {
            if (filled == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            int read = reader.ReadSamples(buffer, filled, buffer.Length - filled);
            if (read <= 0) break;
            filled += read;
        }
        if (filled != buffer.Length) Array.Resize(ref buffer, filled - filled % channels);
        return new PcmAudio { Samples = buffer, Channels = channels, SampleRate = reader.SampleRate };
    }

    private static PcmAudio DecodeMp3(Stream stream)
    {
        var mpeg = new NLayer.MpegFile(stream);
        int channels = mpeg.Channels;
        var buffer = new float[1 << 20];
        int filled = 0;
        while (true)
        {
            if (buffer.Length - filled < 4608) Array.Resize(ref buffer, buffer.Length * 2);
            int read = mpeg.ReadSamples(buffer, filled, buffer.Length - filled);
            if (read <= 0) break;
            filled += read;
        }
        Array.Resize(ref buffer, filled - filled % channels);
        return new PcmAudio { Samples = buffer, Channels = channels, SampleRate = mpeg.SampleRate };
    }

    private static PcmAudio DecodeWav(Stream stream)
    {
        using var br = new BinaryReader(stream);
        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");
        int format = 0, channels = 0, rate = 0, bits = 0;
        byte[] data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            long next = stream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = br.ReadInt16();
                channels = br.ReadInt16();
                rate = br.ReadInt32();
                br.ReadInt32();
                br.ReadInt16();
                bits = br.ReadInt16();
                if (format == 0xFFFE && size >= 26)
                {
                    br.ReadInt16(); br.ReadInt16(); br.ReadInt32();
                    format = br.ReadInt16(); // sub-format GUID starts with the real format tag
                }
            }
            else if (id == "data")
            {
                data = br.ReadBytes((int)Math.Min(size, stream.Length - stream.Position));
            }
            stream.Position = Math.Min(next, stream.Length);
        }
        if (data == null || channels <= 0) throw new InvalidDataException("WAV has no fmt or data chunk");

        int bytesPer = bits / 8;
        int count = data.Length / bytesPer;
        var samples = new float[count - count % channels];
        for (int i = 0; i < samples.Length; i++)
        {
            int o = i * bytesPer;
            samples[i] = (format, bits) switch
            {
                (1, 8) => (data[o] - 128) / 128f,
                (1, 16) => BitConverter.ToInt16(data, o) / 32768f,
                (1, 24) => ((data[o] | data[o + 1] << 8 | (sbyte)data[o + 2] << 16)) / 8388608f,
                (1, 32) => BitConverter.ToInt32(data, o) / 2147483648f,
                (3, 32) => BitConverter.ToSingle(data, o),
                (3, 64) => (float)BitConverter.ToDouble(data, o),
                _ => throw new NotSupportedException($"WAV format {format} with {bits} bits is not supported"),
            };
        }
        return new PcmAudio { Samples = samples, Channels = channels, SampleRate = rate };
    }
}
