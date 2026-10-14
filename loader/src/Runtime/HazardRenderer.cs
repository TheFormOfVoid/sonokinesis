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
    /// <summary>Script shapes drawn in front of the players (front: true).</summary>
    private readonly DynamicMesh front;
    /// <summary>The strongest flash drawn this frame (0 to 1).</summary>
    public float FlashCover { get; private set; }
    private readonly DynamicMesh background;
    /// <summary>Backdrop objects (layer at or below LevelEvaluator.BackdropLayer), behind everything.</summary>
    private readonly DynamicMesh backdrop;
    private readonly List<DrawItem> items = new();
    private readonly List<Rgba> flashes = new();

    public bool Visible { get; private set; }

    public HazardRenderer(Transform host)
    {
        background = new DynamicMesh("Beatform Background", host);
        backdrop = new DynamicMesh("Beatform Backdrop", host);
        hazards = new DynamicMesh("Beatform Hazards", host);
        // Drawn after the game's own sprites, so they cover the players.
        front = new DynamicMesh("Beatform Front", host, 3100);
        overlay = new DynamicMesh("Beatform Overlay", host, 3101);
        SetVisible(false);
    }

    public void SetVisible(bool v)
    {
        Visible = v;
        background.SetActive(v);
        backdrop.SetActive(v);
        hazards.SetActive(v);
        front.SetActive(v);
        overlay.SetActive(v);
    }

    /// <summary>
    /// Rebuilds the meshes for song time t. <paramref name="camera"/> is the gameplay layer's
    /// concatenated matrix (translation in world units, y up); <paramref name="hazardZ"/> is the depth
    /// just in front of the players.
    /// </summary>
    public List<DrawItem> Draw(LevelEvaluator ev, double t, Matrix4x4 camera, float hazardZ, float overlayZ)
    {
        ev.Collect(t, items);

        float camX = camera.m03, camY = camera.m13;
        float zoom = Mathf.Sqrt(camera.m00 * camera.m00 + camera.m10 * camera.m10);
        if (zoom <= 0.0001f) zoom = 1;
        float camRot = Mathf.Atan2(camera.m10, camera.m00) * Mathf.Rad2Deg;

        DrawBackground(ev, t);

        // Backdrop objects go in their own mesh far behind the players and the game's effects; the
        // rest just behind the players.
        Fill(backdrop, Pass.Backdrop);
        backdrop.End(new Vector3(camX, camY, BackgroundZ - 1), zoom, camRot);
        Fill(hazards, Pass.Hazards);
        hazards.End(new Vector3(camX, camY, hazardZ), zoom, camRot);
        Fill(front, Pass.Front);
        front.End(new Vector3(camX, camY, overlayZ + 0.002f), zoom, camRot);

        ev.Flashes(t, flashes);
        FlashCover = 0;
        overlay.Begin(flashes.Count * 4, flashes.Count * 6);
        foreach (var f in flashes)
        {
            overlay.AddQuad(0, 0, FullScreenW, FullScreenH, ToColor32(f));
            FlashCover = Math.Max(FlashCover, f.A);
        }
        overlay.End(new Vector3(0, 0, overlayZ), 1);
        return items;
    }

    private enum Pass { Backdrop, Hazards, Front }

    private static Pass PassOf(in DrawItem it) => it.Front ? Pass.Front : LevelEvaluator.IsBackdrop(it) ? Pass.Backdrop : Pass.Hazards;

    private void Fill(DynamicMesh mesh, Pass pass)
    {
        int vCount = 0, iCount = 0;
        foreach (var it in items)
        {
            if (PassOf(it) != pass) continue;
            vCount += it.Geometry.Vertices.Length; iCount += it.Geometry.Indices.Length;
        }
        mesh.Begin(vCount, iCount);
        foreach (var it in items)
        {
            if (PassOf(it) != pass) continue;
            var g = it.Geometry;
            var c = ToColor32(it.Color);
            int baseIndex = mesh.VertexCount;
            var m = it.Transform;
            var verts = g.Vertices;
            for (int k = 0; k < verts.Length; k++)
            {
                var u = verts[k];
                mesh.AddVertex((float)(m.A * u.X + m.C * u.Y + m.Tx), (float)-(m.B * u.X + m.D * u.Y + m.Ty), c);
            }
            var idx = g.Indices;
            for (int k = 0; k < idx.Length; k++) mesh.AddIndex(baseIndex + idx[k]);
        }
    }

    /// <summary>Shows only the background (at the last song time); hazards and flashes are cleared.</summary>
    public void DrawBackgroundOnly(LevelEvaluator ev, double t)
    {
        DrawBackground(ev, double.IsNaN(t) ? 0 : t);
        backdrop.Begin(0, 0);
        backdrop.End(Vector3.zero, 1);
        hazards.Begin(0, 0);
        hazards.End(Vector3.zero, 1);
        front.Begin(0, 0);
        front.End(Vector3.zero, 1);
        overlay.Begin(0, 0);
        overlay.End(Vector3.zero, 1);
    }

    private void DrawBackground(LevelEvaluator ev, double t)
    {
        // A solid quad far behind everything, not affected by camera shake.
        background.Begin(4, 6);
        background.AddQuad(0, 0, FullScreenW, FullScreenH, ToColor32(ev.BackgroundAt(t)));
        background.End(new Vector3(0, 0, BackgroundZ), 1);
    }

    private static Color32 ToColor32(Rgba c) =>
        new((byte)(Mathf.Clamp01(c.R) * 255), (byte)(Mathf.Clamp01(c.G) * 255), (byte)(Mathf.Clamp01(c.B) * 255), (byte)(Mathf.Clamp01(c.A) * 255));

    public void Destroy()
    {
        background.Destroy();
        backdrop.Destroy();
        hazards.Destroy();
        front.Destroy();
        overlay.Destroy();
    }

    /// <summary>
    /// A mesh rebuilt every frame, written straight into Unity's buffers. Buffers have a capacity that
    /// only grows (index capacity stays a multiple of 3, as Unity requires); unused triangles are
    /// collapsed to a point, so only the plain vertices/colors32/triangles setters are needed.
    /// </summary>
    private sealed unsafe class DynamicMesh
    {
        private readonly string name;
        private readonly Transform host;
        private GameObject go;
        private Mesh mesh;
        private Il2CppStructArray<Vector3> verts;
        private Il2CppStructArray<Color32> colors;
        private Il2CppStructArray<int> tris;
        // Raw element pointers: indexer access through the interop layer is far too slow for a mesh
        // rebuilt every frame. The il2cpp heap does not move objects, so these stay valid.
        private Vector3* vPtr;
        private Color32* cPtr;
        private int* iPtr;
        private int capacityV, capacityI;
        private int lastUsedI;
        private bool active;
        private int indexCount;
        public int VertexCount { get; private set; }

        private readonly int queue;

        public DynamicMesh(string name, Transform host, int renderQueue = 3000)
        {
            this.name = name;
            this.host = host;
            queue = renderQueue;
            EnsureAlive();
        }

        /// <summary>The game destroys scene objects during some transitions; rebuild ours if that happened.</summary>
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
            var mat = new Material(shader) { renderQueue = queue };
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
            capacityI = (i + 2) / 3 * 3;
            verts = new Il2CppStructArray<Vector3>(capacityV);
            colors = new Il2CppStructArray<Color32>(capacityV);
            tris = new Il2CppStructArray<int>(capacityI);
            vPtr = (Vector3*)Elements(verts);
            cPtr = (Color32*)Elements(colors);
            iPtr = (int*)Elements(tris);
            mesh.Clear();
            mesh.indexFormat = capacityV > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            lastUsedI = capacityI;
        }

        /// <summary>First element of an il2cpp array: after its header of four pointer-sized fields.</summary>
        private static byte* Elements(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase array) =>
            (byte*)array.Pointer + 4 * IntPtr.Size;

        private static int Grow(int current, int needed)
        {
            int c = Math.Max(1, current);
            while (c < needed) c *= 2;
            return c;
        }

        /// <summary>Starts a frame that will add at most <paramref name="vCount"/> vertices and <paramref name="iCount"/> indices.</summary>
        public void Begin(int vCount, int iCount)
        {
            EnsureAlive();
            if (vCount > capacityV || iCount > capacityI) Allocate(Grow(capacityV, vCount), Grow(capacityI, iCount));
            VertexCount = 0;
            indexCount = 0;
        }

        public void AddVertex(float x, float y, Color32 c)
        {
            vPtr[VertexCount] = new Vector3(x, y, 0);
            cPtr[VertexCount] = c;
            VertexCount++;
        }

        public void AddIndex(int i) => iPtr[indexCount++] = i;

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

        public void End(Vector3 position, float scale, float rotationDeg = 0)
        {
            // Collapse whatever part of the index buffer was used last frame but not this one.
            int clearTo = Math.Min(lastUsedI, capacityI);
            for (int i = indexCount; i < clearTo; i++) iPtr[i] = 0;
            lastUsedI = indexCount;

            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000, 100000, 10));

            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, scale, 1);
            go.transform.rotation = Quaternion.Euler(0, 0, rotationDeg);
        }

        public void Destroy() { if (go != null) UnityEngine.Object.Destroy(go); }
    }
}
