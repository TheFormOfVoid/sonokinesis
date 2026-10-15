using System.Linq;
using System.Text;
using Il2CppSystem.Collections.Generic;

namespace Sonokinesis.Loader.Dev;

/// <summary>Game-specific dev-console commands used while reverse engineering the level flow.</summary>
public static class GameCommands
{
    private static UnityEngine.Networking.UnityWebRequest pendingAudio;
    private static readonly System.Collections.Generic.List<string> camSamples = new();
    private static int camFrames;

    /// <summary>Called every frame by the dev console.</summary>
    private static readonly System.Collections.Generic.List<float> frameSamples = new();
    private static int frameWatch;
    private static readonly System.Collections.Generic.List<string> clockSamples = new();
    private static int clockFrames;

    private static int shotIn;
    private static string shotName;

    public static void Tick()
    {
        if (shotIn > 0 && --shotIn == 0)
            UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Plugin.DataDir, "shots", shotName + ".png"), 1);
        if (frameWatch > 0) { frameWatch--; frameSamples.Add(UnityEngine.Time.unscaledDeltaTime * 1000f); }
        if (clockFrames > 0)
        {
            clockFrames--;
            var music = Runtime.GameBridge.MusicPositionMs(Runtime.GameBridge.LevelLogic());
            clockSamples.Add($"{UnityEngine.Time.unscaledTime * 1000:0.0} {music:0.0} {Runtime.SonokinesisRuntime.Instance.LastSongTime:0.0}");
        }
        if (camFrames <= 0) return;
        camFrames--;
        try
        {
            var layers = CameraFlashContainers.instance;
            var c = layers.container;
            var m = layers.enemyVisual.transform.concatenedMatrix;
            var cam = CameraFlash.mainCamera;
            var uc = UnityEngine.Camera.main;
            camSamples.Add($"f={UnityEngine.Time.frameCount} layer t=({m.m03:0.##},{m.m13:0.##}) unityCam=({uc?.transform.position.x:0.##},{uc?.transform.position.y:0.##}) container x={c.x:0.##} y={c.y:0.##} rot={c.rotation:0.##}");
        }
        catch (System.Exception e) { camSamples.Add("!" + e.Message); }
    }

    public static void Register()
    {
        DevConsole.Commands["practice"] = a =>
        {
            var song = ModelStoryCheckpointEnum.ALL[int.Parse(a[0])];
            var list = new List<ModelStoryCheckpoint>();
            list.Add(song);
            MainGame.instance.StartPractice(list, ModelGameDifficultyEnum.NORMAL);
            return "started practice: " + song.id;
        };

        DevConsole.Commands["customtest"] = a =>
        {
            float secs = a.Length > 0 ? float.Parse(a[0]) : 40;
            var clip = Runtime.CustomSongRegistry.MakeClickTrack(secs, 120);
            var model = Runtime.CustomSongRegistry.Create("clicktest", "Click Test", "Sonokinesis", clip, new[] { 0f, 15000f, 30000f });
            var list = new List<ModelStoryCheckpoint>();
            list.Add(model);
            MainGame.instance.StartPractice(list, ModelGameDifficultyEnum.NORMAL);
            return $"started {model.id} checkpoints={model.metaSong.allCheckPoints.Count} spawns={model.metaSong.allSpawnsInOrder.Count} clip={clip.length}s";
        };

        DevConsole.Commands["ui"] = _ =>
        {
            var sb = new StringBuilder();
            var list = UIBase.manager.actorList;
            for (int i = 0; i < list.Count; i++)
            {
                var actor = list[i];
                sb.AppendLine($"{i}: {actor.TryCast<Il2CppSystem.Object>().GetIl2CppType().Name} destroyed={actor.destroyed} enabled={actor.enabled}");
            }
            return sb.ToString();
        };

        DevConsole.Commands["uicall"] = a =>
        {
            // uicall <UiTypeName> <method>: call a no-argument method on the first live UI actor of that type.
            var type = DevConsole.FindType(a[0]) ?? throw new System.Exception("no type " + a[0]);
            var list = UIBase.manager.actorList;
            for (int i = 0; i < list.Count; i++)
            {
                var actor = list[i];
                if (actor.destroyed || actor.TryCast<Il2CppSystem.Object>().GetIl2CppType().Name != type.Name) continue;
                var typed = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod("TryCast").MakeGenericMethod(type).Invoke(actor, null);
                var m = type.GetMethod(a[1], System.Type.EmptyTypes) ?? throw new System.Exception("no method " + a[1]);
                var r = m.Invoke(typed, null);
                return $"called {type.Name}.{a[1]} -> {DevConsole.Fmt(r, 0)}";
            }
            return "no live " + a[0];
        };

        DevConsole.Commands["savecheck"] = a =>
        {
            // savecheck <file> <outFile> <needle>: decrypt a save file copy with the game's own crypto,
            // write the clear text next to it and report whether the needle occurs.
            var bytes = System.IO.File.ReadAllBytes(a[0]);
            // The file is a BinaryWriter string (7-bit length prefix) holding XOR-obfuscated text.
            using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes), System.Text.Encoding.UTF8);
            var text = SimpleCrypto.EncryptDecryptXOR(reader.ReadString());
            System.IO.File.WriteAllText(a[1], text);
            return $"decrypted {bytes.Length} bytes -> {text.Length} chars; contains '{a[2]}': {text.Contains(a[2])}";
        };

        DevConsole.Commands["audiotest"] = a =>
        {
            // audiotest <path> <MPEG|OGGVORBIS|WAV>: start a UnityWebRequest audio load; poll with audioresult.
            var type = (UnityEngine.AudioType)System.Enum.Parse(typeof(UnityEngine.AudioType), a[1]);
            var uri = new System.Uri(a[0]).AbsoluteUri;
            pendingAudio = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(uri, type);
            pendingAudio.SendWebRequest();
            return "loading " + uri;
        };
        DevConsole.Commands["audioresult"] = _ =>
        {
            if (pendingAudio == null) return "nothing pending";
            if (!pendingAudio.isDone) return "not done";
            var err = pendingAudio.error;
            UnityEngine.AudioClip clip = null;
            try { clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(pendingAudio); } catch (System.Exception e) { err += " / " + e.Message; }
            var res = $"error={err ?? "none"} clip={(clip == null ? "null" : $"{clip.length}s ch={clip.channels} hz={clip.frequency} state={clip.loadState}")}";
            pendingAudio.Dispose();
            pendingAudio = null;
            return res;
        };

        DevConsole.Commands["kick"] = a =>
        {
            CameraFlash.mainCamera.kickback(float.Parse(a[0]), int.Parse(a[1]));
            return "kicked";
        };
        DevConsole.Commands["elastic"] = a =>
        {
            // elastic X Y TIME: the game's elastic camera kick, sampled like camwatch.
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            camSamples.Clear();
            camFrames = 60;
            CameraEffectKickBackElastic.addToActor(CameraFlash.mainCamera, float.Parse(a[0], inv), float.Parse(a[1], inv), float.Parse(a[2], inv));
            return "elastic kick";
        };
        DevConsole.Commands["kickshot"] = a =>
        {
            // kickshot ANGLE FORCE NAME [FRAMES]: a game camera kick, then a screenshot a few frames later.
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Plugin.DataDir, "shots"));
            if (int.Parse(a[1]) > 0) CameraFlash.mainCamera.kickback(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(a[1]));
            shotName = a[2];
            shotIn = a.Length > 3 ? int.Parse(a[3]) : 4;
            return "kick and shot";
        };
        DevConsole.Commands["camwatch"] = a =>
        {
            // camwatch: sample the gameplay camera container for the next frames (see camlog).
            camSamples.Clear();
            camFrames = a.Length > 0 ? int.Parse(a[0]) : 30;
            return "watching";
        };
        DevConsole.Commands["camlog"] = _ => string.Join("\n", camSamples);

        DevConsole.Commands["seek"] = a =>
        {
            var logic = ActorNormalLevelLogic.getNormalLevelLogic();
            logic.logicEnemyCreator.putMusicTo(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture));
            return "seeked to " + a[0];
        };
        DevConsole.Commands["invincible"] = a =>
        {
            var on = a.Length == 0 || a[0] != "off";
            ActorNormalLevelLogic.getNormalLevelLogic().logicPlayers.makeAllPlayersInvincible(on);
            return "invincible " + on;
        };
        DevConsole.Commands["pause"] = a =>
        {
            var on = a.Length == 0 || a[0] != "off";
            ActorNormalLevelLogic.getNormalLevelLogic().pause(on);
            return "paused " + on;
        };

        DevConsole.Commands["menu"] = a =>
        {
            var m = Runtime.SonokinesisRuntime.Instance.Menu;
            if (a.Length > 0 && a[0] == "off") m.Hide(); else m.Show();
            return "menu open " + m.Open;
        };

        DevConsole.Commands["menuplay"] = a =>
        {
            var m = Runtime.SonokinesisRuntime.Instance.Menu;
            if (!m.Open) m.Show();
            if (Runtime.LevelLibrary.Entries == null) return "still scanning, try again";
            m.PlayIndex(int.Parse(a[0]), a.Length > 1 && a[1] == "hard");
            return "menu play " + a[0];
        };
        DevConsole.Commands["levels"] = _ =>
        {
            var e = Runtime.LevelLibrary.Entries;
            if (e == null) return "scanning";
            return string.Join("\n", e.Select((x, i) => $"{i}: {x.Meta.Title} valid={x.IsValid} {x.Error}"));
        };
        DevConsole.Commands["frametime"] = a =>
        {
            frameSamples.Clear();
            frameWatch = a.Length > 0 ? int.Parse(a[0]) : 120;
            return "sampling";
        };
        DevConsole.Commands["clockprobe"] = a =>
        {
            // clockprobe N: record wall time, raw music time and the drawn song time for N frames.
            clockSamples.Clear();
            clockFrames = a.Length > 0 ? int.Parse(a[0]) : 120;
            return "sampling clock";
        };
        DevConsole.Commands["clocklog"] = _ => string.Join("\n", clockSamples);
        DevConsole.Commands["music"] = _ =>
        {
            // Everything the jukebox is playing: dynamic music (songs, menu themes) and sounds.
            var sb = new StringBuilder();
            var musics = JukeBox.allDynamicMusics;
            sb.AppendLine($"dynamic musics n={musics?.length} current={JukeBox.currentMusicComponent?.sfxViewDynamic?.debugSoundName}");
            for (int i = 0; musics != null && i < musics.length; i++)
            {
                var d = musics[i]?.sfxViewDynamic;
                sb.AppendLine($"  [{i}] {d?.debugSoundName} clip={d?.modelSong?.name} playing={d?.isPlaying} vol={d?.volume:0.##} pos={d?.position:0}");
            }
            var sounds = JukeBox.allSounds;
            sb.AppendLine($"sounds n={sounds?.length}");
            for (int i = 0; sounds != null && i < sounds.length; i++)
            {
                var v = sounds[i]?.sfxView;
                if (v != null && v.isPlaying) sb.AppendLine($"  [{i}] {v.debugSoundName} loop={v.looping} musicVol={v.bindToMusicVolume} pos={v.position:0}");
            }
            return sb.ToString();
        };
        DevConsole.Commands["audiosources"] = _ =>
        {
            var sb = new StringBuilder();
            foreach (var src in UnityEngine.Object.FindObjectsOfType<UnityEngine.AudioSource>())
                if (src != null && src.isPlaying)
                    sb.AppendLine($"{src.gameObject.name}: clip={src.clip?.name} len={src.clip?.length:0.0} loop={src.loop} vol={src.volume:0.00} t={src.time:0.0}");
            return sb.Length == 0 ? "nothing playing" : sb.ToString();
        };
        DevConsole.Commands["frames"] = _ =>
        {
            if (frameSamples.Count == 0) return "no samples";
            var sorted = frameSamples.OrderBy(x => x).ToList();
            return $"n={sorted.Count} avg={sorted.Average():0.00}ms p50={sorted[sorted.Count / 2]:0.00}ms p95={sorted[(int)(sorted.Count * 0.95)]:0.00}ms max={sorted.Last():0.00}ms";
        };

        DevConsole.Commands["meshinfo"] = _ =>
        {
            var sb = new StringBuilder();
            foreach (var mf in UnityEngine.Object.FindObjectsOfType<UnityEngine.MeshFilter>())
            {
                if (!mf.gameObject.name.StartsWith("Sonokinesis")) continue;
                var m = mf.sharedMesh;
                sb.AppendLine($"{mf.gameObject.name}: verts={m.vertexCount} indexFormat={m.indexFormat} subMeshes={m.subMeshCount} indices={m.GetIndexCount(0)} active={mf.gameObject.activeInHierarchy}");
            }
            return sb.ToString();
        };

        DevConsole.Commands["profile"] = _ => Runtime.SonokinesisRuntime.Profile.Report();

        DevConsole.Commands["sandbox"] = _ =>
        {
            // Make the running level safe to observe unattended: endless rewinds, no splash overlay.
            var logic = ActorNormalLevelLogic.getNormalLevelLogic();
            if (logic == null) return "no level running";
            logic.logicRewindMusic.infiniteRewind = true;
            var ui = DefaultMainGame.UI_CONTAINER;
            for (var d = ui.firstChild; d != null; d = d.nextSibling) d.visible = false;
            return "infinite rewind on, UI_CONTAINER children hidden";
        };

        DevConsole.Commands["tree"] = a =>
        {
            var root = DevConsole.Eval(a[0]) as DisplayObject;
            int maxDepth = a.Length > 1 ? int.Parse(a[1]) : 4;
            var sb = new StringBuilder();
            void Walk(DisplayObject d, int depth)
            {
                var go = d.hasMetaGo && d.metaSpriteGo != null ? d.metaSpriteGo : null;
                string goInfo = "";
                if (go != null)
                {
                    try { goInfo = " go=" + DevConsole.Fmt(go, 0); } catch { }
                }
                sb.Append(new string(' ', depth * 2)).AppendLine($"{d.name} <{d.TryCast<Il2CppSystem.Object>().GetIl2CppType().Name}> [{d.className}] x={d.x:0.#} y={d.y:0.#} vis={d.visible}{goInfo}");
                if (depth >= maxDepth) return;
                var c = d.TryCast<DisplayObjectContainer>();
                if (c == null) return;
                for (var ch = c.firstChild; ch != null; ch = ch.nextSibling) Walk(ch, depth + 1);
            }
            Walk(root, 0);
            return sb.ToString();
        };

        DevConsole.Commands["level"] = _ =>
        {
            var logic = ActorNormalLevelLogic.getNormalLevelLogic();
            if (logic == null) return "no level running";
            var sb = new StringBuilder();
            sb.AppendLine($"song={logic.modelSong?.id} paused={logic.isPaused()}");
            var creator = logic.logicEnemyCreator;
            if (creator != null)
            {
                sb.AppendLine($"spawnCreator next={creator.indexOfNext} metaSong={(creator.metaSong == null ? "null" : creator.metaSong.songName)}");
                var music = creator.sfxView;
                if (music != null) sb.AppendLine($"music pos={music.position} len={music.length} playing={music.isPlaying} start={music.startTime}");
            }
            var cp = logic.logicCheckPoint;
            if (cp != null) sb.AppendLine($"lastCheckPoint={cp.lastCheckPoint}");
            var rw = logic.logicRewindMusic;
            if (rw != null) sb.AppendLine($"rewindLeft={rw.rewindLeft} rewinding={rw.isRewinding()}");
            return sb.ToString();
        };

        DevConsole.Commands["heroes"] = _ =>
        {
            var sb = new StringBuilder();
            var cols = Hero.allCollisionEnemy;
            sb.AppendLine($"allCollisionEnemy n={cols?.length}");
            for (int i = 0; cols != null && i < cols.length; i++)
            {
                var c = cols[i];
                var actor = c.actor;
                var hero = actor?.TryCast<Hero>();
                var circle = c.circleCol;
                sb.Append($"  [{i}] actor=({actor?.px:0.#}, {actor?.py:0.#})");
                if (circle != null) sb.Append($" circle=({circle.x:0.#},{circle.y:0.#}) r={circle.radius}");
                sb.Append($" circles={c.circles?.length}");
                if (hero != null && hero.lifeComponent != null) sb.Append($" hp={hero.lifeComponent.hp}/{hero.lifeComponent.maxHp} dead={hero.lifeComponent.isDead}");
                var mp = hero?.modelPlayer;
                if (mp != null) sb.Append($" playerId={mp.playerId} number={mp.getPlayerNumber()} color={mp.color:X8}");
                sb.AppendLine();
            }
            return sb.ToString();
        };

        DevConsole.Commands["herospeed"] = a =>
        {
            var sb = new StringBuilder();
            var cols = Hero.allCollisionEnemy;
            for (int i = 0; cols != null && i < cols.length; i++)
            {
                var hc = cols[i].actor?.TryCast<Hero>()?.controlComponent;
                if (hc == null) continue;
                sb.AppendLine($"[{i}] speed={hc.speed} normal={hc.speedNormal} dash={hc.speedDash} dashing={hc.isDashing} input=({hc.moveInput?.x:0.##},{hc.moveInput?.y:0.##}) noMove={hc.NoMoveFrame}");
            }
            return sb.ToString();
        };

        DevConsole.Commands["herorender"] = _ =>
        {
            var sb = new StringBuilder();
            var cols = Hero.allCollisionEnemy;
            for (int i = 0; cols != null && i < cols.length; i++)
            {
                var r = cols[i].actor?.TryCast<Hero>()?.heroRenderer;
                if (r == null) continue;
                foreach (var (n, mc) in new[] { ("container", r.heroContainerMc), ("hero", r.heroMc), ("back", r.heroBackMc) })
                    if (mc != null) sb.AppendLine($"[{i}] {n}: rot={mc.rotation:0.##} sx={mc.scaleX:0.###} sy={mc.scaleY:0.###} x={mc.x:0.#} y={mc.y:0.#}");
                var ph = cols[i].actor.physicComponent;
                if (ph != null) sb.AppendLine($"[{i}] physics v=({ph.vx:0.##},{ph.vy:0.##}) angle={ph.getAngle():0.##} rotation={ph.getRotation():0.##}");
            }
            return sb.ToString();
        };

        DevConsole.Commands["coverhide"] = a =>
        {
            Runtime.SonokinesisRuntime.HideCoveredHeroes = a.Length == 0 || a[0] != "off";
            return "hide covered players " + Runtime.SonokinesisRuntime.HideCoveredHeroes;
        };

        DevConsole.Commands["playerspeed"] = a =>
        {
            // playerspeed <speed> [turn] | playerspeed level
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool level = a.Length == 0 || a[0] == "level";
            Runtime.SonokinesisRuntime.PlayerSpeedOverride = level ? null : float.Parse(a[0], inv);
            Runtime.SonokinesisRuntime.PlayerTurnOverride = level ? null : a.Length > 1 ? float.Parse(a[1], inv) : 1;
            return level ? "player speed from the level"
                : $"player speed {Runtime.SonokinesisRuntime.PlayerSpeedOverride} turn {Runtime.SonokinesisRuntime.PlayerTurnOverride}";
        };

        DevConsole.Commands["hit"] = a =>
        {
            float x = float.Parse(a[0]), y = float.Parse(a[1]), r = float.Parse(a[2]);
            var infos = CollisionCircleComponent.attackTargets(MainGame.dummyActor, Hero.allCollisionEnemy, x, y, r, 0, 0);
            return $"attackTargets -> {infos?.length} collisions";
        };

        DevConsole.Commands["hithero"] = a =>
        {
            float r = a.Length > 0 ? float.Parse(a[0]) : 10;
            var c = Hero.allCollisionEnemy[0];
            var before = c.actor.TryCast<Hero>().lifeComponent.hp;
            var infos = CollisionCircleComponent.attackTargets(MainGame.dummyActor, Hero.allCollisionEnemy, c.actor.px, c.actor.py, r, 0, 0);
            return $"at ({c.actor.px:0.#},{c.actor.py:0.#}) attackTargets -> {infos?.length} collisions; hp before={before} now={c.actor.TryCast<Hero>().lifeComponent.hp}";
        };

        DevConsole.Commands["chain"] = a =>
        {
            // Walk a display object's parents, printing local and concatenated transforms.
            var d = DevConsole.Eval(a[0]) as DisplayObject;
            var sb = new StringBuilder();
            while (d != null)
            {
                var t = d.transform;
                sb.AppendLine($"{d.name} [{d.className}] x={d.x:0.##} y={d.y:0.##} sx={d.scaleX:0.###} sy={d.scaleY:0.###} rot={d.rotation:0.##} visible={d.visible}");
                if (t != null) sb.AppendLine("    concat=" + DevConsole.Fmt(t.concatenedMatrix, 0));
                d = d.hasParent ? d.parent : null;
            }
            return sb.ToString();
        };

        DevConsole.Commands["set"] = a =>
        {
            // set <Type.path.Prop> <value>
            var path = a[0];
            var dot = path.LastIndexOf('.');
            var owner = DevConsole.Eval(path[..dot]);
            var name = path[(dot + 1)..];
            var type = owner as System.Type ?? owner.GetType();
            var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            object val = prop.PropertyType == typeof(bool) ? bool.Parse(a[1])
                : prop.PropertyType == typeof(float) ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture)
                : prop.PropertyType == typeof(int) ? int.Parse(a[1])
                : prop.PropertyType == typeof(string) ? a[1] : DevConsole.Eval(a[1]);
            prop.SetValue(owner is System.Type ? null : owner, val);
            return $"{path} = {DevConsole.Fmt(prop.GetValue(owner is System.Type ? null : owner), 0)}";
        };

        DevConsole.Commands["gfxtest"] = a =>
        {
            // gfxtest <x> <y> <r>: draw a filled circle and a rectangle with the engine's vector API
            // in a fresh layer of the gameplay camera.
            float x = float.Parse(a[0]), y = float.Parse(a[1]), r = float.Parse(a[2]);
            var layers = CameraFlashContainers.instance;
            var mc = new MovieClip();
            mc.name = "sonokinesisGfxTest";
            layers.enemyVisual.addChild(mc);
            mc.graphics.beginFill(unchecked((int)0xFF2A6D));
            mc.graphics.drawCircle(x, y, r);
            mc.graphics.endFill();
            mc.graphics.beginFill(unchecked((int)0xFF2A6D), 1f);
            mc.graphics.drawRect(x + r * 2, y - r, (int)(r * 2), (int)(r * 2));
            mc.graphics.endFill();
            return $"added {mc.name} children={layers.enemyVisual.numChildren}";
        };

        DevConsole.Commands["shaders"] = a =>
        {
            var sb = new StringBuilder();
            foreach (var name in new[] { "Hidden/Internal-Colored", "Sprites/Default", "Unlit/Color", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/2D/Sprite-Unlit-Default", "UI/Default" })
                sb.AppendLine($"{name}: {(UnityEngine.Shader.Find(name) != null ? "yes" : "no")}");
            return sb.ToString();
        };

        DevConsole.Commands["renderers"] = a =>
        {
            // List active mesh renderers (what the game draws with) and their sorting.
            var sb = new StringBuilder();
            var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.MeshRenderer>();
            foreach (var r in all)
            {
                var mat = r.sharedMaterial;
                sb.AppendLine($"{r.gameObject.name} pos={DevConsole.Fmt(r.transform.position, 0)} scale={DevConsole.Fmt(r.transform.lossyScale, 0)} layer={r.gameObject.layer} sortLayer={r.sortingLayerName} order={r.sortingOrder} shader={mat?.shader?.name} queue={mat?.renderQueue} bounds={DevConsole.Fmt(r.bounds.center, 0)}/{DevConsole.Fmt(r.bounds.size, 0)}");
            }
            return sb.ToString();
        };

        DevConsole.Commands["quadtest"] = a =>
        {
            // quadtest <x> <y> <z> <size>: place a solid quad in Unity world space.
            float x = float.Parse(a[0]), y = float.Parse(a[1]), z = float.Parse(a[2]), s = float.Parse(a[3]);
            var go = new UnityEngine.GameObject("sonokinesisQuadTest");
            var mf = go.AddComponent<UnityEngine.MeshFilter>();
            var mr = go.AddComponent<UnityEngine.MeshRenderer>();
            var mesh = new UnityEngine.Mesh();
            mesh.vertices = new UnityEngine.Vector3[] { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0) };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mf.mesh = mesh;
            var shader = UnityEngine.Shader.Find("Hidden/Internal-Colored");
            var mat = new UnityEngine.Material(shader);
            mat.color = new UnityEngine.Color(0f, 1f, 0.4f, 1f);
            mat.SetInt("_Cull", 0);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mr.material = mat;
            go.transform.position = new UnityEngine.Vector3(x, y, z);
            go.transform.localScale = new UnityEngine.Vector3(s, s, 1);
            return "quad at " + DevConsole.Fmt(go.transform.position, 0);
        };

        DevConsole.Commands["shot"] = a =>
        {
            var name = a.Length > 0 ? a[0] : "shot";
            var dir = System.IO.Path.Combine(Plugin.DataDir, "shots");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, name + ".png");
            UnityEngine.ScreenCapture.CaptureScreenshot(path, 1);
            return "capturing " + path;
        };
    }
}
