using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Sonokinesis.Audio;

namespace Sonokinesis.Cli;

/// <summary>
/// Listens to a song so levels can be built around it: tempo, where the beats fall, how loud each bar
/// is, where the song changes, and its strongest hits. Spectral-flux onsets at 100 frames a second.
/// </summary>
public static class SongAnalysis
{
    private const double FrameMs = 10;
    private const int Window = 2048;

    public static JsonObject Analyze(string path, double? knownBpm = null)
    {
        var pcm = AudioDecoder.Decode(path);
        var mono = Mono(pcm);
        int sr = pcm.SampleRate;
        int hop = Math.Max(1, (int)(sr * FrameMs / 1000));
        var (onset, low, rms) = Onsets(mono, hop, sr);
        double lengthMs = pcm.LengthMs;
        var candidates = TempoCandidates(onset);
        double bpm = knownBpm ?? Refine(onset, candidates.Count > 0 ? candidates[0].Bpm : 120);
        double beatMs = 60000 / bpm;
        // The beat grid lines up with the most onsets. Between it and the grid half a beat later (where
        // off-beat hats can win), the low end decides when it clearly prefers one: kicks mark the beat.
        double lat = LatencyMs(sr);
        double phase = Phase(onset, beatMs);
        double half = (phase + beatMs / 2) % beatMs;
        if (Comb(low, half, beatMs) > 1.5 * Comb(low, phase, beatMs)) (phase, half) = (half, phase);
        double offset = (phase + lat) % beatMs;
        if (offset > beatMs - 8) offset = 0;
        double other = (half + lat) % beatMs;

        // Bars: loudness (0..1 against the loudest bar) and how busy they are (onsets per beat).
        var bars = new JsonArray();
        var barEnergy = new List<double>();
        double barMs = beatMs * 4;
        for (double t = offset; t < lengthMs; t += barMs)
        {
            barEnergy.Add(Mean(rms, t, t + barMs));
        }
        double maxE = Math.Max(1e-9, barEnergy.DefaultIfEmpty(0).Max());
        for (int i = 0; i < barEnergy.Count; i++)
        {
            double t = offset + i * barMs;
            bars.Add(new JsonObject
            {
                ["bar"] = i + 1,
                ["startMs"] = Math.Round(t, 1),
                ["songTime"] = Math.Round(i * barMs, 1),
                ["energy"] = Math.Round(barEnergy[i] / maxE, 3),
                ["busy"] = Math.Round(Mean(onset, t, t + barMs) / Math.Max(1e-9, onset.Average()), 2),
            });
        }

        var sections = Sections(barEnergy.Select(e => e / maxE).ToList(), offset, barMs, lengthMs);
        var hits = StrongHits(onset, 40, beatMs, lat);

        return new JsonObject
        {
            ["file"] = path,
            ["lengthMs"] = Math.Round(lengthMs, 1),
            ["sampleRate"] = sr,
            ["channels"] = pcm.Channels,
            ["bpm"] = Math.Round(bpm, 2),
            ["bpmCandidates"] = new JsonArray(candidates.Take(4).Select(c => (JsonNode)new JsonObject { ["bpm"] = Math.Round(c.Bpm, 2), ["score"] = Math.Round(c.Score, 3) }).ToArray()),
            ["beatMs"] = Math.Round(beatMs, 3),
            ["offsetMs"] = Math.Round(offset, 1),
            ["offsetIfOffBeat"] = Math.Round(other, 1),
            ["note"] = "Beats fall at offsetMs + k·beatMs in the audio file. With meta.offsetMs = offsetMs, beat k is at song time k·beatMs (what level times use). bars[].songTime and strongHits[].songTime are song times. If the strong hits sit half a beat off the grid, the beat is probably at offsetIfOffBeat instead.",
            ["beats"] = (int)Math.Floor((lengthMs - offset) / beatMs) + 1,
            ["bars"] = bars,
            ["sections"] = sections,
            ["strongHits"] = new JsonArray(hits.Select(h => (JsonNode)new JsonObject
            {
                ["ms"] = Math.Round(h.Ms, 1),
                ["songTime"] = Math.Round(h.Ms - offset, 1),
                ["beat"] = Math.Round((h.Ms - offset) / beatMs, 2),
                ["strength"] = Math.Round(h.Strength, 2),
            }).ToArray()),
        };
    }

