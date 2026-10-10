using System;
using System.Collections.Generic;
using Beatform.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Beatform.Loader.Runtime;

/// <summary>
/// Draws a level with Beatform's own geometry: a background quad behind everything, all objects
/// in one dynamic mesh placed just in front of the players (where the game draws its hazards), and
/// full-screen flashes over them. Nothing here uses the game's art.
/// </summary>
internal sealed class HazardRenderer
{
    private const float BackgroundZ = 50f;
    private const float FullScreenW = 4000f, FullScreenH = 3000f;

    private readonly DynamicMesh hazards;
    private readonly DynamicMesh overlay;
    private readonly DynamicMesh background;
    private readonly List<DrawItem> items = new();
    private readonly List<Rgba> flashes = new();

    public bool Visible { get; private set; }

    public HazardRenderer(Transform host)
    {
        background = new DynamicMesh("Beatform Background", host);
        hazards = new DynamicMesh("Beatform Hazards", host);
        overlay = new DynamicMesh("Beatform Overlay", host);
        SetVisible(false);
    }

    public void SetVisible(bool v)
    {
        Visible = v;
        background.SetActive(v);
        hazards.SetActive(v);
        overlay.SetActive(v);
    }

    /// <summary>
    /// Rebuilds the meshes for song time t. <paramref name="camera"/> is the gameplay layer's
    /// concatenated matrix (translation in world units, y up); <paramref name="hazardZ"/> is the depth
    /// just in front of the players.
    /// </summary>
    public List<DrawItem> Draw(LevelEvaluator ev, double t, Matrix4x4 camera, float hazardZ)
    {
        ev.Collect(t, items);

        float camX = camera.m03, camY = camera.m13;
        float zoom = Mathf.Sqrt(camera.m00 * camera.m00 + camera.m10 * camera.m10);
        if (zoom <= 0.0001f) zoom = 1;

        // Background: a solid quad far behind everything, not affected by camera shake.
        background.Begin();
        background.AddQuad(0, 0, FullScreenW, FullScreenH, ToColor32(ev.BackgroundAt(t)));
        background.End(new Vector3(0, 0, BackgroundZ), 1);

        hazards.Begin();
        foreach (var it in items)
        {
            var g = it.Geometry;
            var c = ToColor32(it.Color);
            int baseIndex = hazards.VertexCount;
            var m = it.Transform;
            foreach (var u in g.Vertices)
            {
                var p = m.Apply(u);
                hazards.AddVertex((float)p.X, (float)-p.Y, c);
            }
            foreach (var i in g.Indices) hazards.AddIndex(baseIndex + i);
        }
        hazards.End(new Vector3(camX, camY, hazardZ), zoom);

        overlay.Begin();
        ev.Flashes(t, flashes);
        foreach (var f in flashes) overlay.AddQuad(0, 0, FullScreenW, FullScreenH, ToColor32(f));
        overlay.End(new Vector3(0, 0, hazardZ - 0.003f), 1);
        return items;
    }

    /// <summary>Shows only the background (at the last song time); hazards and flashes are cleared.</summary>
    public void DrawBackgroundOnly(LevelEvaluator ev, double t)
    {
        if (double.IsNaN(t)) t = 0;
        background.Begin();
        background.AddQuad(0, 0, FullScreenW, FullScreenH, ToColor32(ev.BackgroundAt(t)));
        background.End(new Vector3(0, 0, BackgroundZ), 1);
        hazards.Begin();
        hazards.End(Vector3.zero, 1);
        overlay.Begin();
        overlay.End(Vector3.zero, 1);
    }

    private static Color32 ToColor32(Rgba c) =>
        new((byte)(Mathf.Clamp01(c.R) * 255), (byte)(Mathf.Clamp01(c.G) * 255), (byte)(Mathf.Clamp01(c.B) * 255), (byte)(Mathf.Clamp01(c.A) * 255));

    public void Destroy()
    {
        background.Destroy();
        hazards.Destroy();
        overlay.Destroy();
    }

