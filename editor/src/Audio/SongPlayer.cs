using System;
using System.IO;
using System.Threading.Tasks;
using Beatform.Audio;
using Godot;

namespace Beatform.Editor.Audio;

/// <summary>
/// Plays the level's song and reports song time (ms, with the level's audio offset applied) in a way
/// that stays smooth between audio mixes. Also decodes the song once for the timeline waveform.
/// </summary>
public partial class SongPlayer : Node
{
    private AudioStreamPlayer player;
    private double offsetMs;
    private double startSongMs;
    private float speed = 1;

    public string LoadedPath { get; private set; }
    public double LengthMs { get; private set; }
    public bool HasSong => player.Stream != null;
    public bool Playing => player.Playing;
    public Waveform Waveform { get; private set; }
    public string Error { get; private set; }

    public override void _Ready()
    {
        player = new AudioStreamPlayer { Bus = "Master" };
        AddChild(player);
    }

    public float Speed
    {
        get => speed;
        set
        {
            speed = Mathf.Clamp(value, 0.25f, 2f);
            player.PitchScale = speed;
        }
    }

    public void Load(string path, double audioOffsetMs)
    {
        Stop();
        offsetMs = audioOffsetMs;
        Error = null;
        LoadedPath = path;
        player.Stream = null;
        Waveform = null;
        LengthMs = 0;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Error = path == null ? null : "song file not found: " + path;
            return;
        }
        try
        {
            AudioStream stream = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                ".mp3" => new AudioStreamMP3 { Data = File.ReadAllBytes(path) },
                ".wav" => AudioStreamWav.LoadFromFile(path),
                _ => throw new NotSupportedException("use .ogg, .mp3 or .wav"),
            };
            player.Stream = stream;
            LengthMs = stream.GetLength() * 1000.0;
        }
        catch (Exception e)
        {
            Error = "could not open the song: " + e.Message;
            return;
        }
        var file = path;
        Task.Run(() =>
        {
            try { return Waveform.FromPcm(AudioDecoder.Decode(file)); }
            catch { return null; }
        }).ContinueWith(t => { if (LoadedPath == file) Waveform = t.Result; });
    }

    public void SetOffset(double audioOffsetMs) => offsetMs = audioOffsetMs;

    /// <summary>Starts playback at a song time (ms).</summary>
    public void Play(double songMs)
    {
        if (!HasSong) return;
        double audioSec = Math.Max(0, (songMs + offsetMs) / 1000.0);
        player.Play((float)audioSec);
        startSongMs = songMs;
        lastSongMs = songMs;
    }

    public void Stop()
    {
        if (player != null && player.Playing) player.Stop();
    }

    private double lastSongMs;

    /// <summary>Current song time in ms while playing; never steps backwards by a few ms.</summary>
    public double SongMs
    {
        get
        {
            if (!player.Playing) return startSongMs;
            double audio = player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
            double t = audio * 1000.0 - offsetMs;
            if (t < lastSongMs && lastSongMs - t < 40) t = lastSongMs;
            lastSongMs = t;
            return t;
        }
    }
}

/// <summary>Min/max peaks of a song in 5 ms buckets, for drawing the timeline waveform.</summary>
public sealed class Waveform
{
    public const double BucketMs = 5;
    public float[] Min, Max;
    public double LengthMs;

    public static Waveform FromPcm(PcmAudio pcm)
    {
        int bucketFrames = Math.Max(1, (int)(pcm.SampleRate * BucketMs / 1000));
        int buckets = pcm.Frames / bucketFrames + 1;
        var w = new Waveform { Min = new float[buckets], Max = new float[buckets], LengthMs = pcm.LengthMs };
        var s = pcm.Samples;
        int ch = pcm.Channels;
        for (int b = 0; b < buckets; b++)
        {
            float lo = 0, hi = 0;
            int f0 = b * bucketFrames, f1 = Math.Min(pcm.Frames, f0 + bucketFrames);
            for (int f = f0; f < f1; f++)
            {
                float v = 0;
                for (int c = 0; c < ch; c++) v += s[f * ch + c];
                v /= ch;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            w.Min[b] = lo;
            w.Max[b] = hi;
        }
        return w;
    }

    /// <summary>Peak range over an audio time span (ms from the start of the file).</summary>
    public (float lo, float hi) Range(double fromMs, double toMs)
    {
        int b0 = Math.Max(0, (int)(fromMs / BucketMs)), b1 = Math.Min(Min.Length - 1, (int)(toMs / BucketMs));
        float lo = 0, hi = 0;
        for (int b = b0; b <= b1; b++)
        {
            if (Min[b] < lo) lo = Min[b];
            if (Max[b] > hi) hi = Max[b];
        }
        return (lo, hi);
    }
}
