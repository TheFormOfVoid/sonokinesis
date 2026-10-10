using Beatform.Core;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>A parsed level with its decoded audio, ready to play.</summary>
internal sealed class LoadedLevel
{
    public string Folder;
    public LevelData Data;
    public LevelEvaluator Evaluator;
    public AudioClip Clip;

    /// <summary>Song time (ms) for a position in the audio file (ms).</summary>
    public double SongTime(double audioMs) => audioMs - Data.Meta.OffsetMs;

    /// <summary>Audio file position (ms) for a song time (ms).</summary>
    public double AudioTime(double songMs) => songMs + Data.Meta.OffsetMs;
}
