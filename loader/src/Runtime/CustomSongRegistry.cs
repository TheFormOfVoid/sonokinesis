using System.Collections.Generic;
using UnityEngine;

namespace Sonokinesis.Loader.Runtime;

/// <summary>
/// Builds and tracks the game-side song slots (ModelStoryCheckpoint) that stand in for custom
/// levels. A slot carries our runtime-built MetaSong (checkpoints only; hazards are drawn by
/// Sonokinesis itself) and the level's AudioClip, so the game's own level flow can run it.
/// </summary>
internal static class CustomSongRegistry
{
    public const string IdPrefix = "sonokinesis:";
    private const ushort FirstNumericId = 60000;
    private const float EndCheckpointMarginMs = 150;

    private static readonly Dictionary<string, ModelStoryCheckpoint> byId = new();
    private static ushort nextNumericId = FirstNumericId;

    public static bool IsCustom(ModelStoryCheckpoint m) => m != null && m.id != null && m.id.StartsWith(IdPrefix);

    /// <summary>Creates the song slot for a loaded level. Checkpoints are converted to audio time.</summary>
    public static ModelStoryCheckpoint Create(LoadedLevel level)
    {
        var meta = level.Data.Meta;
        var cps = new List<float>();
        foreach (var c in level.Data.Checkpoints) cps.Add((float)level.AudioTime(c));
        // The game finishes a level when its last checkpoint is completed, so every level gets a
        // final checkpoint at its end (the end of the trimmed audio).
        float endMs = level.Clip.length * 1000f - EndCheckpointMarginMs;
        if (cps.Count == 0 || endMs > cps[cps.Count - 1] + 500) cps.Add(endMs);
        return Create(meta.Id, string.IsNullOrEmpty(meta.Title) ? "Untitled" : meta.Title, meta.Artist ?? "", level.Clip, cps);
    }

    public static ModelStoryCheckpoint Create(string key, string title, string artist, AudioClip clip, IReadOnlyList<float> checkpointTimesMs)
    {
        var id = IdPrefix + key;
        // Borrow a plain arcade map so the level scene has the references it expects.
        var templateMap = ModelStoryCheckpointEnum.ALL[100].map;
        var soundTrack = new ModelSoundTrack(title, 1, artist, "", "", "", null, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Assets.Plugins.Berzerk.ModelFla>(0));
        var model = new ModelStoryCheckpoint(nextNumericId++, id, templateMap, soundTrack);
        model.isStory = false;

        var metaSong = BuildMetaSong(model, title, artist, checkpointTimesMs);
        model.SetMetaSong(metaSong);
        model.SetAudioClip(clip);
        byId[id] = model;
        return model;
    }

    private static MetaSong BuildMetaSong(ModelStoryCheckpoint model, string title, string artist, IReadOnlyList<float> checkpointTimesMs)
    {
        var metaSong = ScriptableObject.CreateInstance<MetaSong>();
        metaSong.name = model.id;
        metaSong.songName = title;
        metaSong.artistName = artist;
        metaSong.linkToArtistWeb = "";
        metaSong.urlSong = "";
        metaSong.modelSong = model;
        metaSong.metaLayerGroup = new MetaSongLayerGroup(metaSong);
        metaSong.metaLayerGroup.addNewLayer();
        metaSong.allSpawnsInOrder = new Il2CppSystem.Collections.Generic.List<MetaSpawn>();
        metaSong.allCheckPoints = new Il2CppSystem.Collections.Generic.List<MetaSpawn>();

        var layer = metaSong.metaLayerGroup.getAt(0);
        foreach (var t in checkpointTimesMs)
        {
            var spawn = MetaSpawn.createInTime(ModelSpawnEnum.CHECKPOINT, t);
            layer.addSpawn(spawn);
        }
        metaSong.RefreshReferences();
        metaSong.refreshAllSpawnInOrder();
        metaSong.sortByTimeAndSetIndex();
        metaSong.feedCheckPointList();
        return metaSong;
    }

    /// <summary>A procedural click track, used to test the pipeline before file loading exists.</summary>
    public static AudioClip MakeClickTrack(float seconds, float bpm)
    {
        const int rate = 44100;
        int n = (int)(seconds * rate);
        var data = new float[n];
        int beat = (int)(rate * 60f / bpm);
        for (int i = 0; i < n; i++)
        {
            int k = i % beat;
            if (k < 2200)
            {
                float env = 1f - k / 2200f;
                bool bar = (i / beat) % 4 == 0;
                data[i] = Mathf.Sin(2 * Mathf.PI * (bar ? 1320f : 880f) * i / rate) * env * 0.4f;
            }
        }
        var clip = AudioClip.Create("sonokinesis-click", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