    private static float[] Mono(PcmAudio pcm)
    {
        int ch = pcm.Channels, n = pcm.Frames;
        var m = new float[n];
        for (int f = 0; f < n; f++)
        {
            float v = 0;
            for (int c = 0; c < ch; c++) v += pcm.Samples[f * ch + c];
            m[f] = v / ch;
        }
        return m;
    }

    /// <summary>
    /// How much earlier than the sound the flux peaks: an onset raises the windowed energy most while it
    /// passes the first quarter of the Hann window.
    /// </summary>
    private static double LatencyMs(int sr) => Window / 4.0 / sr * 1000;

    /// <summary>Spectral flux (log magnitude, half-wave rectified), the same below 200 Hz, and RMS per frame.</summary>
    private static (double[] onset, double[] low, double[] rms) Onsets(float[] x, int hop, int sr)
    {
        int frames = Math.Max(1, x.Length / hop);
        int lowBins = Math.Max(2, (int)(200.0 * Window / sr));
        var onset = new double[frames];
        var low = new double[frames];
        var rms = new double[frames];
        var re = new double[Window];
        var im = new double[Window];
        var prev = new double[Window / 2];
        var hann = new double[Window];
        for (int i = 0; i < Window; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1));
        for (int f = 0; f < frames; f++)
        {
            int c = f * hop;
            double e = 0;
            for (int i = 0; i < Window; i++)
            {
                int k = c - Window / 2 + i;
                double v = k >= 0 && k < x.Length ? x[k] : 0;
                if (i >= Window / 2 - hop / 2 && i < Window / 2 + hop / 2) e += v * v;
                re[i] = v * hann[i];
                im[i] = 0;
            }
            rms[f] = Math.Sqrt(e / Math.Max(1, hop));
            Fft(re, im);
            double flux = 0, lowFlux = 0;
            for (int b = 1; b < Window / 2; b++)
            {
                double mag = Math.Log(1 + 100 * Math.Sqrt(re[b] * re[b] + im[b] * im[b]));
                double d = mag - prev[b];
                if (d > 0) { flux += d; if (b <= lowBins) lowFlux += d; }
                prev[b] = mag;
            }
            onset[f] = flux;
            low[f] = lowFlux;
        }
        // Remove the slow trend so loud passages do not dominate.
        foreach (var v in new[] { onset, low })
        {
            var smooth = MovingAverage(v, 50);
            for (int i = 0; i < frames; i++) v[i] = Math.Max(0, v[i] - smooth[i]);
        }
        return (onset, low, rms);
    }

    private static double[] MovingAverage(double[] v, int half)
    {
        var res = new double[v.Length];
        double sum = 0;
        int lo = 0, hi = -1;
        for (int i = 0; i < v.Length; i++)
        {
            while (hi < Math.Min(v.Length - 1, i + half)) sum += v[++hi];
            while (lo < i - half) sum -= v[lo++];
            res[i] = sum / (hi - lo + 1);
        }
        return res;
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }

    /// <summary>Tempo candidates from the onset autocorrelation, weighted towards common tempos.</summary>
    private static List<(double Bpm, double Score)> TempoCandidates(double[] onset)
    {
        var res = new List<(double, double)>();
        int n = onset.Length;
        if (n < 400) return res;
        var scores = new List<(double Bpm, double Score)>();
        for (double bpm = 60; bpm <= 200; bpm += 0.5)
        {
            double lag = 60000 / bpm / FrameMs;
            double s = 0;
            int l0 = (int)Math.Floor(lag);
            double fr = lag - l0;
            for (int i = 0; i + l0 + 1 < n; i++) s += onset[i] * (onset[i + l0] * (1 - fr) + onset[i + l0 + 1] * fr);
            // Also reward the half and double periods, so the main pulse wins over a subdivision.
            double weight = Math.Exp(-0.5 * Math.Pow(Math.Log(bpm / 120, 2) / 0.9, 2));
            scores.Add((bpm, s * weight));
        }
        double max = scores.Max(s => s.Score);
        if (max <= 0) return res;
        for (int i = 1; i < scores.Count - 1; i++)
            if (scores[i].Score >= scores[i - 1].Score && scores[i].Score >= scores[i + 1].Score)
                res.Add((scores[i].Bpm, scores[i].Score / max));
        return res.OrderByDescending(r => r.Item2).ToList();
    }

    /// <summary>Fine tempo: the comb over the whole song that lines up best with the onsets.</summary>
    private static double Refine(double[] onset, double bpm)
    {
        double best = bpm, bestScore = double.MinValue;
        for (double b = bpm - 1; b <= bpm + 1; b += 0.01)
        {
            Phase(onset, 60000 / b, out double score);
            if (score > bestScore) { bestScore = score; best = b; }
        }
        // Most songs are made at a whole (or half) tempo: prefer it when it fits almost as well.
        foreach (var round in new[] { Math.Round(best), Math.Round(best * 2) / 2 })
        {
            if (Math.Abs(round - best) > 0.15) continue;
            Phase(onset, 60000 / round, out double score);
            if (score >= bestScore * 0.985) return round;
        }
        return best;
    }

    private static double Phase(double[] onset, double beatMs) => Phase(onset, beatMs, out _);

    private static double Comb(double[] onset, double phase, double beatMs)
    {
        double s = 0;
        for (double t = phase; t / FrameMs < onset.Length - 1; t += beatMs)
        {
            int i = (int)(t / FrameMs);
            s += Math.Max(onset[i], onset[i + 1]);
        }
        return s;
    }

    /// <summary>The first beat (ms into the file) whose comb collects the most onset strength.</summary>
    private static double Phase(double[] onset, double beatMs, out double bestScore)
    {
        bestScore = double.MinValue;
        double best = 0;
        for (double p = 0; p < beatMs; p += 2)
        {
            double s = 0;
            for (double t = p; t / FrameMs < onset.Length - 1; t += beatMs)
            {
                int i = (int)(t / FrameMs);
                s += Math.Max(onset[i], onset[i + 1]);
            }
            if (s > bestScore) { bestScore = s; best = p; }
        }
        return best;
    }

    private static double Mean(double[] v, double fromMs, double toMs)
    {
        int a = Math.Max(0, (int)(fromMs / FrameMs)), b = Math.Min(v.Length, (int)(toMs / FrameMs));
        if (b <= a) return 0;
        double s = 0;
        for (int i = a; i < b; i++) s += v[i];
        return s / (b - a);
    }

    /// <summary>Splits the song where the bar loudness changes a lot (at least 4 bars apart).</summary>
    private static JsonArray Sections(List<double> energy, double offset, double barMs, double lengthMs)
    {
        var res = new JsonArray();
        if (energy.Count == 0) return res;
        var starts = new List<int> { 0 };
        for (int i = 2; i < energy.Count - 1; i++)
        {
            double before = (energy[i - 1] + energy[i - 2]) / 2, after = (energy[i] + energy[i + 1]) / 2;
            if (Math.Abs(after - before) > 0.18 && i - starts[^1] >= 4) starts.Add(i);
        }
        for (int s = 0; s < starts.Count; s++)
        {
            int a = starts[s], b = s + 1 < starts.Count ? starts[s + 1] : energy.Count;
            double e = energy.Skip(a).Take(b - a).Average();
            res.Add(new JsonObject
            {
                ["fromBar"] = a + 1,
                ["toBar"] = b,
                ["startMs"] = Math.Round(offset + a * barMs, 1),
                ["endMs"] = Math.Round(Math.Min(lengthMs, offset + b * barMs), 1),
                ["energy"] = Math.Round(e, 3),
                ["feel"] = e > 0.75 ? "intense" : e > 0.5 ? "driving" : e > 0.25 ? "calm" : "quiet",
            });
        }
        return res;
    }

    private static List<(double Ms, double Strength)> StrongHits(double[] onset, int count, double beatMs, double latencyMs)
    {
        var peaks = new List<(double, double)>();
        double mean = onset.Average(), sd = Math.Sqrt(onset.Select(v => (v - mean) * (v - mean)).Average());
        for (int i = 3; i < onset.Length - 3; i++)
        {
            double v = onset[i];
            if (v <= mean + 2 * sd) continue;
            bool peak = true;
            for (int k = -3; k <= 3 && peak; k++) if (k != 0 && onset[i + k] > v) peak = false;
            if (peak) peaks.Add((i * FrameMs + latencyMs, (v - mean) / Math.Max(1e-9, sd)));
        }
        // Keep the strongest, at least half a beat apart.
        var chosen = new List<(double Ms, double Strength)>();
        foreach (var p in peaks.OrderByDescending(p => p.Item2))
        {
            if (chosen.Any(c => Math.Abs(c.Ms - p.Item1) < beatMs / 2)) continue;
            chosen.Add(p);
            if (chosen.Count >= count) break;
        }
        return chosen.OrderBy(c => c.Ms).ToList();
    }
}
