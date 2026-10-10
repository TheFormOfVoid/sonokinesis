using System;
using System.IO;
using System.Threading.Tasks;
using Beatform.Core;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>
/// Loads a level in the background (JSON and audio decoding) and finishes on the main thread,
/// where the AudioClip has to be created.
/// </summary>
internal sealed class LevelLoader
{
    private readonly Task<(LevelData data, PcmAudio pcm)> task;
    public readonly string Folder;

    public LevelLoader(string folder)
    {
        Folder = folder;
        task = Task.Run(() =>
        {
            var data = LevelData.Parse(File.ReadAllText(Path.Combine(folder, "level.json")));
            var pcm = AudioDecoder.Decode(Path.Combine(folder, data.Meta.Audio));
            Trim(pcm, data);
            return (data, pcm);
        });
    }

    public bool IsDone => task.IsCompleted;
    public string Error => task.IsFaulted ? (task.Exception?.InnerException ?? task.Exception)?.Message : null;

    /// <summary>Call on the main thread once IsDone and no Error.</summary>
    public LoadedLevel Finish()
    {
        var (data, pcm) = task.Result;
        var clip = AudioClip.Create("beatform:" + data.Meta.Id, pcm.Frames, pcm.Channels, pcm.SampleRate, false);
        clip.SetData(pcm.Samples, 0);
        return new LoadedLevel { Folder = Folder, Data = data, Evaluator = new LevelEvaluator(data), Clip = clip };
    }

    /// <summary>Cuts the audio at the level end (offset + length) so the game ends the level there.</summary>
    private static void Trim(PcmAudio pcm, LevelData data)
    {
        if (data.Meta.LengthMs is not double length) return;
        long frames = (long)Math.Ceiling((data.Meta.OffsetMs + length) * pcm.SampleRate / 1000.0);
        if (frames <= 0 || frames >= pcm.Frames) return;
        Array.Resize(ref pcm.Samples, (int)(frames * pcm.Channels));
    }
}