    /// <summary>
    /// A mesh rebuilt every frame. Buffers have a fixed capacity that only grows; unused triangles are
    /// collapsed to a point, so only the plain vertices/colors32/triangles setters are needed.
    /// </summary>
    private sealed class DynamicMesh
    {
        private readonly string name;
        private readonly Transform host;
        private GameObject go;
        private Mesh mesh;
        private Il2CppStructArray<Vector3> verts;
        private Il2CppStructArray<Color32> colors;
        private Il2CppStructArray<int> tris;
        private int capacityV, capacityI;
        private int lastUsedI;
        private bool active;
        public int VertexCount { get; private set; }
        private int indexCount;

        public DynamicMesh(string name, Transform host)
        {
            this.name = name;
            this.host = host;
            EnsureAlive();
        }

        /// <summary>
        /// The game destroys scene objects during some transitions; rebuild ours if that happened.
        /// </summary>
        private void EnsureAlive()
        {
            if (go != null && mesh != null) return;
            go = new GameObject(name);
            go.transform.SetParent(host, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mf.mesh = mesh;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            var mat = new Material(shader);
            mat.renderQueue = 3000;
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Allocate(Math.Max(capacityV, 1024), Math.Max(capacityI, 3072));
            go.SetActive(active);
        }

        public void SetActive(bool v)
        {
            active = v;
            EnsureAlive();
            go.SetActive(v);
        }

        private void Allocate(int v, int i)
        {
            capacityV = v;
            capacityI = i;
            verts = new Il2CppStructArray<Vector3>(v);
            colors = new Il2CppStructArray<Color32>(v);
            tris = new Il2CppStructArray<int>(i);
            mesh.Clear();
            mesh.indexFormat = v > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            lastUsedI = capacityI;
        }

        // Geometry is first collected in managed lists so capacity can be checked before copying.
        private readonly List<Vector3> pendingV = new();
        private readonly List<Color32> pendingC = new();
        private readonly List<int> pendingI = new();

        public void Begin()
        {
            pendingV.Clear();
            pendingC.Clear();
            pendingI.Clear();
            VertexCount = 0;
            indexCount = 0;
        }

        public void AddVertex(float x, float y, Color32 c)
        {
            pendingV.Add(new Vector3(x, y, 0));
            pendingC.Add(c);
            VertexCount++;
        }

        public void AddIndex(int i)
        {
            pendingI.Add(i);
            indexCount++;
        }

        public void AddQuad(float cx, float cy, float w, float h, Color32 c)
        {
            int b = VertexCount;
            AddVertex(cx - w / 2, cy - h / 2, c);
            AddVertex(cx + w / 2, cy - h / 2, c);
            AddVertex(cx + w / 2, cy + h / 2, c);
            AddVertex(cx - w / 2, cy + h / 2, c);
            AddIndex(b); AddIndex(b + 2); AddIndex(b + 1);
            AddIndex(b); AddIndex(b + 3); AddIndex(b + 2);
        }

        public void End(Vector3 position, float scale)
        {
            EnsureAlive();
            if (pendingV.Count > capacityV || pendingI.Count > capacityI)
                Allocate(Math.Max(capacityV * 2, NextPow2(pendingV.Count)), Math.Max(capacityI * 2, NextPow2(pendingI.Count)));

            for (int i = 0; i < pendingV.Count; i++)
            {
                verts[i] = pendingV[i];
                colors[i] = pendingC[i];
            }
            for (int i = 0; i < pendingI.Count; i++) tris[i] = pendingI[i];
            // Collapse the rest of the index buffer that was used last frame.
            int clearTo = Math.Min(lastUsedI, capacityI);
            for (int i = pendingI.Count; i < clearTo; i++) tris[i] = 0;
            lastUsedI = pendingI.Count;

            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000, 100000, 10));

            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, scale, 1);
        }

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }

        public void Destroy() { if (go != null) UnityEngine.Object.Destroy(go); }
    }
}
