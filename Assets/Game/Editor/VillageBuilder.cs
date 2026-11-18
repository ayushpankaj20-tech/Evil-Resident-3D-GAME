using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the village level from the imported KayKit packs.
// Re-run from Tools > Village > Rebuild Level. Tuning values are the constants below.
public static class VillageBuilder
{
    const string KK = "Assets/ThirdParty/KayKit/";
    const string ScenePath = "Assets/Game/Scenes/Village.unity";
    const string MatDir = "Assets/Game/Materials/";

    static readonly Vector3 MoonDir = new Vector3(0.7f, 0.5f, 0.2f).normalized;
    static readonly Vector3 PlayerStart = new Vector3(-46f, 0f, -24f); // clearing in the woods
    const float PlayerYaw = 180f;                                       // faces south, down the start path
    static readonly Vector3 ArcCentre = new Vector3(-23f, 0f, -48f);    // half-circle path, radius ArcRadius
    const float ArcRadius = 23f;
    const float PathHalfWidth = 5.2f; // walkable half-width of the start path (invisible walls sit here)
    static readonly List<Vector3> startPeaks = new List<Vector3>(); // x, radius, z
    // mountains close to the start clearing: x, z, scale
    static readonly float[,] NearMountains = { { -90, -14, 22 }, { -84, -64, 24 }, { -48, -116, 24 } };

    // 2 = the game starts at the level-2 gate (for testing stages 2 and 3); set back to 1 for the full game
    const int TestStartLevel = 1;

    static System.Random rng;
    static Transform env, props, chars, lights;
    static GameObject playerGo, cameraGo, exitGateGo;

    static string Hw(string n) { return KK + "Halloween/" + n + ".gltf"; }
    static string Dg(string n)
    {
        if (n == "chest" || n == "chest_gold" || n == "wall_doorway" || n == "wall_doorway_scaffold" || n == "floor_tile_big_spikes")
            return KK + "Dungeon/" + n + ".glb";
        return KK + "Dungeon/" + n + ".gltf.glb";
    }
    static string Md(string folder, string n) { return KK + "Medieval/" + folder + "/" + n + ".gltf"; }

    static float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    [MenuItem("Tools/Village/Rebuild Level")]
    public static void Build()
    {
        rng = new System.Random(4242);
        startPeaks.Clear(); peakMeshes = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        env = new GameObject("Environment").transform;
        props = new GameObject("Props").transform;
        chars = new GameObject("Characters").transform;
        lights = new GameObject("Lighting").transform;

        BuildLighting();
        BuildGround();
        BuildRoute();
        BuildStartArea();
        GO = Vector3.zero; BuildGraveyard("Graveyard");
        GO = new Vector3(4f, 0f, 58f); BuildGraveyard("Graveyard 2");
        GO = Vector3.zero;
        BuildHouse();
        BuildExtraHouses();
        BuildBigHouse();
        BuildYard();
        BuildExit();
        BuildForest();
        BuildClouds();
        BuildMountains();
        BuildGrass();
        BuildLevel2();
        BuildStage3();
        BuildSideLoop();
        BuildCharacters();
        BuildCamera();
        BuildGameplay();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Village level built: " + ScenePath);
    }

    // ---------- helpers ----------

    static Material Mat(string name, Color color, float smoothness = 0.1f, float metallic = 0f, Color? emission = null)
    {
        string path = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Place(string path, Vector3 pos, float rotY = 0f, float scale = 1f, Transform parent = null, bool collider = true, bool recenter = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogWarning("Missing model: " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        GameObject result = go;
        if (recenter)
        {
            var wrap = new GameObject(prefab.name);
            go.transform.SetParent(wrap.transform, false);
            go.transform.localScale = Vector3.one * scale;
            var b = BoundsOf(go);
            go.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            wrap.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            result = wrap;
        }
        else
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
            go.transform.localScale = Vector3.one * scale;
        }
        if (parent != null) result.transform.SetParent(parent, true);
        if (collider)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        return result;
    }

    // Saves a generated mesh. An existing asset is updated in place (never deleted while the Editor may be drawing it).
    static Mesh SaveMesh(Mesh m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
        string name = m.name;
        EditorUtility.CopySerialized(m, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(m);
        return existing;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat, Transform parent, bool keepCollider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Light PointLight(string name, Vector3 pos, Color color, float intensity, float range, bool shadows = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(lights, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        return l;
    }

    static Transform FindDeep(Transform t, string name)
    {
        foreach (var x in t.GetComponentsInChildren<Transform>(true)) if (x.name == name) return x;
        return null;
    }

    static void Pose(GameObject character, string modelPath, string clipName, float time)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            var clip = a as AnimationClip;
            if (clip != null && clip.name == clipName) { clip.SampleAnimation(character, time); return; }
        }
        Debug.LogWarning("Clip not found: " + clipName + " in " + modelPath);
    }

    // ---------- lighting, sky, fog, post ----------

    static void BuildLighting()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "DuskSky.mat");
        if (sky == null)
        {
            sky = new Material(Shader.Find("Game/DuskSkybox"));
            AssetDatabase.CreateAsset(sky, MatDir + "DuskSky.mat");
        }
        sky.SetVector("_MoonDir", MoonDir);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;

        var moon = new GameObject("Moon Light");
        moon.transform.SetParent(lights, false);
        moon.transform.rotation = Quaternion.LookRotation(-MoonDir);
        var l = moon.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(0.64f, 0.74f, 1f);
        l.intensity = 1.25f;
        l.shadows = LightShadows.Soft;
        l.shadowStrength = 0.85f;
        RenderSettings.sun = l;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.24f, 0.28f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.22f, 0.27f);
        RenderSettings.ambientGroundColor = new Color(0.09f, 0.08f, 0.08f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.20f, 0.20f, 0.30f);
        RenderSettings.fogDensity = 0.013f;

        string profPath = "Assets/Game/Settings/VillageVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profPath);

            var bloom = profile.Add<Bloom>(false);
            bloom.intensity.Override(0.55f); bloom.threshold.Override(0.85f); bloom.scatter.Override(0.7f);
            var ca = profile.Add<ColorAdjustments>(false);
            ca.postExposure.Override(0.35f); ca.contrast.Override(16f); ca.saturation.Override(-6f);
            var vig = profile.Add<Vignette>(false);
            vig.intensity.Override(0.36f); vig.smoothness.Override(0.5f);
            var tm = profile.Add<Tonemapping>(false);
            tm.mode.Override(TonemappingMode.Neutral);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
        }
        var vol = new GameObject("Global Volume").AddComponent<Volume>();
        vol.transform.SetParent(lights, false);
        vol.isGlobal = true;
        vol.sharedProfile = profile;
    }

    // ---------- ground and route ----------

    static void BuildGround()
    {
        var grass = Mat("Ground_Grass", new Color(0.17f, 0.23f, 0.15f), 0.05f);
        var g = Prim(PrimitiveType.Plane, "Ground", Vector3.zero, new Vector3(70, 1, 70), Vector3.zero, grass, env);
        g.isStatic = true;
    }

    static void Dirt(float x, float z)
    {
        Place(Hw("floor_dirt"), new Vector3(x, 0.02f, z), 90 * rng.Next(4), 1f, env, false);
    }

    static void BuildRoute()
    {
        // main route from the start arch up to the yard
        for (float z = -44; z <= 8; z += 4) Dirt(0, z);
        string[] stones = { "path_A", "path_B", "path_C", "path_D" };
        for (float z = -42; z <= 8; z += 3.2f)
            Place(Hw(stones[rng.Next(4)]), new Vector3(Rand(-1.1f, 1.1f), 0.06f, z), Rand(0, 360), 1f, env, false);

        Place(Hw("arch"), new Vector3(0, 0, -44), 0, 1.15f, props);
        Place(Hw("fence"), new Vector3(-4.4f, 0, -44), 0, 1f, props);
        Place(Hw("fence_broken"), new Vector3(4.4f, 0, -44), 0, 1f, props);

        // lantern posts with warm light
        float[] lz = { -36, -20, -4 };
        for (int i = 0; i < lz.Length; i++)
        {
            float side = (i % 2 == 0) ? 2.9f : -2.9f;
            Place(Hw("post_lantern"), new Vector3(side, 0, lz[i]), side > 0 ? -90 : 90, 1f, props);
            PointLight("Lantern Light", new Vector3(side * 0.75f, 2.7f, lz[i]), new Color(1f, 0.62f, 0.28f), 14f, 11f);
        }
        Place(Hw("post_skull"), new Vector3(-2.8f, 0, -28), 60, 1f, props);
        Place(Hw("post_skull"), new Vector3(2.9f, 0, -11), -70, 1f, props);

        // wooden fence along the right side of the route
        for (float z = -40; z <= -2; z += 4.6f)
        {
            if (Mathf.Abs(z + 21.6f) < 0.5f) continue; // a gap
            if (z > -18.5f && z < -6f) continue;         // open in front of the big house
            float fenceTurn = Rand(-4, 4);
            if (z < -33f || z > -5f) continue;           // open at both ends of the side loop around the two houses
            Place(Md("neutral", "fence_wood_straight"), new Vector3(6.2f, 0, z), fenceTurn, 4f, props, true, true);
        }

        // clutter on the route
        Place(Hw("bone_A"), new Vector3(1.4f, 0.08f, -24), 40, 1f, props, false);
        Place(Hw("bone_B"), new Vector3(-1.2f, 0.08f, -15), 120, 1f, props, false);
        Place(Hw("skull"), new Vector3(1.9f, 0.05f, -16), 200, 1f, props, false);
        Place(Hw("ribcage"), new Vector3(-2.2f, 0.02f, -33), 30, 1f, props, false);
        Place(Dg("barrel_small"), new Vector3(7.3f, 0, -41.3f), 0, 1f, props);
        Place(Dg("crates_stacked"), new Vector3(4.2f, 0, -13), 20, 1f, props);
        Place(Md("props", "wheelbarrow"), new Vector3(-3.6f, 0, -6), 150, 5f, props, true, true);
    }

    // ---------- graveyard ----------

    static Vector3 GO;

    static void BuildGraveyard(string gname)
    {
        var gy = new GameObject(gname).transform; gy.SetParent(env);
        for (float x = -24; x <= -8; x += 4)
        {
            Place(Hw(x == -16 ? "fence_broken" : "fence"), (new Vector3(x, 0, -22) + GO), 0, 1f, gy);
            Place(Hw("fence"), (new Vector3(x, 0, -2) + GO), 0, 1f, gy);
        }
        for (float z = -20; z <= -4; z += 4)
        {
            Place(Hw("fence"), (new Vector3(-26, 0, z) + GO), 90, 1f, gy);
            var seg = Place(Hw(z == -12 ? "fence_gate" : "fence"), (new Vector3(-6, 0, z) + GO), 90, 1f, gy);
            if (z == -12) RemoveLeaves(seg, "fence_gate"); // the graveyard gate stands open
        }
        Place(Hw("fence_pillar"), (new Vector3(-26, 0, -22) + GO), 0, 1f, gy);
        Place(Hw("fence_pillar"), (new Vector3(-6, 0, -22) + GO), 0, 1f, gy);
        Place(Hw("fence_pillar"), (new Vector3(-26, 0, -2) + GO), 0, 1f, gy);
        Place(Hw("fence_pillar_broken"), (new Vector3(-6, 0, -2) + GO), 0, 1f, gy);

        // short dirt path from the route into the gate
        Dirt(-4 + GO.x, -12 + GO.z); Dirt(-8 + GO.x, -12 + GO.z); Dirt(-12 + GO.x, -12 + GO.z);

        Place(Hw("crypt"), (new Vector3(-21, 0, -12) + GO), 90, 1f, gy);
        PointLight("Crypt Light", (new Vector3(-16.5f, 2.2f, -12) + GO), new Color(0.45f, 0.9f, 0.7f), 9f, 9f);

        string[] graves = { "grave_A", "grave_B", "gravestone", "gravemarker_A", "gravemarker_B", "grave_A_destroyed" };
        float[] gz = { -19.5f, -16.5f, -7.5f, -4.5f };
        float[] gx = { -15f, -10.5f };
        int k = 0;
        foreach (float x in gx)
            foreach (float z in gz)
            {
                Place(Hw(graves[k % graves.Length]), (new Vector3(x, 0, z) + GO), 90 + Rand(-8, 8), 1f, gy);
                k++;
            }
        Place(Hw("coffin"), (new Vector3(-12.5f, 0, -13.5f) + GO), 25, 1f, gy);
        Place(Hw("shrine_candles"), (new Vector3(-23.5f, 0, -4.5f) + GO), 135, 1f, gy);
        Place(Hw("tree_dead_large"), (new Vector3(-8.5f, 0, -19) + GO), 40, 1.2f, gy);
        Place(Hw("tree_dead_medium"), (new Vector3(-23.5f, 0, -19.5f) + GO), 200, 1.2f, gy);
        Place(Hw("lantern_standing"), (new Vector3(-7.2f, 0, -9.6f) + GO), 0, 1f, gy);
        PointLight("Gate Lantern", (new Vector3(-7.2f, 1.1f, -9.6f) + GO), new Color(1f, 0.62f, 0.28f), 6f, 7f);
        Place(Hw("skull_candle"), (new Vector3(-14f, 0, -11f) + GO), 80, 1f, gy, false);
        Place(Hw("pumpkin_orange_jackolantern"), (new Vector3(-7.5f, 0, -14.6f) + GO), -60, 1f, gy);
    }

    // ---------- house ----------

    static Mesh RoofMesh(float w, float d, float h)
    {
        string path = "Assets/Game/Meshes/HouseRoof.asset";
        float x = w / 2, z = d / 2;
        // gable roof: ridge runs along X
        Vector3 a = new Vector3(-x, 0, -z), b = new Vector3(x, 0, -z), c = new Vector3(x, 0, z), e = new Vector3(-x, 0, z);
        Vector3 r0 = new Vector3(-x, h, 0), r1 = new Vector3(x, h, 0);
        var v = new List<Vector3>(); var t = new List<int>();
        System.Action<Vector3, Vector3, Vector3, Vector3> quad = (p0, p1, p2, p3) =>
        {
            int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        };
        System.Action<Vector3, Vector3, Vector3> tri = (p0, p1, p2) =>
        {
            int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); t.Add(i); t.Add(i + 1); t.Add(i + 2);
        };
        quad(a, r0, r1, b);   // south slope
        quad(c, r1, r0, e);   // north slope
        tri(e, r0, a);        // west gable
        tri(b, r1, c);        // east gable
        quad(a, b, c, e);     // underside (ceiling)
        var m = new Mesh { name = "HouseRoof" };
        m.SetVertices(v); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return SaveMesh(m, path);
    }

    static void BuildHouse()
    {
        var house = new GameObject("House").transform; house.SetParent(env);
        // footprint: x -16..-4, z 20..28. Door in the middle of the south wall.
        Place(Dg("wall_window_open"), new Vector3(-14, 0, 20), 0, 1f, house);
        var doorway = Place(Dg("wall_doorway"), new Vector3(-10, 0, 20), 0, 1f, house);
        var shut = doorway != null ? FindDeep(doorway.transform, "wall_doorway_door") : null;
        if (shut != null) shut.gameObject.SetActive(false); // the model's own closed door; ours stands open
        Place(Dg("wall_window_open"), new Vector3(-6, 0, 20), 0, 1f, house);
        Place(Dg("wall"), new Vector3(-14, 0, 28), 180, 1f, house);
        Place(Dg("wall_window_closed"), new Vector3(-10, 0, 28), 180, 1f, house);
        Place(Dg("wall"), new Vector3(-6, 0, 28), 180, 1f, house);
        Place(Dg("wall"), new Vector3(-16, 0, 22), 90, 1f, house);
        Place(Dg("wall_window_open"), new Vector3(-16, 0, 26), 90, 1f, house);
        Place(Dg("wall_window_open"), new Vector3(-4, 0, 22), -90, 1f, house);
        Place(Dg("wall"), new Vector3(-4, 0, 26), -90, 1f, house);

        for (float x = -14; x <= -6; x += 4)
            for (float z = 22; z <= 26; z += 4)
                Place(Dg("floor_wood_large"), new Vector3(x, 0.06f, z), 0, 1f, house);

        var roofMat = Mat("Roof_Shingle", new Color(0.24f, 0.13f, 0.12f), 0.15f);
        var roof = new GameObject("Roof");
        roof.transform.SetParent(house, false);
        roof.transform.position = new Vector3(-10, 3.95f, 24);
        var mesh = RoofMesh(14.4f, 10.6f, 3.6f);
        roof.AddComponent<MeshFilter>().sharedMesh = mesh;
        roof.AddComponent<MeshRenderer>().sharedMaterial = roofMat;
        roof.AddComponent<MeshCollider>().sharedMesh = mesh;

        var stone = Mat("Chimney_Stone", new Color(0.36f, 0.36f, 0.4f), 0.05f);
        Prim(PrimitiveType.Cube, "Chimney", new Vector3(-6.5f, 6.4f, 26f), new Vector3(1.1f, 3.6f, 1.1f), Vector3.zero, stone, house);

        var wood = Mat("Door_Wood", new Color(0.30f, 0.19f, 0.11f), 0.1f);
        Prim(PrimitiveType.Cube, "Door", new Vector3(-11.25f, 1.45f, 19.0f), new Vector3(1.9f, 2.9f, 0.14f), new Vector3(0, 62, 0), wood, house);

        // interior
        Place(Dg("table_medium_decorated_A"), new Vector3(-12.5f, 0.13f, 24.5f), 0, 1f, house);
        Place(Dg("chair"), new Vector3(-12.5f, 0.13f, 23f), 180, 1f, house);
        Place(Dg("stool"), new Vector3(-14.2f, 0.13f, 24.8f), 0, 1f, house);
        Place(Dg("bed_decorated"), new Vector3(-6.2f, 0.13f, 26f), 0, 1f, house);
        Place(Dg("chest"), new Vector3(-14.3f, 0.13f, 26.8f), 180, 1f, house);
        Place(Dg("barrel_small_stack"), new Vector3(-5.4f, 0.13f, 21.6f), 0, 1f, house);
        Place(Dg("shelves"), new Vector3(-10f, 0.13f, 27.2f), 180, 1f, house);
        Place(Dg("candle_triple"), new Vector3(-9f, 0.13f, 22f), 0, 1f, house, false);
        PointLight("House Interior Light", new Vector3(-10.5f, 2.6f, 24f), new Color(1f, 0.66f, 0.33f), 22f, 12f, true);

        // exterior dressing
        Place(Dg("torch_mounted"), new Vector3(-7.9f, 2.2f, 19.45f), 180, 1f, house, false);
        PointLight("Door Torch Light", new Vector3(-7.9f, 2.9f, 18.8f), new Color(1f, 0.55f, 0.2f), 16f, 10f);
        Place(Hw("pumpkin_orange"), new Vector3(-12.6f, 0, 18.6f), 20, 1f, props);
        Place(Hw("pumpkin_yellow_small"), new Vector3(-13.6f, 0, 18.9f), 80, 1f, props);
        Place(Hw("pumpkin_orange_jackolantern"), new Vector3(-7.2f, 0, 18.4f), 170, 1f, props);
        Place(Dg("barrel_large"), new Vector3(-17.4f, 0, 21.2f), 0, 1f, props);
        Place(Dg("keg"), new Vector3(-17.3f, 0, 23.4f), 90, 1f, props);
        Place(Md("props", "resource_lumber"), new Vector3(-2.2f, 0, 25f), 90, 6f, props, true, true);
        Place(Hw("bench"), new Vector3(-3.0f, 0, 21f), -90, 1f, props);
    }

    // ---------- yard ----------

    static void BuildYard()
    {
        for (float x = -14; x <= 10; x += 4)
            for (float z = 12; z <= 16; z += 4)
                Dirt(x, z);
        // square loop path around the village: x = -26..26, z = 8..60
        string[] loopStones = { "path_A", "path_B", "path_C", "path_D" };
        for (float t = -26; t <= 26; t += 4)
        {
            Dirt(t, 8); Dirt(t, 60);
            Place(Hw(loopStones[rng.Next(4)]), new Vector3(t + Rand(-1f, 1f), 0.06f, 8 + Rand(-1f, 1f)), Rand(0, 360), 1f, env, false);
            Place(Hw(loopStones[rng.Next(4)]), new Vector3(t + Rand(-1f, 1f), 0.06f, 60 + Rand(-1f, 1f)), Rand(0, 360), 1f, env, false);
        }
        for (float t = 12; t <= 56; t += 4)
        {
            Dirt(-26, t); Dirt(26, t);
            Place(Hw(loopStones[rng.Next(4)]), new Vector3(-26 + Rand(-1f, 1f), 0.06f, t + Rand(-1f, 1f)), Rand(0, 360), 1f, env, false);
            Place(Hw(loopStones[rng.Next(4)]), new Vector3(26 + Rand(-1f, 1f), 0.06f, t + Rand(-1f, 1f)), Rand(0, 360), 1f, env, false);
        }
        Dirt(4, 46);            // link to the second graveyard gate
        Dirt(-4, -34); Dirt(-8, -34); // link to the small house by the start
        // lanterns on the loop corners and sides
        float[,] lp = { { -28.6f, 8 }, { 28.6f, 8 }, { -28.6f, 60 }, { 28.6f, 60 }, { -28.6f, 34 }, { 28.6f, 34 } };
        for (int i = 0; i < lp.GetLength(0); i++)
        {
            float lx = lp[i, 0], lzz = lp[i, 1];
            Place(Hw("post_lantern"), new Vector3(lx, 0, lzz), lx > 0 ? -90 : 90, 1f, props);
            PointLight("Loop Lantern", new Vector3(lx * 0.96f, 2.7f, lzz), new Color(1f, 0.62f, 0.28f), 14f, 11f);
        }
        Dirt(-10, 18.2f);
        // path on to the exit
        float[,] pts = { { 2, 20 }, { 3, 24 }, { 4, 28 }, { 5, 32 }, { 6, 36 }, { 6, 40 }, { 6, 44 }, { 6, 48 }, { 5, 52 }, { 3, 56 } };
        for (int i = 0; i < pts.GetLength(0); i++) Dirt(pts[i, 0], pts[i, 1]);

        Place(Md("red", "building_well_red"), new Vector3(3.5f, 0, 14f), 30, 5f, props, true, true);
        Place(Md("red", "building_home_B_red"), new Vector3(16f, 0, 24f), -90, 8f, env, true, true);
        Place(Md("red", "building_home_A_red"), new Vector3(17f, 0, -31f), -100, 8f, env, true, true);
        Place(Md("props", "tent"), new Vector3(11f, 0, 15f), 200, 7f, props, true, true);
        Place(Md("props", "weaponrack"), new Vector3(8.2f, 0, 10.5f), 40, 6f, props, true, true);
        Place(Md("props", "pallet"), new Vector3(9.5f, 0, 19f), 10, 6f, props, true, true);
        Place(Md("props", "sack"), new Vector3(8.6f, 0, 20.5f), 0, 6f, props, true, true);
        Place(Dg("box_stacked"), new Vector3(-1.5f, 0, 10f), 15, 1f, props);
        Place(Dg("box_large"), new Vector3(-3.4f, 0, 9.4f), -20, 1f, props);
        Place(Dg("barrel_large_decorated"), new Vector3(6.5f, 0, 16.5f), 0, 1f, props);
        Place(Dg("trunk_medium_A"), new Vector3(1.2f, 0, 17.6f), 70, 1f, props);
        Place(Hw("lantern_standing"), new Vector3(1.6f, 0, 12.2f), 0, 1f, props);
        PointLight("Yard Lantern", new Vector3(1.6f, 1.2f, 12.2f), new Color(1f, 0.62f, 0.28f), 9f, 9f);
        Place(Hw("post_lantern"), new Vector3(8.6f, 0, 30f), -90, 1f, props);
        PointLight("Exit Path Lantern", new Vector3(7.6f, 2.7f, 30f), new Color(1f, 0.62f, 0.28f), 14f, 11f);

        // wooden fence on the west and east edges of the yard
        for (float z = 4; z <= 64; z += 4.6f)
        {
            Place(Md("neutral", "fence_wood_straight"), new Vector3(-32f, 0, z), Rand(-4, 4), 4f, props, true, true);
            Place(Md("neutral", "fence_wood_straight"), new Vector3(32f, 0, z), Rand(-4, 4), 4f, props, true, true);
        }
    }

    // ---------- exit ----------

    static void BuildExit()
    {
        Dirt(0, 64);
        exitGateGo = Place(Hw("arch_gate"), new Vector3(0, 0, 66), 0, 1.15f, props);
        for (float x = -30f; x <= 31; x += 4)
        {
            if (Mathf.Abs(x) < 3.9f) continue;
            Place(Hw("fence"), new Vector3(x, 0, 66), 0, 1f, props);
        }
        Place(Hw("fence_pillar"), new Vector3(-2.7f, 0, 66), 0, 1f, props);
        Place(Hw("fence_pillar"), new Vector3(2.7f, 0, 66), 0, 1f, props);
        PointLight("Exit Glow", new Vector3(0, 3, 67.5f), new Color(0.5f, 0.75f, 1f), 12f, 12f);
    }

    // ---------- forest and backdrop ----------

    static bool InPlayArea(float x, float z, float margin)
    {
        // route corridor + first graveyard + start-side house + right-side home + village square
        if (x > -8 - margin && x < 8 + margin && z > -47 - margin && z < 6) return true;
        if (x > -27 - margin && x < -5 && z > -23 - margin && z < -1 + margin) return true;
        if (x > -20 - margin && x < -4 && z > -42 - margin && z < -26 + margin) return true;
        if (x > 8 && x < 26 + margin && z > -37 - margin && z < 6) return true;
        // start clearing, straight start path and the half-circle path
        float cx = x - PlayerStart.x, cz = z - (PlayerStart.z + 2);
        if (cx * cx + cz * cz < (10 + margin) * (10 + margin)) return true;
        if (Mathf.Abs(x - PlayerStart.x) < 5 + margin && z > -50 && z < -22) return true;
        float ax = x - ArcCentre.x, az = z - ArcCentre.z;
        if (z < -44 && Mathf.Abs(Mathf.Sqrt(ax * ax + az * az) - ArcRadius) < 5 + margin) return true;
        if (x > -33 - margin && x < 33 + margin && z > 2 - margin && z < 67 + margin) return true;
        return false;
    }

    // true where grass must not grow: paths, floors and building footprints
    static bool Blocked(float x, float z)
    {
        if (Mathf.Abs(x) < 2.4f && z > -50 && z < 10) return true;
        if (Mathf.Abs(x - PlayerStart.x) < 2.4f && z > -50 && z < -21) return true;
        float bx = x - ArcCentre.x, bz = z - ArcCentre.z;
        if (z < -46 && Mathf.Abs(Mathf.Sqrt(bx * bx + bz * bz) - ArcRadius) < 2.6f) return true;
        if (x > 8 && x < 26 && z > -21 && z < -3) return true;   // big house
        if (x > 12 && x < 22 && z > -37 && z < -25) return true; // right-side home
        if (z > 5.6f && z < 10.4f && Mathf.Abs(x) < 28.4f) return true;
        if (z > 57.6f && z < 62.4f && Mathf.Abs(x) < 28.4f) return true;
        if (Mathf.Abs(Mathf.Abs(x) - 26) < 2.4f && z > 5.6f && z < 62.4f) return true;
        if (x > -16.4f && x < 12.4f && z > 9.6f && z < 18.4f) return true;
        if (x > -17 && x < -3 && z > 19 && z < 29) return true;
        if (x > 8.4f && x < 19.6f && z > 42 && z < 50) return true;
        if (x > -17 && x < -5.6f && z > -39.6f && z < -28.4f) return true;
        if (Mathf.Abs(x - 4.5f) < 2.8f && z > 18 && z < 58) return true;
        if (Mathf.Abs(x) < 2.4f && z > 60 && z < 67) return true;
        if (x > 10 && x < 22 && z > 18 && z < 30) return true;
        if (x > -14.4f && x < 2 && z > 43.6f && z < 48.4f) return true;
        if (x > -14.4f && x < -2 && z > -14.4f && z < -9.6f) return true;
        return false;
    }

    static bool InNearMountain(float x, float z)
    {
        for (int i = 0; i < NearMountains.GetLength(0); i++)
        {
            float dx = x - NearMountains[i, 0], dz = z - NearMountains[i, 1], r = NearMountains[i, 2] * 0.78f;
            if (dx * dx + dz * dz < r * r) return true;
        }
        foreach (var p in startPeaks)
        {
            float dx = x - p.x, dz = z - p.z;
            if (dx * dx + dz * dz < p.y * p.y * 0.8f) return true;
        }
        return false;
    }

    static void BuildForest()
    {
        var forest = new GameObject("Forest").transform; forest.SetParent(env);
        string[] trees = { "tree_pine_orange_large", "tree_pine_yellow_large", "tree_pine_orange_medium", "tree_pine_yellow_medium",
                           "tree_pine_orange_large", "tree_dead_large", "tree_pine_yellow_small", "tree_dead_medium" };
        int placed = 0, tries = 0;
        while (placed < 470 && tries < 5000)
        {
            tries++;
            float x = Rand(-100, 100), z = Rand(-90, 130);
            if (InPlayArea(x, z, 2.5f) || InNearMountain(x, z)) continue;
            // denser close to the play area, sparser far away
            float dist = Mathf.Max(Mathf.Abs(x) - 34, Mathf.Abs(z - 10) - 58);
            if (dist > 12 && rng.NextDouble() < 0.55) continue;
            string tname = trees[rng.Next(trees.Length)];
            float treeRot = Rand(0, 360), treeScale = Rand(1.1f, 1.9f);
            // level 2 is kept clear, but the tree is still "drawn" so everything after it lands where it always did
            if (!InLevel2(x, z, 2.5f) && !InStage3(x, z, 2.5f) && !InSideLoop(x, z)) Place(Hw(tname), new Vector3(x, 0, z), treeRot, treeScale, forest, dist < 6);
            placed++;
        }
        // a few trees and rocks inside the level for cover
        Place(Hw("tree_pine_orange_medium"), new Vector3(10.5f, 0, -45f), 0, 1.3f, forest);
        Place(Hw("tree_dead_small"), new Vector3(-4.6f, 0, -23.5f), 90, 1.4f, forest);
        Place(Hw("tree_pine_yellow_large"), new Vector3(18f, 0, 36f), 40, 1.4f, forest);
        Place(Hw("tree_pine_orange_large"), new Vector3(-29.5f, 0, 44f), 200, 1.5f, forest);
        Place(Hw("tree_dead_large"), new Vector3(12f, 0, 40f), 10, 1.5f, forest);
        Place(Hw("tree_pine_orange_medium"), new Vector3(-19f, 0, 10f), 10, 1.4f, forest);

        string[] rocks = { "rock_single_A", "rock_single_B", "rock_single_C", "rock_single_D", "rock_single_E" };
        float[,] rp = { { -4.5f, -20 }, { 4.8f, -6 }, { -18, 7 }, { 19, 12 }, { -2, 30 }, { 12, 34 }, { -17, 33 }, { 6.4f, -42.6f }, { -5.5f, 2 }, { 23, 38 }, { -22, -30 }, { -20, -25 }, { 22, 54 }, { -29, 20 }, { 29, 48 } };
        for (int i = 0; i < rp.GetLength(0); i++)
            Place(Md("nature", rocks[i % rocks.Length]), new Vector3(rp[i, 0], 0, rp[i, 1]), Rand(0, 360), Rand(7f, 12f), forest, true, true);

    }

    // ---------- extra houses ----------

    static void CloneHouse(string name, Vector3 center, float rotY, float scale)
    {
        var src = env.Find("House");
        if (src == null) return;
        var pivot = new GameObject(name).transform;
        pivot.SetParent(env, false);
        pivot.position = new Vector3(-10, 0, 24); // centre of the original house
        var copy = Object.Instantiate(src.gameObject);
        copy.name = "Building";
        copy.transform.SetParent(pivot, true);
        pivot.SetPositionAndRotation(center, Quaternion.Euler(0, rotY, 0));
        pivot.localScale = Vector3.one * scale;
        PointLight(name + " Interior Light", pivot.TransformPoint(new Vector3(-0.5f, 2.6f, 0f)), new Color(1f, 0.66f, 0.33f), 16f, 10f);
        PointLight(name + " Door Torch", pivot.TransformPoint(new Vector3(2.1f, 2.9f, -5.2f)), new Color(1f, 0.55f, 0.2f), 14f, 9f);
    }

    static void BuildExtraHouses()
    {
        // small house on the left of the start route, door facing the route
        CloneHouse("Small House Start", new Vector3(-13f, 0, -34f), -90, 1f);
        Place(Hw("pumpkin_orange_jackolantern"), new Vector3(-8.2f, 0, -31.2f), 100, 1f, props);
        Place(Hw("pumpkin_yellow"), new Vector3(-8.4f, 0, -37.2f), 30, 1f, props);
        Place(Dg("barrel_large"), new Vector3(-10.5f, 0, -27.6f), 0, 1f, props);
        Place(Dg("box_small_decorated"), new Vector3(-12.2f, 0, -27.8f), 25, 1f, props);
        Place(Hw("bench_decorated"), new Vector3(-7.6f, 0, -39.5f), 180, 1f, props);
        Place(Hw("tree_dead_large_decorated"), new Vector3(-6.2f, 0, -26.5f), 120, 1.25f, props);
        Place(Md("props", "crate_long_A"), new Vector3(-18.6f, 0, -31f), 80, 6f, props, true, true);
        Place(Md("props", "bucket_water"), new Vector3(-8.0f, 0, -35.6f), 0, 6f, props, true, true);
        for (float z = -40; z <= -27; z += 4.6f)
            Place(Md("neutral", "fence_wood_straight"), new Vector3(-21.5f, 0, z), Rand(-4, 4), 4f, props, true, true);
        Place(Hw("lantern_standing"), new Vector3(-5.2f, 0, -36.2f), 0, 1f, props);
        PointLight("Start House Lantern", new Vector3(-5.2f, 1.1f, -36.2f), new Color(1f, 0.62f, 0.28f), 7f, 8f);

        // small house in the north-east of the square, door facing the north path
        CloneHouse("Small House North", new Vector3(14f, 0, 46f), 180, 1f);
        Place(Hw("pumpkin_orange"), new Vector3(11.4f, 0, 51.2f), 60, 1f, props);
        Place(Dg("keg_decorated"), new Vector3(20.6f, 0, 44.5f), 90, 1f, props);
        Place(Md("props", "crate_A_big"), new Vector3(20.4f, 0, 48.4f), 15, 6f, props, true, true);
        Place(Md("props", "ladder"), new Vector3(7.6f, 0, 44.2f), 90, 6f, props, true, true);
    }

    // ---------- start area: clearing, straight path, half-circle left to the old start ----------

    static void BuildStartArea()
    {
        var start = new GameObject("Start Area").transform; start.SetParent(env);
        string[] stones = { "path_A", "path_B", "path_C", "path_D" };
        float px = PlayerStart.x;

        // straight path south from the clearing
        for (float z = -24; z >= -48; z -= 4)
        {
            Place(Hw("floor_dirt"), new Vector3(px, 0.02f, z), 90 * rng.Next(4), 1f, start, false);
            Place(Hw(stones[rng.Next(4)]), new Vector3(px + Rand(-1f, 1f), 0.06f, z + Rand(-1f, 1f)), Rand(0, 360), 1f, start, false);
        }
        // half circle turning left (east), ending just south of the entry arch
        for (float a = 186; a <= 354; a += 10.5f)
        {
            float r = a * Mathf.Deg2Rad;
            var pos = ArcCentre + new Vector3(Mathf.Cos(r), 0, Mathf.Sin(r)) * ArcRadius;
            Place(Hw("floor_dirt"), new Vector3(pos.x, 0.02f + (a % 21 < 10 ? 0.004f : 0f), pos.z), -a, 1f, start, false);
            Place(Hw(stones[rng.Next(4)]), new Vector3(pos.x + Rand(-1f, 1f), 0.07f, pos.z + Rand(-1f, 1f)), Rand(0, 360), 1f, start, false);
        }
        Place(Hw("floor_dirt"), new Vector3(0, 0.02f, -48), 0, 1f, start, false);

        // lanterns along the way
        float[] la = { 180, 225, 270, 315 };
        foreach (float a in la)
        {
            float r = a * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(r), 0, Mathf.Sin(r));
            var pos = ArcCentre + dir * (ArcRadius + 3f);
            Place(Hw("post_lantern"), pos, 90 - a + 180, 1f, props);
            PointLight("Path Lantern", ArcCentre + dir * (ArcRadius + 2.2f) + Vector3.up * 2.7f, new Color(1f, 0.62f, 0.28f), 14f, 11f);
        }
        Place(Hw("post_lantern"), new Vector3(px - 2.9f, 0, -34), 90, 1f, props);
        PointLight("Path Lantern", new Vector3(px - 2.2f, 2.7f, -34), new Color(1f, 0.62f, 0.28f), 14f, 11f);

        // the clearing itself: a small camp
        Place(Md("props", "tent"), new Vector3(px - 5.5f, 0, -18.5f), 130, 7f, props, true, true);
        Place(Md("props", "crate_A_small"), new Vector3(px + 4.6f, 0, -19.5f), 20, 6f, props, true, true);
        Place(Md("props", "sack"), new Vector3(px + 5.6f, 0, -21.4f), 0, 6f, props, true, true);
        Place(Md("props", "resource_lumber"), new Vector3(px + 3.2f, 0, -16.2f), 40, 6f, props, true, true);
        Place(Dg("barrel_small"), new Vector3(px - 3.4f, 0, -23.2f), 0, 1f, props);
        Place(Hw("lantern_standing"), new Vector3(px + 2.6f, 0, -22.6f), 0, 1f, props);
        PointLight("Camp Lantern", new Vector3(px + 2.6f, 1.2f, -22.6f), new Color(1f, 0.62f, 0.28f), 10f, 10f);
        Place(Hw("post_skull"), new Vector3(px + 3.0f, 0, -46), -60, 1f, props);
        Place(Hw("bone_C"), new Vector3(px + 0.8f, 0.08f, -41), 70, 1f, props, false);

        BuildStartPeaks(start);
        BuildStartBarriers(start);

        // a few trees and rocks in the gaps between the path and the mountain feet
        string[] trees = { "tree_pine_orange_large", "tree_pine_yellow_large", "tree_pine_orange_medium", "tree_pine_yellow_medium", "tree_dead_large" };
        string[] rocks = { "rock_single_A", "rock_single_B", "rock_single_C", "rock_single_D", "rock_single_E" };
        int placed = 0, tries = 0;
        while (placed < 45 && tries < 6000)
        {
            tries++;
            float x = Rand(-76, 10), z = Rand(-88, -4);
            float de = StartEdgeDist(x, z);
            if (de < 1.2f || de > 7f || ProtectedDist(x, z) < 2f || InNearMountain(x, z)) continue;
            if (rng.NextDouble() < 0.25)
                Place(Md("nature", rocks[rng.Next(rocks.Length)]), new Vector3(x, 0, z), Rand(0, 360), Rand(6f, 10f), start, true, true);
            else
                Place(Hw(trees[rng.Next(trees.Length)]), new Vector3(x, 0, z), Rand(0, 360), Rand(1.1f, 1.7f), start, true);
            placed++;
        }
    }

    // distance from a point to the edge of the walkable start path (clearing + straight + half circle + gate stub)
    static float StartEdgeDist(float x, float z)
    {
        float px = PlayerStart.x;
        float d = Mathf.Sqrt((x - px) * (x - px) + (z + 22) * (z + 22)) - 10.5f;
        float cz = Mathf.Clamp(z, -48, -24);
        d = Mathf.Min(d, Mathf.Sqrt((x - px) * (x - px) + (z - cz) * (z - cz)) - PathHalfWidth);
        float ax = x - ArcCentre.x, az = z - ArcCentre.z;
        if (az <= 0) d = Mathf.Min(d, Mathf.Abs(Mathf.Sqrt(ax * ax + az * az) - ArcRadius) - PathHalfWidth);
        float gz = Mathf.Clamp(z, -48, -44);
        d = Mathf.Min(d, Mathf.Sqrt(x * x + (z - gz) * (z - gz)) - PathHalfWidth);
        return d;
    }

    static float RectDist(float x, float z, float x0, float x1, float z0, float z1)
    {
        float dx = Mathf.Max(x0 - x, 0, x - x1), dz = Mathf.Max(z0 - z, 0, z - z1);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // distance to the parts of the level the start mountains must not cover
    static float ProtectedDist(float x, float z)
    {
        float d = RectDist(x, z, -27.5f, -4.5f, -23.5f, 0f);          // first graveyard
        d = Mathf.Min(d, RectDist(x, z, -23f, -4f, -42f, -25f));     // small house by the gate
        d = Mathf.Min(d, RectDist(x, z, -7.5f, 80f, -45.5f, 90f));   // everything past the main gate
        d = Mathf.Min(d, RectDist(x, z, -34f, 80f, 2f, 90f));        // village square
        return d;
    }

    // mountains walling in both sides of the start path, all the way to the main gate
    static void BuildStartPeaks(Transform parent)
    {
        EnsurePeaks();
        var mts = new GameObject("Start Mountains").transform; mts.SetParent(parent);
        int tries = 0;
        while (startPeaks.Count < 95 && tries < 20000)
        {
            tries++;
            float x = Rand(-84, 20), z = Rand(-106, 6);
            float de = StartEdgeDist(x, z);
            if (de < 4.5f) continue;
            float r = Mathf.Min(11.5f, (de - 0.6f) / 1.2f, (ProtectedDist(x, z) - 0.3f) / 1.2f);
            if (r < 3.4f || de - 0.8f * r > 5f) continue;
            bool crowded = false;
            foreach (var p in startPeaks)
                if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < 0.55f * Mathf.Max(r, p.y) * Mathf.Max(r, p.y)) { crowded = true; break; }
            if (crowded) continue;
            startPeaks.Add(new Vector3(x, r, z));
            Peak(peakMeshes[rng.Next(peakMeshes.Length)], peakMats, mts, new Vector3(x, -0.3f, z), r, r * Rand(1.7f, 2.4f), true);
        }
        // one large peak fills the middle of the half circle
        startPeaks.Add(new Vector3(ArcCentre.x, 12f, ArcCentre.z - 3f));
        Peak(peakMeshes[0], peakMats, mts, new Vector3(ArcCentre.x, -0.3f, ArcCentre.z - 3f), 12f, 30f, true);
    }

    static void Wall(Transform parent, Vector3 a, Vector3 b)
    {
        var go = new GameObject("Barrier");
        go.transform.SetParent(parent, false);
        go.transform.position = (a + b) * 0.5f + Vector3.up * 4f;
        go.transform.rotation = Quaternion.LookRotation(b - a);
        go.AddComponent<BoxCollider>().size = new Vector3(0.6f, 8f, (b - a).magnitude + 0.6f);
        go.layer = 2; // Ignore Raycast: blocks walking, not bullets or the camera
    }

    // invisible walls so the player cannot leave the start path before the main gate
    static void BuildStartBarriers(Transform parent)
    {
        var walls = new GameObject("Start Barriers").transform; walls.SetParent(parent);
        float px = PlayerStart.x, w = PathHalfWidth;
        var cc = new Vector3(px, 0, -22);
        System.Func<Vector3, float, float, Vector3> onCircle = (c, rad, deg) =>
            c + new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0, Mathf.Sin(deg * Mathf.Deg2Rad)) * rad;
        float open = Mathf.Asin(w / 10.5f) * Mathf.Rad2Deg;
        float a0 = 270 + open, a1 = 630 - open;
        for (float a = a0; a < a1 - 0.01f; a += (a1 - a0) / 20f)
            Wall(walls, onCircle(cc, 10.5f, a), onCircle(cc, 10.5f, a + (a1 - a0) / 20f));
        float zTop = -22 - Mathf.Sqrt(10.5f * 10.5f - w * w);
        Wall(walls, new Vector3(px - w, 0, zTop), new Vector3(px - w, 0, -48));
        Wall(walls, new Vector3(px + w, 0, zTop), new Vector3(px + w, 0, -48));
        for (float a = 180; a < 359.9f; a += 9f)
        {
            Wall(walls, onCircle(ArcCentre, ArcRadius - w, a), onCircle(ArcCentre, ArcRadius - w, a + 9f));
            Wall(walls, onCircle(ArcCentre, ArcRadius + w, a), onCircle(ArcCentre, ArcRadius + w, a + 9f));
        }
        Wall(walls, new Vector3(-w, 0, -48), new Vector3(-w, 0, -44));
        Wall(walls, new Vector3(w, 0, -48), new Vector3(w, 0, -44));
    }

    // ---------- big house facing the first graveyard ----------

    static void BuildBigHouse()
    {
        var bigHouse = Place(Md("red", "building_home_A_red"), new Vector3(17f, 0, -12f), -90, 15f, env, true, true);
        bigHouse.name = "Big House";
        // a second copy joined to its back: one long roof for the gunman to walk on, out of sight of the route
        float houseLen = BoundsOf(bigHouse).size.x - 0.3f;
        Place(Md("red", "building_home_A_red"), new Vector3(17f + houseLen, 0, -12f), -90, 15f, env, true, true).name = "Big House Back";
        Dirt(4, -12);
        Place(Hw("lantern_standing"), new Vector3(7.4f, 0, -17.6f), 0, 1f, props);
        PointLight("Big House Lantern", new Vector3(7.4f, 1.2f, -17.6f), new Color(1f, 0.62f, 0.28f), 9f, 9f);
        PointLight("Big House Glow", new Vector3(8.0f, 5f, -12f), new Color(1f, 0.6f, 0.3f), 40f, 20f);
        Place(Dg("barrel_large_decorated"), new Vector3(7.6f, 0, -6.4f), 0, 1f, props);
        Place(Md("props", "crate_B_big"), new Vector3(7.8f, 0, -4.2f), 30, 6f, props, true, true);
        Place(Hw("bench"), new Vector3(7.2f, 0, -9.2f), -90, 1f, props);
    }

    // ---------- mountains ----------

    // Low-poly peak: unit radius, unit height. Submesh 0 = rock, submesh 1 = snow cap.
    static Mesh MountainMesh(int index)
    {
        const int seg = 8;
        float[] ringH = { 0f, 0.42f, 0.74f };
        float[] ringR = { 1f, 0.62f, 0.30f };
        var ring = new Vector3[3, seg];
        for (int k = 0; k < 3; k++)
            for (int i = 0; i < seg; i++)
            {
                float ang = (i + (k % 2) * 0.5f) / seg * Mathf.PI * 2f;
                float rad = ringR[k] * Rand(0.8f, 1.2f);
                float h = k == 0 ? 0f : ringH[k] * Rand(0.88f, 1.12f);
                ring[k, i] = new Vector3(Mathf.Cos(ang) * rad, h, Mathf.Sin(ang) * rad);
            }
        var apex = new Vector3(Rand(-0.08f, 0.08f), 1f, Rand(-0.08f, 0.08f));
        var v = new List<Vector3>(); var rock = new List<int>(); var snow = new List<int>();
        System.Action<List<int>, Vector3, Vector3, Vector3> tri = (list, p0, p1, p2) =>
        {
            int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); list.Add(i); list.Add(i + 1); list.Add(i + 2);
        };
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                var list = k == 1 ? snow : rock;
                tri(list, ring[k, i], ring[k + 1, i], ring[k, j]);
                tri(list, ring[k, j], ring[k + 1, i], ring[k + 1, j]);
            }
        for (int i = 0; i < seg; i++) tri(snow, ring[2, i], apex, ring[2, (i + 1) % seg]);
        var m = new Mesh { name = "Mountain_" + index };
        m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(rock, 0); m.SetTriangles(snow, 1);
        m.RecalculateNormals(); m.RecalculateBounds();
        return SaveMesh(m, "Assets/Game/Meshes/Mountain_" + index + ".asset");
    }

    static void Peak(Mesh mesh, Material[] mats, Transform parent, Vector3 pos, float radius, float height, bool collider)
    {
        var go = new GameObject("Mountain");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, Rand(0, 360), 0);
        go.transform.localScale = new Vector3(radius, height, radius);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
    }

    static Mesh[] peakMeshes;
    static Material[] peakMats;

    static void EnsurePeaks()
    {
        if (peakMeshes != null) return;
        peakMats = new[] { Mat("Mountain_Rock", new Color(0.25f, 0.25f, 0.33f), 0.02f), Mat("Mountain_Snow", new Color(0.72f, 0.75f, 0.86f), 0.05f) };
        peakMeshes = new Mesh[4];
        for (int i = 0; i < peakMeshes.Length; i++) peakMeshes[i] = MountainMesh(i);
    }

    static void BuildMountains()
    {
        EnsurePeaks();
        var mts = new GameObject("Mountains").transform; mts.SetParent(env);

        // far backdrop: one low ring of wide peaks, merged into a single small mesh (view only, no colliders)
        int count = 13;
        var rock = new List<CombineInstance>(); var snow = new List<CombineInstance>();
        for (int i = 0; i < count; i++)
        {
            float ang = (360f / count) * i + Rand(-4, 4);
            float dist = Rand(176, 192);
            var pos = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * dist, -1f, 10 + Mathf.Sin(ang * Mathf.Deg2Rad) * dist);
            var trs = Matrix4x4.TRS(pos, Quaternion.Euler(0, Rand(0, 360), 0), new Vector3(Rand(52, 64), Rand(16, 30), Rand(52, 64)));
            var m = peakMeshes[rng.Next(peakMeshes.Length)];
            rock.Add(new CombineInstance { mesh = m, subMeshIndex = 0, transform = trs });
            snow.Add(new CombineInstance { mesh = m, subMeshIndex = 1, transform = trs });
        }
        var rockMesh = new Mesh(); rockMesh.CombineMeshes(rock.ToArray(), true, true);
        var snowMesh = new Mesh(); snowMesh.CombineMeshes(snow.ToArray(), true, true);
        var far = new Mesh { name = "FarMountains" };
        far.CombineMeshes(new[] {
            new CombineInstance { mesh = rockMesh, transform = Matrix4x4.identity },
            new CombineInstance { mesh = snowMesh, transform = Matrix4x4.identity } }, false, false);
        far.RecalculateBounds();
        Object.DestroyImmediate(rockMesh); Object.DestroyImmediate(snowMesh);
        far = SaveMesh(far, "Assets/Game/Meshes/FarMountains.asset");
        var go = new GameObject("Far Mountains");
        go.transform.SetParent(mts, false);
        go.AddComponent<MeshFilter>().sharedMesh = far;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = peakMats;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;

        // a few larger mountains behind the start area
        for (int i = 0; i < NearMountains.GetLength(0); i++)
        {
            float sc = NearMountains[i, 2];
            Peak(peakMeshes[i % peakMeshes.Length], peakMats, mts, new Vector3(NearMountains[i, 0], -0.5f, NearMountains[i, 1]), sc * 0.95f, sc * Rand(1.2f, 1.5f), false);
        }
    }

    // ---------- clouds ----------

    static void BuildClouds()
    {
        var clouds = new GameObject("Clouds").transform; clouds.SetParent(env);
        string[] names = { "cloud_big", "cloud_small", "cloud_big" };
        var cloudMat = Mat("Cloud", new Color(0.62f, 0.56f, 0.68f), 0f, 0f, new Color(0.20f, 0.14f, 0.18f));
        for (int i = 0; i < 10; i++)
        {
            // the first few sit ahead of the start view, the rest ring the level
            float ang = i < 4 ? 55 + i * 24 + Rand(-6, 6) : Rand(0, 360);
            float dist = Rand(85, 135);
            var pos = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad) * dist, Rand(40, 68), 10 + Mathf.Sin(ang * Mathf.Deg2Rad) * dist);
            var c = Place(Md("nature", names[rng.Next(names.Length)]), pos, Rand(0, 360), Rand(6, 12), clouds, false);
            if (c == null) continue;
            foreach (var r in c.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.sharedMaterial = cloudMat; }
        }
    }

    // ---------- grass ----------

    static void BuildGrass()
    {
        GrassPass("Grass", "Grass_", 9000, -60, 36, -78, 70, (x, z) => InPlayArea(x, z, 3f) && !Blocked(x, z), OnSideLoopPath);
    }

    static void GrassPass(string groupName, string prefix, int target, float x0, float x1, float z0, float z1, System.Func<float, float, bool> allowed, System.Func<float, float, bool> hide = null)
    {
        var grass = new GameObject(groupName).transform; grass.SetParent(env);
        var mat = Mat("Grass_Blade", new Color(0.27f, 0.41f, 0.20f), 0.02f);
        mat.SetFloat("_Cull", 0f); // blades are single triangles, visible from both sides
        EditorUtility.SetDirty(mat);


        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        int chunk = 0;
        System.Action flush = () =>
        {
            if (v.Count == 0) return;
            var m = new Mesh { name = prefix + chunk };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            m = SaveMesh(m, "Assets/Game/Meshes/" + prefix + chunk + ".asset");
            var go = new GameObject(prefix + chunk);
            go.transform.SetParent(grass, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            chunk++; v.Clear(); n.Clear(); t.Clear();
        };

        int clumps = 0, tries = 0;
        while (clumps < target && tries < target * 10)
        {
            tries++;
            float x = Rand(x0, x1), z = Rand(z0, z1);
            if (!allowed(x, z)) continue;
            int blades = 3 + rng.Next(4);
            bool hidden = hide != null && hide(x, z);
            for (int b = 0; b < blades; b++)
            {
                var p = new Vector3(x + Rand(-0.35f, 0.35f), 0f, z + Rand(-0.35f, 0.35f));
                float yaw = Rand(0, Mathf.PI * 2), w = Rand(0.06f, 0.12f), h = Rand(0.28f, 0.7f);
                var right = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * w;
                var lean = new Vector3(Rand(-0.14f, 0.14f), 0, Rand(-0.14f, 0.14f));
                if (hidden) continue;
                int i = v.Count;
                v.Add(p - right); v.Add(p + right); v.Add(p + Vector3.up * h + lean);
                n.Add(Vector3.up); n.Add(Vector3.up); n.Add(Vector3.up);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            clumps++;
            if (v.Count > 60000) flush();
        }
        flush();
    }

    // =====================================================================
    // LEVEL 2: begins behind the exit gate, which shuts once the player is through.
    // Path: sharp left (west) along the old fence line, then left again (south-west) into a hamlet
    // built from the "RPG Poly Pack - Lite" assets. No enemies yet and no sky from that pack.
    // =====================================================================

    const string RP = "Assets/ThirdParty/RPGPolyPackLite/Prefabs/";
    static readonly Vector3 L2Start = new Vector3(0f, 0f, 71f);     // where the player stands when level 2 begins
    static readonly Vector3 L2Corner = new Vector3(-38f, 0f, 72f);  // end of the west lane, where the path turns left again
    static readonly Vector3 L2Centre = new Vector3(-70f, 0f, 38f);  // middle of the hamlet clearing
    const float L2Radius = 25f;      // walkable radius of the clearing
    const float L2Spread = L2Radius / 15f;   // the hamlet was first laid out for a 15 m clearing; its props are spread by this
    const float L2HalfWidth = 5f;    // walkable half-width of the two lanes
    const float L2LaneZ = 72f;       // the west lane runs along this z

    // hamlet buildings: angle around the clearing (degrees, 0 = east, 90 = north), distance from its centre,
    // extra turn so the front faces the centre, footprint radius
    static readonly string[] L2BuildingNames = { "building_05", "building_01", "building_03", "building_02", "building_04" };
    // the three main houses (05, 04, 01) and building_03 stand on the right of the way in; building_02 on the left
    static readonly float[,] L2Buildings = { { 160f, 29.45f, 0f, 8f }, { 232f, 30.5f, 0f, 8f }, { 103f, 27.65f, 0f, 7.2f }, { 280f, 28.35f, 0f, 6.3f }, { 196f, 28f, 90f, 6.5f } };
    static readonly float[] L2BuildingHalfWidth = { 5.7f, 4.2f, 6f, 4.2f, 4.9f };   // across the front, for the fence gaps

    static Transform l2;
    static readonly List<Vector3[]> l2Paths = new List<Vector3[]>();   // the paths inside the hamlet: props are kept off them
    static readonly List<Vector3> l2Solid = new List<Vector3>(); // x, radius, z: where grass and clutter must not go

    static string Rp(string sub, string n) { return RP + sub + "/rpgpp_lt_" + n + ".prefab"; }

    static float SegDist(float x, float z, Vector3 a, Vector3 b)
    {
        float vx = b.x - a.x, vz = b.z - a.z, wx = x - a.x, wz = z - a.z;
        float t = Mathf.Clamp01((wx * vx + wz * vz) / (vx * vx + vz * vz));
        float dx = wx - vx * t, dz = wz - vz * t;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static Vector3 L2Ring(float angleDeg, float dist)
    {
        return L2Centre + new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad), 0, Mathf.Sin(angleDeg * Mathf.Deg2Rad)) * dist;
    }

    // distance from the middle of the level-2 path (gate -> west lane -> south-west lane -> clearing centre)
    static float L2PathDist(float x, float z)
    {
        float a = SegDist(x, z, new Vector3(0, 0, 66f), new Vector3(0, 0, L2LaneZ));
        float b = SegDist(x, z, new Vector3(0, 0, L2LaneZ), L2Corner);
        float c = SegDist(x, z, L2Corner, L2Centre - (L2Centre - L2Corner).normalized * (L2Radius - 1f));
        float best = Mathf.Min(a, Mathf.Min(b, c));
        foreach (var seg in l2Paths) best = Mathf.Min(best, SegDist(x, z, seg[0], seg[1]));
        return best;
    }

    // the part of level 2 the player can walk on
    static bool L2Walkable(float x, float z, float margin)
    {
        if (x > L2Corner.x - 3f - margin && x < L2HalfWidth + margin && z > 67f - margin && z < L2LaneZ + L2HalfWidth + margin) return true;
        if (SegDist(x, z, L2Corner, L2Centre) < L2HalfWidth + margin) return true;
        float dx = x - L2Centre.x, dz = z - L2Centre.z;
        return dx * dx + dz * dz < (L2Radius + margin) * (L2Radius + margin);
    }

    // everything level 2 occupies, buildings included (the forest stays out of it)
    static bool InLevel2(float x, float z, float margin)
    {
        if (L2Walkable(x, z, margin + 1f)) return true;
        for (int i = 0; i < L2BuildingNames.Length; i++)
        {
            Vector3 p = L2Ring(L2Buildings[i, 0], L2Buildings[i, 1]);
            float r = L2Buildings[i, 3] + margin, dx = x - p.x, dz = z - p.z;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    static bool L2Free(float x, float z, float pad)
    {
        foreach (var s in l2Solid)
        {
            float dx = x - s.x, dz = z - s.z, r = s.y + pad;
            if (dx * dx + dz * dz < r * r) return false;
        }
        return true;
    }

    // one asset from the new pack; solid > 0 gives it a collider and keeps grass and clutter that far away
    // sit = true lifts a model whose pivot is not at its base so that it rests on pos.y
    static GameObject L2Put(string sub, string n, Vector3 pos, float rotY, float scale, float solid, bool sit = false)
    {
        pos = L2OffPath(pos, solid > 0f ? solid + 1.6f : 2.2f);
        var go = Place(Rp(sub, n), pos, rotY, scale, l2, solid > 0f);
        if (go != null && sit) go.transform.position += Vector3.up * (pos.y - BoundsOf(go).min.y);
        if (solid > 0f) l2Solid.Add(new Vector3(pos.x, solid, pos.z));
        return go;
    }

    // moves a point sideways off any hamlet path it would stand on
    static Vector3 L2OffPath(Vector3 p, float clear)
    {
        foreach (var seg in l2Paths)
        {
            Vector3 a = seg[0], b = seg[1], ab = b - a; ab.y = 0;
            float t = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - a.x, 0, p.z - a.z), ab) / ab.sqrMagnitude);
            Vector3 q = a + ab * t, away = new Vector3(p.x - q.x, 0, p.z - q.z);
            float dist = away.magnitude;
            if (dist >= clear) continue;
            if (dist < 0.01f) away = new Vector3(-ab.z, 0, ab.x); // right on the line: push to one side
            p += away.normalized * (clear - dist);
        }
        return p;
    }

    // rotation that turns a model's local +z towards a point
    static float L2Face(Vector3 from, Vector3 to) { return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg; }

    static void L2Lantern(Vector3 pos, Vector3 toward)
    {
        pos = L2OffPath(pos, 2.4f);
        Vector3 arm = toward - pos; arm.y = 0; arm.Normalize();
        Place(Hw("post_lantern"), pos, Mathf.Atan2(arm.x, arm.z) * Mathf.Rad2Deg, 1f, l2);
        PointLight("Level 2 Lantern", pos + arm * 0.72f + Vector3.up * 2.7f, new Color(1f, 0.62f, 0.28f), 14f, 11f);
        l2Solid.Add(new Vector3(pos.x, 0.6f, pos.z));
    }

    // a row of fence pieces from a to b (the pack's fence models run along their local x)
    static void L2FenceRun(Vector3 a, Vector3 b, string[] names, float pieceLen, bool build = true, float height = 1f)
    {
        Vector3 d = b - a; float len = d.magnitude; d /= len;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / pieceLen));
        float step = len / n, rot = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
        for (int i = 0; i < n; i++)
        {
            string piece = names[rng.Next(names.Length)]; float turn = rot + Rand(-2f, 2f);
            if (!build) continue;
            var f = Place(Rp("Exterior/Wood_path", piece), a + d * (step * (i + 0.5f)), turn, step / pieceLen, l2, true, true);
            if (f != null && height != 1f) f.transform.localScale = new Vector3(1f, height, 1f);
        }
        if (build && names.Length > 0 && names[0].StartsWith("fence_wood_02"))
        {
            // one solid block along the whole run, so bullets and eyes cannot slip between the planks
            var block = new GameObject("Fence Block");
            block.transform.SetParent(l2, false);
            block.transform.position = (a + b) * 0.5f + Vector3.up * (1.1f * height);
            block.transform.rotation = Quaternion.LookRotation(b - a);
            block.AddComponent<BoxCollider>().size = new Vector3(0.25f, 2.2f * height, len);
        }
    }

    // dirt tiles and stepping stones like the level-1 route, turned to follow the lane from a to b
    static void L2PathRun(Vector3 a, Vector3 b, float lift)
    {
        Vector3 d = b - a; float len = d.magnitude; d /= len;
        Vector3 side = new Vector3(-d.z, 0, d.x);
        float rot = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 4f));
        for (int i = 0; i <= n; i++)
            Place(Hw("floor_dirt"), a + d * (len * i / n) + Vector3.up * lift, rot + 90 * rng.Next(4), 1f, l2, false);
        string[] stones = { "path_A", "path_B", "path_C", "path_D" };
        for (float t = 1f; t < len; t += 3.2f)
            Place(Hw(stones[rng.Next(4)]), a + d * t + side * Rand(-1.1f, 1.1f) + Vector3.up * (lift + 0.04f), Rand(0, 360), 1f, l2, false);
    }

    static void BuildLevel2()
    {
        var levelOneRng = rng;
        rng = new System.Random(9090); // its own sequence, so level 1 is built exactly as before
        l2 = new GameObject("Level 2").transform; l2.SetParent(env);
        l2Solid.Clear(); l2Paths.Clear();

        Vector3 K = L2Centre, C = L2Corner;
        Vector3 d = (K - C); d.y = 0; float run = d.magnitude; d /= run;   // direction of the second lane
        Vector3 inward = new Vector3(-d.z, 0, d.x);                         // its left side: towards level 1
        float w = L2HalfWidth, zIn = 67.2f, zOut = L2LaneZ + w;
        float tIn = (zIn - (C.z + inward.z * w)) / d.z, tOut = (zOut - (C.z - inward.z * w)) / d.z;
        float tEnd = run - Mathf.Sqrt(L2Radius * L2Radius - w * w);
        Vector3 innerCorner = C + inward * w + d * tIn, outerCorner = C - inward * w + d * tOut;
        Vector3 innerEnd = C + inward * w + d * tEnd, outerEnd = C - inward * w + d * tEnd;
        float entryAngle = Mathf.Atan2(-d.z, -d.x) * Mathf.Rad2Deg;         // where the lane meets the clearing
        float entryHalf = Mathf.Asin(w / L2Radius) * Mathf.Rad2Deg;
        if (entryAngle < 0) entryAngle += 360f;

        // ---- invisible walls: lanes and clearing only ----
        var walls = new GameObject("Level 2 Barriers").transform; walls.SetParent(l2);
        Wall(walls, new Vector3(w, 0, 66f), new Vector3(w, 0, zIn));   // beside the gate; the way east is open (stage 3)
        Wall(walls, new Vector3(w, 0, zOut), outerCorner);
        Wall(walls, outerCorner, outerEnd);
        Wall(walls, new Vector3(-3.2f, 0, zIn), innerCorner);
        Wall(walls, innerCorner, innerEnd);
        float a0 = entryAngle + entryHalf, a1 = entryAngle + 360f - entryHalf, da = (a1 - a0) / 44f;
        for (int i = 0; i < 44; i++) Wall(walls, L2Ring(a0 + da * i, L2Radius), L2Ring(a0 + da * (i + 1), L2Radius));

        // ---- path surface ----
        L2PathRun(new Vector3(0, 0, 68f), new Vector3(0, 0, L2LaneZ), 0.02f);
        L2PathRun(new Vector3(-4f, 0, L2LaneZ), C + new Vector3(2f, 0, 0), 0.026f);
        L2PathRun(C, K - d * (L2Radius - 1f), 0.032f);

        // ---- fences: no way back into level 1 ----
        string[] tall = { "fence_wood_02a", "fence_wood_02b", "fence_wood_02c" };
        string[] low = { "fence_wood_01a", "fence_wood_01b" };
        // dead end to the right of the gate and behind the landing: the only way on is left
        L2FenceRun(new Vector3(w, 0, 66.6f), new Vector3(w, 0, zOut), tall, 2f, false);   // was the dead end on the right: now the path to stage 3
        L2FenceRun(new Vector3(w, 0, zOut), new Vector3(-w - 1f, 0, zOut), tall, 2f);
        // closes the corner of the old fence line, then runs down the level-1 side of the second lane
        L2FenceRun(new Vector3(-31.8f, 0, 66.3f), innerCorner, tall, 2f);
        L2FenceRun(innerCorner, innerEnd, tall, 2f);

        // ---- buildings around the clearing, fronts to the middle ----
        for (int i = 0; i < L2BuildingNames.Length; i++)
        {
            Vector3 p = L2Ring(L2Buildings[i, 0], L2Buildings[i, 1]);
            var bld = Place(Rp("Buildings/Bld_closed", L2BuildingNames[i]), p, L2Face(p, K) + L2Buildings[i, 2], 1f, l2);
            if (L2BuildingNames[i] == "building_03") l2House03 = bld;
            if (L2BuildingNames[i] == "building_05") l2House05 = bld;
            l2Solid.Add(new Vector3(p.x, L2Buildings[i, 3], p.z));
        }

        // ---- a small house just inside the way in: it blocks the view, so the village must be entered to be fought ----
        Vector3 gSide = new Vector3(-d.z, 0, d.x);
        Vector3 G = K - d * 16.5f;                                       // centre of the gatehouse (its back is to the way in)
        Place(Rp("Buildings/Bld_closed", "building_02"), G, L2Face(G, K), 1f, l2).name = "Gatehouse";
        l2Solid.Add(new Vector3(G.x, 5.5f, G.z));
        // a short tall fence on each side of its back: you have to walk in and turn before you can see the village
        foreach (float sgn in new[] { 1f, -1f })
            L2FenceRun(K - d * 21.3f, K - d * 21.3f + gSide * 7.2f * sgn, new[] { "fence_wood_02a", "fence_wood_02b" }, 2f, true, 1.8f);   // a 4 m palisade behind the gatehouse

        // ---- paths: round both sides of the gatehouse, and from the well out to every door ----
        Vector3 rim = K - d * (L2Radius - 1f), front = K - d * 7f;
        foreach (float sgn in new[] { 1f, -1f })
        {
            Vector3 round1 = K - d * 23.2f + gSide * 9f * sgn, round2 = K - d * 17f + gSide * 9f * sgn;
            l2Paths.Add(new[] { rim, round1 }); l2Paths.Add(new[] { round1, round2 }); l2Paths.Add(new[] { round2, front + gSide * 2f * sgn });
        }
        for (int i = 0; i < L2BuildingNames.Length; i++)
            l2Paths.Add(new[] { L2Ring(L2Buildings[i, 0], 5.5f), L2Ring(L2Buildings[i, 0], L2Radius - 1.6f) });
        l2Paths.Add(new[] { K - d * 5.5f, K - d * 11.5f });   // the gatehouse door
        foreach (var seg in l2Paths) L2PathRun(seg[0], seg[1], 0.034f);

        // ---- the hamlet: positions are metres east (x) and north (z) of the clearing centre ----
        System.Func<float, float, Vector3> at = (lx, lz) => K + new Vector3(lx, 0, lz) * L2Spread;
        const string PR = "Props/", EX = "Exterior/Wood_path";
        // well and seats in the middle
        l2Well = L2Put(PR + "Misc", "well_01", at(-1f, -2f), 20, 1f, 1.9f);
        L2Put(PR + "Containers", "bucket_01", at(1.2f, -1.2f), 40, 1f, 0.5f);
        L2Put(PR + "Benches", "bench_wood_01", at(-4.6f, -0.8f), 80, 1f, 1.0f);
        L2Put(PR + "Benches", "bench_wood_03", at(2.2f, -5.6f), 160, 1f, 1.0f);
        L2Put(PR + "Benches", "bench_wood_02", at(-3.2f, -5.8f), 30, 1f, 1.0f);
        L2Put(PR + "Containers", "barrel_01", at(-2.9f, -3.9f), 0, 1f, 0.6f);
        L2Put(PR + "Containers", "barrel_02", at(-3.8f, -3.2f), 50, 1f, 0.6f);
        // wagon and supplies beside the way in
        L2Put(PR + "Misc", "wagon_01", at(9.8f, 4.6f), 25, 1f, 2.4f, true);
        L2Put(PR + "Containers", "trough_01", at(11.4f, 1.2f), 100, 1f, 1.1f);
        L2Put(PR + "Containers", "sack_02_set", at(7.6f, 2.0f), 70, 1f, 0.8f);
        L2Put(PR + "Containers", "sack_01", at(6.9f, 3.4f), 200, 1f, 0.8f, true);
        L2Put(PR + "Containers", "box_wood_01", at(12.2f, 5.8f), 15, 1f, 0.8f, true);
        // two market stalls on the east side
        Vector3 stallA = at(9.2f, -4.8f), stallB = at(6.4f, -10f);
        l2AwningA = L2Put(EX, "awning_standing_01a", stallA, L2Face(stallA, K), 1f, 2.2f, true);
        l2AwningB = L2Put(EX, "awning_standing_01b", stallB, L2Face(stallB, K), 1f, 2.2f, true);
        L2Put(PR + "Tables", "table_01", stallA, L2Face(stallA, K) + 90, 1f, 0f);
        L2Put(PR + "Containers", "crate_01", stallB + new Vector3(-0.6f, 0, 0.3f), 30, 1f, 0f);
        L2Put(PR + "Containers", "crate_02", stallB + new Vector3(0.5f, 0, -0.4f), 75, 1f, 0f);
        L2Put(PR + "Containers", "crate_03", stallB + new Vector3(0.3f, 0, 0.8f), 10, 1f, 0f);
        L2Put(PR + "Containers", "basket_01", at(6.6f, -3.6f), 0, 1f, 0.5f);
        L2Put(PR + "Containers", "basket_02", at(7.1f, -6.4f), 0, 1f, 0.5f);
        L2Put(PR + "Containers", "sack_open_01", at(4.2f, -8.6f), 120, 1f, 0.6f);
        L2Put(PR + "Containers", "sack_02", at(3.9f, -9.7f), 0, 1f, 0.6f);
        L2Put(PR + "Containers", "vase_01", at(8.8f, -7.4f), 0, 1f, 0.35f);
        L2Put(PR + "Containers", "vase_02", at(9.4f, -7.9f), 0, 1f, 0.35f);
        L2Put(PR + "Containers", "vase_03", at(8.5f, -8.2f), 0, 1f, 0.35f);
        L2Put(PR + "Containers", "jug_01", at(5.3f, -7.9f), 0, 1f, 0f);
        L2Put(PR + "Containers", "bowl_metal_01", at(11.3f, -6.2f), 0, 1f, 0.5f);
        L2Put(PR + "Containers", "package_01", at(10.9f, -8.4f), 40, 1f, 0.6f, true);
        // sheds, washing and tools on the west and south sides
        Vector3 shedA = L2Ring(312f, L2Radius - 1.2f), shedB = L2Ring(132f, L2Radius - 1.2f);
        L2Put(EX, "shed_wood_01", shedA, L2Face(shedA, K), 1f, 2.4f);
        l2Shed = L2Put(EX, "shed_wood_02", shedB, L2Face(shedB, K), 1f, 2.0f);
        L2Put(PR + "Items", "rake_01", at(7.2f, -12.4f), 35, 1f, 0f);
        L2Put(PR + "Items", "broom_01", at(-9.2f, 8.6f), 140, 1f, 0f);
        L2Put(PR + "Hangers", "hanger_clothes_01", at(-9.2f, 3.4f), 75, 1f, 1.6f);
        L2Put(PR + "Hangers", "hanger_wood_01", at(-11.4f, -3.6f), 95, 1f, 1.2f);
        L2Put(PR + "Hangers", "hanger_wood_02", at(-7.4f, -9.4f), 25, 1f, 1.1f);
        L2Put(PR + "Containers", "bathtub_wood_01", at(-11.2f, 6.2f), 0, 1f, 1.0f);
        L2Put(PR + "Misc", "bird_house_01", at(0.4f, 12.6f), 200, 1f, 0.5f);
        // firewood on the south side
        L2Put(PR + "Wood", "log_wood_01", at(-1.8f, -11.6f), 0, 1f, 1.2f, true);
        L2Put(PR + "Wood", "log_wood_02a", at(-4.8f, -12.2f), 15, 1f, 1.3f);
        L2Put(PR + "Wood", "log_wood_02b", at(1.0f, -12.8f), 70, 1f, 1.0f);
        L2Put(PR + "Misc", "stones_01", at(-3.4f, -9.6f), 60, 1f, 0.9f);
        // rocks and bushes along the edge
        L2Put("Nature/Rocks", "rock_01", L2Ring(80f, L2Radius - 1.6f) - Vector3.up * 0.25f, 30, 1f, 1.4f, true);
        L2Put("Nature/Rocks", "rock_02", L2Ring(214f, L2Radius - 1.6f) - Vector3.up * 0.25f, 110, 1f, 1.4f, true);
        L2Put("Nature/Rocks", "rock_03", L2Ring(345f, L2Radius - 1.6f) - Vector3.up * 0.25f, 250, 1f, 1.6f, true);
        for (float ba = entryAngle + 18f; ba < entryAngle + 345f; ba += 17f)
        {
            Vector3 bp = L2Ring(ba + Rand(-4f, 4f), L2Radius - 1.4f);
            if (!L2Free(bp.x, bp.z, 0.6f) || L2PathDist(bp.x, bp.z) < 2.6f) continue;
            L2Put("Nature/Vegetation/Bushes", rng.Next(2) == 0 ? "bush_01" : "bush_02", bp, Rand(0, 360), Rand(0.9f, 1.3f), 1.3f);
        }
        // tall trees inside the east edge: hide the awnings from the second lane
        for (float ta = 6f; ta <= 34f; ta += 7f)
        {
            Vector3 tp = L2Ring(ta, L2Radius - 2.2f);
            if (!L2Free(tp.x, tp.z, 0.8f) || L2PathDist(tp.x, tp.z) < 2.6f) continue;
            L2Put("Nature/Vegetation/Trees", rng.Next(2) == 0 ? "tree_02" : "tree_pine_01", tp, Rand(0, 360), Rand(1.7f, 2.1f), 0.9f);
        }
        // a few trees inside the clearing, between the houses
        foreach (float ta in new[] { 128f, 178f, 214f, 258f, 300f, 22f })
        {
            Vector3 tp = L2Ring(ta + Rand(-3f, 3f), L2Radius - Rand(4f, 6f));
            if (!L2Free(tp.x, tp.z, 1.5f) || L2PathDist(tp.x, tp.z) < 3f) continue;
            L2Put("Nature/Vegetation/Trees", rng.Next(2) == 0 ? "tree_01" : "tree_pine_01", tp, Rand(0, 360), Rand(1.3f, 1.8f), 0.9f);
        }

        // ---- low fence around the open parts of the clearing edge ----
        System.Func<float, bool> edgeTaken = ang =>
        {
            if (Mathf.Abs(Mathf.DeltaAngle(ang, entryAngle)) < entryHalf + 0.6f) return true;
            for (int i = 0; i < L2BuildingNames.Length; i++)
                if (Mathf.Abs(Mathf.DeltaAngle(ang, L2Buildings[i, 0])) < Mathf.Asin(L2BuildingHalfWidth[i] / L2Radius) * Mathf.Rad2Deg + 2f) return true;
            return Mathf.Abs(Mathf.DeltaAngle(ang, 132f)) < 5f || Mathf.Abs(Mathf.DeltaAngle(ang, 312f)) < 5f;   // the two sheds
        };
        // tall all round (taller beside the way in), so the village cannot be shot into from outside;
        // the stretches either side of the way in start exactly at its edge
        float eA = entryAngle + entryHalf + 0.6f, eB = entryAngle - entryHalf - 0.6f + 360f;
        for (float fa = eA; fa < eB - 0.01f; fa += Mathf.Min(7f, eB - fa))
        {
            float fb = Mathf.Min(fa + 7f, eB), fmid = (fa + fb) * 0.5f;
            if (edgeTaken(fmid)) continue;
            bool nearEntry = Mathf.Abs(Mathf.DeltaAngle(fmid, entryAngle)) < 40f;
            L2FenceRun(L2Ring(fa, L2Radius + 0.2f), L2Ring(fb, L2Radius + 0.2f), tall, 2f, true, nearEntry ? 1.8f : 1f);
        }

        // ---- lanterns ----
        L2Lantern(new Vector3(3.3f, 0, 74.6f), new Vector3(0, 0, 72f));
        L2Lantern(new Vector3(-12f, 0, 75.5f), new Vector3(-12f, 0, 72f));
        L2Lantern(new Vector3(-26f, 0, 68.6f), new Vector3(-26f, 0, 72f));
        L2Lantern(new Vector3(-39.6f, 0, 75.2f), C);
        Vector3 mid = C + d * 11f;
        L2Lantern(mid - inward * 3.6f, mid);
        Vector3 gateIn = K - d * (L2Radius + 2f);
        L2Lantern(gateIn + inward * 3.5f, gateIn);
        for (int i = 0; i < L2BuildingNames.Length; i++)
            L2Lantern(L2Ring(L2Buildings[i, 0] + 9f, L2Radius - 4.5f), L2Ring(L2Buildings[i, 0], L2Radius - 4.5f));
        L2Lantern(at(2.6f, -3.4f), K);
        L2Lantern(at(-9.6f, -0.4f), K);
        L2Lantern(at(0.4f, -9.6f), K);

        // ---- the pack's trees: behind the buildings and along both lanes ----
        string[] packTrees = { "tree_01", "tree_02", "tree_pine_01" };
        for (float a = 0f; a < 360f; a += 6f)
        {
            float dist = Rand(L2Radius + 2.5f, L2Radius + 12f);
            Vector3 p = L2Ring(a + Rand(-3f, 3f), dist);
            if (L2Walkable(p.x, p.z, 2f) || !L2Free(p.x, p.z, 1f)) continue;
            L2Put("Nature/Vegetation/Trees", packTrees[rng.Next(packTrees.Length)], p, Rand(0, 360), Rand(1.3f, 2.1f), 0.6f);
        }
        // tall forest pines between the hamlet and level 1, so the old village cannot be seen from here
        string[] pines = { "tree_pine_orange_large", "tree_pine_yellow_large", "tree_pine_orange_large", "tree_pine_yellow_medium" };
        for (float z = 12f; z <= 60f; z += 3.4f)
            for (int row = 0; row < 2; row++)
            {
                Vector3 p = new Vector3(-37.2f - row * 3.6f + Rand(-0.8f, 0.8f), 0, z + row * 1.7f + Rand(-0.6f, 0.6f));
                if (L2Walkable(p.x, p.z, 2.2f) || !L2Free(p.x, p.z, 0.5f)) continue;
                Place(Hw(pines[rng.Next(pines.Length)]), p, Rand(0, 360), Rand(1.5f, 2.0f), l2, true);
            }

        System.Action<Vector3, Vector3, float, bool, bool> lineSides = (a, b, spacing, leftSide, rightSide) =>
        {
            Vector3 dir = (b - a).normalized, side = new Vector3(-dir.z, 0, dir.x);
            for (float t = 3f; t < (b - a).magnitude - 2f; t += spacing)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if ((sgn > 0 && !leftSide) || (sgn < 0 && !rightSide)) continue;
                    Vector3 p = a + dir * (t + Rand(-1.5f, 1.5f)) + side * sgn * Rand(6.6f, 9.5f);
                    if (L2Walkable(p.x, p.z, 1.2f) || InPlayArea(p.x, p.z, 1f) || !L2Free(p.x, p.z, 1f)) continue;
                    double pick = rng.NextDouble();
                    if (pick < 0.55) L2Put("Nature/Vegetation/Trees", packTrees[rng.Next(packTrees.Length)], p, Rand(0, 360), Rand(1.3f, 2.0f), 0.6f);
                    else if (pick < 0.8) L2Put("Nature/Vegetation/Bushes", rng.Next(2) == 0 ? "bush_01" : "bush_02", p, Rand(0, 360), Rand(1f, 1.5f), 0f);
                    else L2Put("Nature/Rocks", "rock_0" + (1 + rng.Next(3)), p - Vector3.up * 0.25f, Rand(0, 360), Rand(0.9f, 1.4f), 0f, true);
                }
        };
        lineSides(new Vector3(4f, 0, L2LaneZ), C, 5.5f, false, true);   // west lane: north side only (level 1's fence is on the south)
        lineSides(C, K - d * (L2Radius + 2f), 5.5f, true, true);

        // ---- small plants, flowers and pebbles on the ground ----
        string[] small = { "Nature/Vegetation/Flowers|flower_01", "Nature/Vegetation/Flowers|flower_02", "Nature/Vegetation/Flowers|flower_03",
                           "Nature/Vegetation/Grass|grass_small_01a", "Nature/Vegetation/Grass|grass_small_01b", "Nature/Vegetation/Grass|grass_small_01a",
                           "Nature/Vegetation/Plants|plant_01", "Nature/Vegetation/Plants|plant_02",
                           "Nature/Rocks|rock_small_01", "Nature/Rocks|rock_small_02", "Nature/Rocks|rocks_tiny_01" };
        int scattered = 0, attempts = 0;
        while (scattered < 280 && attempts < 9000)
        {
            attempts++;
            float x = Rand(K.x - L2Radius - 1f, 6f), z = Rand(K.z - L2Radius - 1f, 78f);
            if (!L2Walkable(x, z, -0.6f) || L2PathDist(x, z) < 2.7f || !L2Free(x, z, 0.3f)) continue;
            string[] pick = small[rng.Next(small.Length)].Split('|');
            L2Put(pick[0], pick[1], new Vector3(x, 0f, z), Rand(0, 360), Rand(0.85f, 1.3f), 0f, pick[1].StartsWith("rock_small"));
            scattered++;
        }

        // ---- grass blades, like level 1 ----
        GrassPass("Grass Level 2", "Grass_L2_", 3800, K.x - L2Radius - 1f, 7f, K.z - L2Radius - 1f, 78f,
                  (x, z) => L2Walkable(x, z, 0.4f) && L2PathDist(x, z) > 2.4f && L2Free(x, z, 0f));

        rng = levelOneRng;
    }

    // =====================================================================
    // SIDE LOOP (level 1): a U-shaped path that leaves the route opposite the first graveyard, runs behind
    // the two red houses and rejoins the route by the square gate. With the route it makes a full circle,
    // so the player can lead enemies round and round. Dressed with a small camp and a pumpkin patch.
    // =====================================================================

    const float LoopSouthZ = -38f, LoopNorthZ = -2f, LoopEastX = 40f;

    // forest trees that would stand on the loop are left out
    static bool InSideLoop(float x, float z)
    {
        return x > 2.5f && x < LoopEastX + 4.6f && z > LoopSouthZ - 4.2f && z < LoopNorthZ + 4.5f;   // the whole loop and both big houses
    }

    // on the loop's dirt tiles (no grass blades there)
    static bool OnSideLoopPath(float x, float z)
    {
        if (x > 1.8f && x < LoopEastX + 2.2f && Mathf.Abs(z - LoopSouthZ) < 2.2f) return true;
        if (x > 1.8f && x < LoopEastX + 2.2f && Mathf.Abs(z - LoopNorthZ) < 2.2f) return true;
        return Mathf.Abs(x - LoopEastX) < 2.2f && z > LoopSouthZ - 2.2f && z < LoopNorthZ + 2.2f;
    }

    static void LoopLantern(Transform parent, Vector3 pos, Vector3 toward)
    {
        Vector3 arm = toward - pos; arm.y = 0; arm.Normalize();
        Place(Hw("post_lantern"), pos, Mathf.Atan2(arm.x, arm.z) * Mathf.Rad2Deg, 1f, parent);
        PointLight("Side Loop Lantern", pos + arm * 0.72f + Vector3.up * 2.7f, new Color(1f, 0.62f, 0.28f), 14f, 11f);
    }

    static void BuildSideLoop()
    {
        var levelOneRng = rng;
        rng = new System.Random(5151); // own sequence: the rest of level 1 is built exactly as before
        var loop = new GameObject("Side Loop").transform; loop.SetParent(env);

        // ---- dirt tiles: south arm, east arm, north arm ----
        System.Action<float, float, float> tile = (x, z, widthX) =>
        {
            var t = Place(Hw("floor_dirt"), new Vector3(x, 0.02f, z), widthX < 1f ? 0 : 90 * rng.Next(4), 1f, loop, false);
            if (t != null && widthX < 1f) t.transform.localScale = new Vector3(widthX, 1f, 1f);
        };
        foreach (float z in new[] { LoopSouthZ, LoopNorthZ })
        {
            for (float x = 4f; x <= LoopEastX - 4f; x += 4f) tile(x, z, 1f);
        }
        for (float z = LoopSouthZ; z <= LoopNorthZ + 0.1f; z += 4f) tile(LoopEastX, z, 1f);
        string[] stones = { "path_A", "path_B", "path_C", "path_D" };
        for (float x = 5f; x < LoopEastX - 3f; x += 3.4f)
        {
            Place(Hw(stones[rng.Next(4)]), new Vector3(x, 0.06f, LoopSouthZ + Rand(-1.1f, 1.1f)), Rand(0, 360), 1f, loop, false);
            Place(Hw(stones[rng.Next(4)]), new Vector3(x, 0.06f, LoopNorthZ + Rand(-1.1f, 1.1f)), Rand(0, 360), 1f, loop, false);
        }
        for (float z = LoopSouthZ + 1f; z < LoopNorthZ; z += 3.4f)
            Place(Hw(stones[rng.Next(4)]), new Vector3(LoopEastX + Rand(-1.1f, 1.1f), 0.06f, z), Rand(0, 360), 1f, loop, false);

        // ---- wooden fence round the outside of the U ----
        for (float z = -39.6f; z <= 2f; z += 4.6f)
            Place(Md("neutral", "fence_wood_straight"), new Vector3(LoopEastX + 2.9f, 0, z), Rand(-4, 4), 4f, loop, true, true);
        for (float x = 9.6f; x <= LoopEastX + 1f; x += 4.6f)
            Place(Md("neutral", "fence_wood_straight"), new Vector3(x, 0, LoopSouthZ - 2.9f), 90 + Rand(-4, 4), 4f, loop, true, true);

        // ---- lanterns ----
        LoopLantern(loop, new Vector3(12f, 0, LoopSouthZ - 2.4f), new Vector3(12f, 0, LoopSouthZ));
        LoopLantern(loop, new Vector3(LoopEastX + 2.4f, 0, -29f), new Vector3(LoopEastX, 0, -29f));
        LoopLantern(loop, new Vector3(LoopEastX + 2.4f, 0, -11f), new Vector3(LoopEastX, 0, -11f));
        LoopLantern(loop, new Vector3(15f, 0, LoopNorthZ + 2.5f), new Vector3(15f, 0, LoopNorthZ));

        // ---- small camp between the two houses ----
        Place(Md("props", "tent"), new Vector3(15.5f, 0, -22.6f), 200, 6.5f, loop, true, true);
        Place(Md("props", "weaponrack"), new Vector3(11.6f, 0, -24.4f), 60, 6f, loop, true, true);
        Place(Md("props", "target"), new Vector3(21.4f, 0, -21.2f), -90, 6f, loop, true, true);
        Place(Md("props", "bucket_arrows"), new Vector3(20.2f, 0, -23.6f), 0, 6f, loop, true, true);
        Place(Md("props", "crate_open"), new Vector3(12.6f, 0, -20.4f), 25, 6f, loop, true, true);
        Place(Md("props", "sack"), new Vector3(18.6f, 0, -24.6f), 110, 6f, loop, true, true);
        Place(Md("props", "flag_red"), new Vector3(18.8f, 0, -20.2f), 40, 6f, loop, true, true);
        Place(Hw("lantern_standing"), new Vector3(17.9f, 0, -22.4f), 0, 1f, loop);
        PointLight("Camp Lantern", new Vector3(17.9f, 1.1f, -22.4f), new Color(1f, 0.62f, 0.28f), 8f, 8f);
        Place(Hw("bench"), new Vector3(23.2f, 0, -22.5f), 90, 1f, loop);

        // ---- stores behind the big house ----
        Place(Md("props", "resource_lumber"), new Vector3(17f, 0, -4.9f), 0, 5f, loop, true, true);
        Place(Md("props", "pallet"), new Vector3(12.6f, 0, -4.9f), 10, 6f, loop, true, true);
        Place(Md("props", "barrel"), new Vector3(20.6f, 0, -4.9f), 0, 6f, loop, true, true);
        Place(Md("props", "crate_A_small"), new Vector3(21.9f, 0, -4.8f), 30, 6f, loop, true, true);
        Place(Md("props", "resource_stone"), new Vector3(23.3f, 0, -26.2f), 70, 5f, loop, true, true);

        // ---- pumpkin patch beside the small red house ----
        string[] pumpkins = { "pumpkin_orange", "pumpkin_orange_small", "pumpkin_yellow", "pumpkin_yellow_small", "pumpkin_orange_jackolantern", "pumpkin_orange_small" };
        for (int i = 0; i < 9; i++)
            Place(Hw(pumpkins[i % pumpkins.Length]), new Vector3(8.3f + (i % 3) * 1.5f + Rand(-0.3f, 0.3f), 0, -33.8f + (i / 3) * 1.9f + Rand(-0.3f, 0.3f)), Rand(0, 360), Rand(0.9f, 1.2f), loop);
        Place(Hw("post_skull"), new Vector3(7.6f, 0, -28.4f), 250, 1f, loop);
        PointLight("Pumpkin Glow", new Vector3(9.8f, 0.8f, -32f), new Color(1f, 0.5f, 0.15f), 5f, 6f);

        // ---- flowers, plants and bushes from the new pack along the loop ----
        string[] small = { "Nature/Vegetation/Flowers|flower_01", "Nature/Vegetation/Flowers|flower_02", "Nature/Vegetation/Flowers|flower_03",
                           "Nature/Vegetation/Grass|grass_small_01a", "Nature/Vegetation/Grass|grass_small_01b", "Nature/Vegetation/Plants|plant_01", "Nature/Vegetation/Plants|plant_02" };
        int done = 0, tries = 0;
        while (done < 34 && tries < 2000)
        {
            tries++;
            float x = Rand(7.5f, LoopEastX + 2.4f), z = Rand(LoopSouthZ - 2.4f, LoopNorthZ + 3f);
            if (OnSideLoopPath(x, z)) continue;
            if (x > 9.5f && x < 37.5f && z > -19.5f && z < -4f) continue;    // the two big houses
            if (x > 11.5f && x < 22.5f && z > -36.5f && z < -25.5f) continue; // small red house
            if (x > 10.5f && x < 22.5f && z > -25.5f && z < -19.5f) continue; // camp
            if (x < 12.5f && z > -35f && z < -29f) continue;                  // pumpkin patch
            string[] pick = small[rng.Next(small.Length)].Split('|');
            Place(Rp(pick[0], pick[1]), new Vector3(x, 0, z), Rand(0, 360), Rand(0.9f, 1.3f), loop, false);
            done++;
        }
        Place(Rp("Nature/Vegetation/Bushes", "bush_01"), new Vector3(23.4f, 0, -35.4f), 40, 1.1f, loop);
        Place(Rp("Nature/Vegetation/Bushes", "bush_02"), new Vector3(23.4f, 0, 2.3f), 120, 1f, loop);
        Place(Rp("Nature/Vegetation/Bushes", "bush_01"), new Vector3(8.6f, 0, 1.6f), 200, 1f, loop);

        // grass on the new ground east of the houses
        GrassPass("Grass Side Loop", "Grass_SL_", 1400, 23.6f, LoopEastX + 2.4f, LoopSouthZ - 2.4f, LoopNorthZ + 2.4f,
                  (x, z) => !(x > 23f && x < 37f && z > -18.6f && z < -5.4f), OnSideLoopPath);

        rng = levelOneRng;
    }

    // =====================================================================
    // STAGE 3: from the level-2 start the path also runs right (east), straight along the old fence line.
    // Where that fence ends there is a gate, and behind it a small round arena (a boss area for later):
    // barrels for cover, a floor with some blocks left as grass, and one small house. No enemies yet.
    // =====================================================================

    const float S3Radius = 26f;    // walkable radius of the arena (52 m across)
    const float S3GateX = 34f;     // the arena gate, where the level-1 fence ends
    static readonly Vector3 S3Arena = new Vector3(S3GateX + 5f + S3Radius, 0f, 72f);        // middle of the arena
    static readonly Vector3 S3House = new Vector3(S3GateX + 5f + 2f * S3Radius + 5.5f, 0f, 72f); // small house on its far side
    const int S3Cells = 6;         // floor blocks reach this many 4 m cells out from the middle

    // arena floor: 4 m blocks on a grid; about a quarter are left out so grass grows there
    static bool S3Block(int i, int j)
    {
        float dx = 4f * i, dz = 4f * j, r = S3Radius - 1.6f;
        if (j == 0) return i >= -S3Cells && i < S3Cells;   // the row from the gate to the middle is always paved
        if (dx * dx + dz * dz > r * r) return false;
        return (i * 7 + j * 13 + 100) % 4 != 0;
    }

    static bool S3OnBlock(float x, float z)
    {
        int i = Mathf.RoundToInt((x - S3Arena.x) / 4f), j = Mathf.RoundToInt((z - S3Arena.z) / 4f);
        return S3Block(i, j);
    }

    static bool S3Walkable(float x, float z, float margin)
    {
        if (x > L2HalfWidth && x < S3Arena.x && z > 67f - margin && z < L2LaneZ + L2HalfWidth + margin) return true;
        float dx = x - S3Arena.x, dz = z - S3Arena.z;
        return dx * dx + dz * dz < (S3Radius + margin) * (S3Radius + margin);
    }

    // everything stage 3 occupies (the forest stays out of it)
    static readonly Transform[] s3Graveyards = new Transform[2];
    // the two graveyards: left (north) and right (south) of the arena, gates facing it
    static Vector3 S3GraveCentre(int i) { return S3Arena + new Vector3(0, 0, (i == 0 ? 1f : -1f) * (S3Radius + 10.6f)); }

    static bool InStage3(float x, float z, float margin)
    {
        if (S3Walkable(x, z, margin + 1f)) return true;
        for (int i = 0; i < 2; i++)
        {
            Vector3 g = S3GraveCentre(i);
            if (Mathf.Abs(x - g.x) < 11.5f + margin && Mathf.Abs(z - g.z) < 11.5f + margin) return true;
        }
        float dx = x - S3House.x, dz = z - S3House.z, r = 9f + margin;
        return dx * dx + dz * dz < r * r;
    }

    static void BuildStage3()
    {
        var levelOneRng = rng;
        rng = new System.Random(7373); // own sequence: levels 1 and 2 are built exactly as before
        var s3 = new GameObject("Stage 3").transform; s3.SetParent(env);
        var level2Parent = l2; l2 = s3;   // the level-2 helpers (lanterns, paths, props) now add to stage 3
        Vector3 A = S3Arena;
        float w = L2HalfWidth, zIn = 67.2f, zOut = L2LaneZ + w;
        float rimX = A.x - Mathf.Sqrt(S3Radius * S3Radius - w * w);
        float openHalf = Mathf.Asin(w / S3Radius) * Mathf.Rad2Deg;
        System.Func<float, float, Vector3> ring = (deg, dist) => A + new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0, Mathf.Sin(deg * Mathf.Deg2Rad)) * dist;

        // ---- invisible walls: east lane and arena ----
        var walls = new GameObject("Stage 3 Barriers").transform; walls.SetParent(s3);
        Wall(walls, new Vector3(w, 0, zOut), new Vector3(rimX, 0, zOut));
        Wall(walls, new Vector3(3.2f, 0, zIn), new Vector3(rimX, 0, zIn));
        Wall(walls, new Vector3(rimX, 0, zIn), new Vector3(rimX, 0, A.z - w));
        // gaps in the ring: the house door (0) and the two graveyard gates (90, 270)
        float[] gaps = { 0f, 90f, 270f };
        float a0 = 180f + openHalf, a1 = 540f - openHalf, da = (a1 - a0) / 88f;
        for (int i = 0; i < 88; i++)
        {
            float m = a0 + da * (i + 0.5f);
            bool open = false;
            foreach (float g in gaps) if (Mathf.Abs(Mathf.DeltaAngle(m, g)) < 5f) open = true;
            if (!open) Wall(walls, ring(a0 + da * i, S3Radius), ring(a0 + da * (i + 1), S3Radius));
        }

        // ---- two graveyards outside the ring (the level-1 graveyard, turned so its gate faces the arena) ----
        for (int gi = 0; gi < 2; gi++)
        {
            int envBefore = env.childCount, lightsBefore = lights.childCount;
            string gname = gi == 0 ? "Arena Graveyard Left" : "Arena Graveyard Right";
            GO = new Vector3(16f, 0f, 12f);   // builds the graveyard centred on the origin
            BuildGraveyard(gname);
            GO = Vector3.zero;
            Transform gy = null;
            for (int c = envBefore; c < env.childCount; c++) if (env.GetChild(c).name == gname) gy = env.GetChild(c);
            if (gy == null) continue;
            var loose = new List<Transform>();
            for (int c = envBefore; c < env.childCount; c++) if (env.GetChild(c) != gy) loose.Add(env.GetChild(c));
            for (int c = lightsBefore; c < lights.childCount; c++) loose.Add(lights.GetChild(c));
            foreach (var t in loose) t.SetParent(gy, true);
            gy.SetPositionAndRotation(S3GraveCentre(gi), Quaternion.Euler(0, gi == 0 ? 90f : -90f, 0));   // gate (local +x) towards the arena
            gy.SetParent(s3, true);
            s3Graveyards[gi] = gy;
            l2Solid.Add(new Vector3(gy.position.x, 15f, gy.position.z));
            // walls round its three closed sides, and a short corridor from the arena gap to its gate
            Vector3[] cnr = { gy.TransformPoint(new Vector3(10.4f, 0, -10.4f)), gy.TransformPoint(new Vector3(-10.4f, 0, -10.4f)),
                              gy.TransformPoint(new Vector3(-10.4f, 0, 10.4f)), gy.TransformPoint(new Vector3(10.4f, 0, 10.4f)) };
            Wall(walls, cnr[0], cnr[1]); Wall(walls, cnr[1], cnr[2]); Wall(walls, cnr[2], cnr[3]);
            float gateZ = gy.TransformPoint(new Vector3(10.4f, 0, 0)).z;
            foreach (float e in new[] { -5f, 5f })
            {
                Vector3 edge = ring((gi == 0 ? 90f : 270f) + e, S3Radius);
                Wall(walls, edge, new Vector3(edge.x, 0, gateZ + (gi == 0 ? 0.4f : -0.4f)));
            }
        }

        // ---- path: straight east from the level-2 start to the arena gate ----
        L2PathRun(new Vector3(4f, 0, L2LaneZ), new Vector3(36f, 0, L2LaneZ), 0.026f);

        // ---- the arena gate, with fence either side across the lane ----
        var gate = Place(Hw("arch_gate"), new Vector3(S3GateX, 0, L2LaneZ), 90, 1.15f, s3);
        gate.name = "Arena Gate";
        Place(Hw("fence"), new Vector3(S3GateX, 0, L2LaneZ + 4.43f), 90, 1f, s3);
        Place(Hw("fence"), new Vector3(S3GateX, 0, L2LaneZ - 4.43f), 90, 1f, s3);
        var ag = gate.AddComponent<Gate>();
        ag.leftLeaf = FindDeep(gate.transform, "arch_gate_left"); ag.rightLeaf = FindDeep(gate.transform, "arch_gate_right");
        ag.requiredKeys = 2; ag.openRadius = 7f;   // needs the two stage-2 keys (well + random enemy)
        int noNav = EnsureLayer("NoNav");
        if (ag.leftLeaf != null) SetLayer(ag.leftLeaf.gameObject, noNav);
        if (ag.rightLeaf != null) SetLayer(ag.rightLeaf.gameObject, noNav);
        PointLight("Arena Gate Glow", new Vector3(S3GateX + 1.5f, 3f, L2LaneZ), new Color(1f, 0.35f, 0.25f), 12f, 12f);

        // ---- arena floor: blocks, with some left as grass ----
        for (int i = -S3Cells; i <= S3Cells; i++)
            for (int j = -S3Cells; j <= S3Cells; j++)
                if (S3Block(i, j))
                    Place(Hw("floor_dirt"), A + new Vector3(4f * i, 0.03f, 4f * j), 90 * rng.Next(4), 1f, s3, false);
        string[] stones = { "path_A", "path_B", "path_C", "path_D" };
        for (int k = 0; k < 60; k++)
        {
            Vector3 p = ring(Rand(0, 360), Rand(1f, S3Radius - 2.5f));
            if (S3OnBlock(p.x, p.z)) Place(Hw(stones[rng.Next(4)]), p + Vector3.up * 0.07f, Rand(0, 360), 1f, s3, false);
        }

        // ---- iron fence round the arena (open at the way in and in front of the house) ----
        float[,] arcs = { { 34f, 84f }, { 96f, 180f - openHalf - 2f }, { 180f + openHalf + 2f, 264f }, { 276f, 326f } };
        for (int k = 0; k < arcs.GetLength(0); k++)
        {
            int n = Mathf.RoundToInt((arcs[k, 1] - arcs[k, 0]) * Mathf.Deg2Rad * (S3Radius + 0.4f) / 4f);
            float step = (arcs[k, 1] - arcs[k, 0]) / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = ring(arcs[k, 0] + step * i, S3Radius + 0.4f), p1 = ring(arcs[k, 0] + step * (i + 1), S3Radius + 0.4f);
                Vector3 d = (p1 - p0).normalized;
                Place(Hw("fence"), (p0 + p1) * 0.5f, Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg, (p1 - p0).magnitude / 4f, s3);
                Place(Hw("fence_pillar"), p0, 0, 1f, s3);
                if (i == n - 1) Place(Hw("fence_pillar"), p1, 0, 1f, s3);
            }
        }

        // ---- one small house on the far side, door to the arena: open, it will hold the final reward ----
        CloneHouse("Small House Arena", S3House, 90, 1f);
        foreach (float e in new[] { -5f, 5f })
        {
            Vector3 edge = ring(e, S3Radius);
            Wall(walls, edge, new Vector3(S3House.x - 4.6f, 0, edge.z));   // short corridor from the ring gap to the door
        }

        l2Solid.Add(new Vector3(S3House.x, 9f, S3House.z));

        // ---- barrels: only 4 big ones, zig-zagging across the arena from the gate side to the house side ----
        float[,] zig = { { -11f, 6f }, { -4f, -6f }, { 4f, 6f }, { 11f, -6f } };
        for (int k = 0; k < 4; k++)
        {
            Vector3 bp = A + new Vector3(zig[k, 0], 0, zig[k, 1]);
            Place(Dg("barrel_large"), bp, Rand(0, 360), 1.7f, s3).name = "Cover Barrel";
            l2Solid.Add(new Vector3(bp.x, 1.5f, bp.z));
        }

        // ---- tombstones in the arena ----
        string[] stones3 = { "gravestone", "grave_A", "gravemarker_A", "gravemarker_B", "grave_B", "grave_A_destroyed" };
        int tombs = 0;
        for (int tries = 0; tries < 400 && tombs < 12; tries++)
        {
            Vector3 tp = ring(Rand(0, 360), Rand(5f, S3Radius - 4f));
            if (S3OnBlock(tp.x, tp.z) || Mathf.Abs(tp.z - A.z) < 3f || !L2Free(tp.x, tp.z, 1.2f)) continue;
            Place(Hw(stones3[rng.Next(stones3.Length)]), tp, Rand(0, 360), 1f, s3);
            l2Solid.Add(new Vector3(tp.x, 0.8f, tp.z));
            tombs++;
        }

        // ---- lights ----
        L2Lantern(new Vector3(16f, 0, 75.5f), new Vector3(16f, 0, L2LaneZ));
        L2Lantern(new Vector3(27f, 0, 68.6f), new Vector3(27f, 0, L2LaneZ));
        foreach (float deg in new[] { 22f, 65f, 112f, 158f, 202f, 248f, 295f, 338f }) L2Lantern(ring(deg, S3Radius - 0.9f), A);
        PointLight("Arena Glow", A + Vector3.up * 8f, new Color(1f, 0.28f, 0.18f), 22f, 34f);

        // ---- trees from the new pack: north of the lane and round the arena ----
        string[] packTrees = { "tree_01", "tree_02", "tree_pine_01" };
        for (float x = 9f; x < 38f; x += 5.5f)
            L2Put("Nature/Vegetation/Trees", packTrees[rng.Next(3)], new Vector3(x + Rand(-1.5f, 1.5f), 0, zOut + Rand(1.8f, 4.5f)), Rand(0, 360), Rand(1.3f, 2f), 0f);
        for (float deg = 12f; deg < 350f; deg += 7f)
        {
            Vector3 p = ring(deg + Rand(-2f, 2f), Rand(S3Radius + 3f, S3Radius + 8f));
            if (S3Walkable(p.x, p.z, 2f) || InPlayArea(p.x, p.z, 1.5f) || !L2Free(p.x, p.z, 1f)) continue;
            if (rng.NextDouble() < 0.6) L2Put("Nature/Vegetation/Trees", packTrees[rng.Next(3)], p, Rand(0, 360), Rand(1.4f, 2.1f), 0f);
            else Place(Hw(rng.Next(2) == 0 ? "tree_dead_large" : "tree_pine_orange_large"), p, Rand(0, 360), Rand(1.3f, 1.8f), s3, false);
        }

        // ---- small plants on the grass blocks and beside the lane ----
        string[] small = { "Nature/Vegetation/Flowers|flower_01", "Nature/Vegetation/Flowers|flower_03", "Nature/Vegetation/Grass|grass_small_01a",
                           "Nature/Vegetation/Grass|grass_small_01b", "Nature/Vegetation/Plants|plant_01", "Nature/Vegetation/Plants|plant_02",
                           "Nature/Rocks|rock_small_01", "Nature/Rocks|rocks_tiny_01" };
        int scattered = 0, attempts = 0;
        while (scattered < 200 && attempts < 8000)
        {
            attempts++;
            float x = Rand(6f, A.x + S3Radius + 1f), z = Rand(A.z - S3Radius - 1f, A.z + S3Radius + 1f);
            if (!S3Walkable(x, z, -0.6f) || !L2Free(x, z, 0.3f)) continue;
            if (x < A.x - S3Radius ? Mathf.Abs(z - L2LaneZ) < 2.7f : S3OnBlock(x, z)) continue;
            string[] pick = small[rng.Next(small.Length)].Split('|');
            L2Put(pick[0], pick[1], new Vector3(x, 0f, z), Rand(0, 360), Rand(0.85f, 1.3f), 0f, pick[1].StartsWith("rock_small"));
            scattered++;
        }

        // ---- grass blades: lane sides and the arena blocks that were left out ----
        GrassPass("Grass Stage 3", "Grass_S3_", 3200, 5.4f, A.x + S3Radius + 1f, A.z - S3Radius - 1f, A.z + S3Radius + 1f,
                  (x, z) => S3Walkable(x, z, 0.4f) && L2Free(x, z, 0f) && (x < A.x - S3Radius - 1f ? Mathf.Abs(z - L2LaneZ) > 2.4f : !S3OnBlock(x, z)));

        l2 = level2Parent;
        rng = levelOneRng;
    }

    // =====================================================================
    // STAGE 2 ENEMIES (level 2). Built hidden under "Level 2 Enemies"; switched on when level 2 begins.
    // Two gunmen on the market awnings, one on the ground by the small shed (he drops the key at the well),
    // a big enemy at each of the three main houses, a group of 7-9 that bursts out of building_03,
    // and small enemies along the straight path to the arena. One random enemy carries the second key.
    // =====================================================================

    static GameObject l2AwningA, l2AwningB, l2Shed, l2House03, l2House05, l2Well;
    static Vector3 l2CrawlerSpot;

    // Shield Skeleton: big shield in the left hand blocks bullets from the front; turns slowly so it can be flanked
    static GameObject MakeShield(Vector3 pos, float yaw, int enemyLayer)
    {
        var e = Skeleton("Skeleton_Warrior", pos, yaw, "Blocking", 0f, "Skeleton_Blade");
        if (e == null) return null;
        var handL = FindDeep(e.transform, "handslot.l");
        var shield = AssetDatabase.LoadAssetAtPath<GameObject>(KK + "Skeletons/Weapons/Skeleton_Shield_Large_A.gltf");
        if (handL != null && shield != null)
        {
            var sh = (GameObject)PrefabUtility.InstantiatePrefab(shield);
            sh.transform.SetParent(handL, false);
            sh.transform.localPosition = Vector3.zero; sh.transform.localEulerAngles = new Vector3(0, 180, 0);
        }
        e.name = "Enemy_ShieldSkeleton";
        SetupEnemy(e, enemyLayer);
        var ai = e.GetComponent<EnemyAI>();
        ai.kind = EnemyAI.Kind.Shield;
        ai.minHits = 6; ai.maxHits = 8;
        ai.chaseSpeed = 2.5f;
        ai.noticeRadius = 15f;
        e.GetComponent<NavMeshAgent>().angularSpeed = 35f;   // very slow to turn: walk round it
        return e;
    }

    // Bone Thrower: keeps its distance and lobs slow bones at where the player stands
    static GameObject MakeThrower(Vector3 pos, float yaw, int enemyLayer)
    {
        var e = Skeleton("Skeleton_Rogue", pos, yaw, "Idle", 0f, null);
        if (e == null) return null;
        e.name = "Enemy_BoneThrower";
        SetupEnemy(e, enemyLayer);
        var ai = e.GetComponent<EnemyAI>();
        ai.kind = EnemyAI.Kind.Thrower;
        ai.minHits = 4; ai.maxHits = 5;
        ai.chaseSpeed = 2.8f;
        ai.noticeRadius = 18f;
        ai.boneMaterial = Mat("FX_Bone", new Color(0.93f, 0.9f, 0.8f), 0.2f, 0f, new Color(0.45f, 0.42f, 0.35f));
        return e;
    }


    static void BuildStage2Enemies(GameManager gm, int enemyLayer)
    {
        var group = new GameObject("Level 2 Enemies");
        Vector3 K = L2Centre;
        System.Action<GameObject> adopt = go => { if (go != null) go.transform.SetParent(group.transform, true); };
        System.Func<string, GameObject> lastNamed = name => { GameObject f = null; foreach (Transform c in chars) if (c.name == name) f = c.gameObject; return f; };

        // ---- the hamlet: Bone Throwers where the gunmen stood, Shield Skeletons at the three main houses ----
        // (the old stage-2 enemies - gunmen, big enemies, the door group - now live in level 1)
        Vector3 wellPos = l2Well != null ? l2Well.transform.position : K;
        foreach (var aw in new[] { l2AwningA, l2AwningB, l2Shed })
        {
            if (aw == null) continue;
            Vector3 c = aw.transform.position, toK = K - c; toK.y = 0;
            Vector3 p = c + toK.normalized * 3.2f;            // on the ground in front of the stall / shed
            adopt(MakeThrower(p, L2Face(p, K), enemyLayer));
        }
        for (int i = 0; i < L2BuildingNames.Length; i++)
        {
            string n = L2BuildingNames[i];
            if (n != "building_05" && n != "building_01" && n != "building_04") continue;
            Vector3 p = L2Ring(L2Buildings[i, 0] + 7f, L2Radius - 4.5f);   // beside the path to the door
            adopt(MakeShield(p, L2Face(p, K), enemyLayer));
        }
        if (l2House03 != null)
        {
            Vector3 h = l2House03.transform.position, toK = (K - h); toK.y = 0; toK.Normalize();
            Vector3 door = h + toK * 5.5f, side = new Vector3(-toK.z, 0, toK.x);
            adopt(MakeShield(door + side * 1.4f, L2Face(door, K), enemyLayer));
            adopt(MakeShield(door - side * 1.4f, L2Face(door, K), enemyLayer));
        }
        adopt(MakeThrower(wellPos + (K - wellPos).normalized * 4f + Vector3.right * 2f, 0f, enemyLayer));
        // both stage-2 keys are carried: one by a random Shield Skeleton, one by a random Bone Thrower
        gm.level2ThrowerKey = MakePickup("Key (Thrower)", Pickup.Kind.Key, 1, new Vector3(0, KeyHeight, 0), false);

        // ---- the straight path of stage 2 (west from the gate): 4 cover barrels (2 each side) and small enemies ----
        // (at the far end, so they do not attack the moment level 2 begins)
        float[,] barrels = { { -22f, 75.4f }, { -32f, 75.4f }, { -19f, 68.6f }, { -30f, 68.6f } };
        for (int i = 0; i < 4; i++)
            Place(Dg("barrel_large"), new Vector3(barrels[i, 0], 0, barrels[i, 1]), Rand(0, 360), 1.7f, props).name = "Cover Barrel";
        // a Bone Thrower at the far end behind the barrels, a Shield Skeleton coming down the lane
        adopt(MakeThrower(new Vector3(-36.5f, 0, 73.4f), 90f, enemyLayer));
        adopt(MakeShield(new Vector3(-29f, 0, 72f), 90f, enemyLayer));

        // ---- the village road (second lane): 2 barrels on its edges and 3 small enemies ----
        {
            Vector3 dr = (K - L2Corner); dr.y = 0; dr.Normalize();
            Vector3 lr = new Vector3(-dr.z, 0, dr.x);
            Place(Dg("barrel_large"), L2Corner + dr * 9f + lr * 3.4f, Rand(0, 360), 1.7f, props).name = "Cover Barrel";
            Place(Dg("barrel_large"), L2Corner + dr * 16f - lr * 3.4f, Rand(0, 360), 1.7f, props).name = "Cover Barrel";
            float faceOut = Mathf.Atan2(-dr.x, -dr.z) * Mathf.Rad2Deg;
            adopt(MakeShield(L2Corner + dr * 11f + lr * 1.2f, faceOut, enemyLayer));
            adopt(MakeThrower(L2Corner + dr * 18f - lr * 1f, faceOut, enemyLayer));
            // pickups for stage 2: ammo and health in the village and along both lanes
            float[,] ammo2 = { { 75f, 17f }, { 135f, 9f }, { 205f, 18f }, { 255f, 14f }, { 15f, 15f } };
            for (int i = 0; i < ammo2.GetLength(0); i++)
                MakePickup("Ammo", Pickup.Kind.Ammo, 12, L2Ring(ammo2[i, 0], ammo2[i, 1]) + Vector3.up * 0.6f, true);
            float[,] health2 = { { 118f, 19f }, { 225f, 8f }, { 335f, 17f }, { 165f, 15f }, { 270f, 19f }, { 45f, 8f } };
            for (int i = 0; i < health2.GetLength(0); i++)
                MakePickup("Health", Pickup.Kind.Health, 2, L2Ring(health2[i, 0], health2[i, 1]) + Vector3.up * 0.7f, true);
            MakePickup("Ammo", Pickup.Kind.Ammo, 12, new Vector3(-14f, 0.6f, 72f), true);
            MakePickup("Health", Pickup.Kind.Health, 2, new Vector3(-6f, 0.7f, 74.6f), true);
            MakePickup("Health", Pickup.Kind.Health, 2, L2Corner + dr * 5f + lr * 2f + Vector3.up * 0.7f, true);
            MakePickup("Ammo", Pickup.Kind.Ammo, 12, L2Corner + dr * 14f + Vector3.up * 0.6f, true);
        }

        // ---- stage 3: the two graveyards beside the arena raise enemies, slowly, once the player is inside ----
        var arenaSpawners = new List<GraveyardSpawner>();
        for (int gi = 0; gi < s3Graveyards.Length; gi++)
        {
            var gy = s3Graveyards[gi];
            if (gy == null) continue;
            var pts = new List<Vector3>();
            foreach (Transform c in gy) if (c.name.StartsWith("grave")) pts.Add(c.position + gy.TransformDirection(Vector3.right * 1.2f));
            if (pts.Count == 0) continue;
            var tpl = Skeleton("Skeleton_Minion", gy.position, 0, "Idle", 0f, null);
            tpl.name = "ArenaSpawnTemplate";
            SetupEnemy(tpl, enemyLayer);
            tpl.SetActive(false);
            adopt(tpl);
            var asp = new GameObject(gy.name + " Spawner").AddComponent<GraveyardSpawner>();
            asp.transform.position = gy.position;
            asp.template = tpl;
            asp.points = pts.ToArray();
            asp.manual = true;
            asp.delay = 8f;                 // slow: gunmen will be added here later
            asp.minCount = 4; asp.maxCount = 5;   // fewer: gunmen will join on the house roof later
            asp.message = gi == 0 ? "The dead are rising beside the arena!" : "";
            adopt(asp.gameObject);
            arenaSpawners.Add(asp);
        }
        gm.arenaSpawners = arenaSpawners.ToArray();
        // two ammo boxes inside each arena graveyard, just inside its gate
        foreach (var gy in s3Graveyards)
        {
            if (gy == null) continue;
            MakePickup("Ammo (Graveyard)", Pickup.Kind.Ammo, AmmoPerPickup, gy.TransformPoint(new Vector3(7.6f, 0f, 3.2f)) + Vector3.up * 0.6f, true);
        }
        gm.finalKey = MakePickup("Final Key", Pickup.Kind.FinalKey, 1, new Vector3(S3House.x, 2.6f, S3House.z), false);
        {
            // twice the size of the other keys, lying flat (the house is too low for it to stand up)
            var fk = gm.finalKey.transform;
            foreach (Transform c in fk) if (c.GetComponentInChildren<MeshFilter>(true) != null) { c.localPosition = Vector3.zero; c.localEulerAngles = Vector3.zero; }
            fk.localScale = Vector3.one * 1.7f;   // about 4 m long
            fk.position = new Vector3(S3House.x, 2.2f, S3House.z);
            var glow = fk.GetComponentInChildren<Light>(true);
            if (glow != null) { glow.intensity = 14f; glow.range = 9f; }
            gm.finalKey.SetActive(false);
        }
        // stage 3 pickups: 3 health, only 2 ammo
        foreach (float ha in new[] { 125f, 235f, 330f, 30f, 180f, 285f }) MakePickup("Health", Pickup.Kind.Health, 2, S3Arena + new Vector3(Mathf.Cos(ha * Mathf.Deg2Rad), 0, Mathf.Sin(ha * Mathf.Deg2Rad)) * 17f + Vector3.up * 0.7f, true);
        foreach (float aa in new[] { 60f }) MakePickup("Ammo", Pickup.Kind.Ammo, 12, S3Arena + new Vector3(Mathf.Cos(aa * Mathf.Deg2Rad), 0, Mathf.Sin(aa * Mathf.Deg2Rad)) * 11f + Vector3.up * 0.6f, true);

        // ---- the bigger hamlet: a mix of both new kinds on the right of the way in, and two behind the gatehouse ----
        Vector3 dIn = (K - L2Corner); dIn.y = 0; dIn.Normalize();
        Vector3 sideIn = new Vector3(-dIn.z, 0, dIn.x);
        var extra = new List<Vector3> { L2Ring(122f, 15f), L2Ring(178f, 17f), K - dIn * 9.5f + sideIn * 3f, K - dIn * 9.5f - sideIn * 3f };
        for (int i = 0; i < extra.Count; i++)
            adopt(i % 2 == 0 ? MakeShield(extra[i], L2Face(extra[i], K) + 180f, enemyLayer) : MakeThrower(extra[i], L2Face(extra[i], K) + 180f, enemyLayer));
        // the stage-2 crawler: springs on the player just inside the gatehouse
        l2CrawlerSpot = K - dIn * 13f;
        // ---- the second key: carried by one random enemy (picked when level 2 begins) ----
        gm.level2EnemyKey = MakePickup("Key (Enemy)", Pickup.Kind.Key, 1, new Vector3(0, KeyHeight, 0), false);
        gm.level2Enemies = group;
        group.SetActive(false);
    }

    // =====================================================================
    // THE BOSS: the KayKit Barbarian, scaled up, with a Skeleton Warrior's skull, jaw and glowing eyes,
    // glowing red, swinging the big two-handed axe. Hidden until the final key is taken.
    // =====================================================================

    const float BossScale = 2.6f;    // THE CREATURE KNIGHT
    const float Boss2Scale = 3.4f;
    const int AmmoPerPickup = 18;   // every ammo box gives at least this many bullets   // THE REAL CREATURE

    // Gives every material of a boss a glowing copy (its own colours plus a tint); the eyes glow hard.
    static void BossGlow(GameObject boss, string prefix, Color body, Color eyes)
    {
        var copies = new Dictionary<Material, Material>();
        foreach (var r in boss.GetComponentsInChildren<Renderer>(true))
        {
            bool isEyes = r.name.EndsWith("_Eyes");
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (isEyes || !copies.TryGetValue(src, out Material m))
                {
                    string mp = MatDir + prefix + (isEyes ? "Eyes" : src.name) + ".mat";
                    m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                    if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, mp); }
                    else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                    m.SetTexture("emissiveTexture", src.GetTexture("baseColorTexture"));
                    m.SetColor("emissiveFactor", isEyes ? eyes : body);
                    EditorUtility.SetDirty(m);
                    if (!isEyes) copies[src] = m;
                }
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
    }

    // A transparent red circle laid on the ground to warn of a boss attack.
    static Renderer BossWarningDisc(string name)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        string wp = MatDir + "FX_BossWarning.mat";
        var wm = AssetDatabase.LoadAssetAtPath<Material>(wp);
        if (wm == null) { wm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(wm, wp); }
        wm.SetFloat("_Surface", 1f); wm.SetFloat("_Blend", 0f);
        wm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); wm.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); wm.SetFloat("_ZWrite", 0f);
        wm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        wm.renderQueue = (int)RenderQueue.Transparent;
        wm.SetColor("_BaseColor", new Color(1f, 0.08f, 0.04f, 0.4f));
        EditorUtility.SetDirty(wm);
        var dr = disc.GetComponent<Renderer>();
        dr.sharedMaterial = wm; dr.shadowCastingMode = ShadowCastingMode.Off;
        disc.SetActive(false);
        return dr;
    }

    // Collider, walking and the fight script, shared by both bosses.
    static BossAI BossBody(GameObject boss, int enemyLayer, Color lampColour, float stopDistance)
    {
        var lg = new GameObject("Boss Glow").AddComponent<Light>();
        lg.transform.SetParent(boss.transform, false);
        lg.transform.localPosition = new Vector3(0, 1.3f, 0.4f);
        lg.type = LightType.Point; lg.color = lampColour; lg.intensity = 25f; lg.range = 11f;
        SetLayer(boss, enemyLayer);
        var cap = boss.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 1.15f, 0); cap.height = 2.3f; cap.radius = 0.6f;
        boss.AddComponent<Rigidbody>().isKinematic = true;
        var ag = boss.AddComponent<NavMeshAgent>();
        ag.enabled = false;
        ag.radius = 0.45f; ag.height = 2.2f; ag.angularSpeed = 240f; ag.acceleration = 14f; ag.stoppingDistance = stopDistance;
        var ai = boss.AddComponent<BossAI>();
        ai.glowLamp = lg; ai.glowLight = lampColour;
        return ai;
    }

    // ---- THE CREATURE KNIGHT: the Barbarian, scaled up and headless, glowing red, two-handed axe ----
    static void BuildBoss(GameManager gm, int enemyLayer)
    {
        var keepRng = rng; rng = new System.Random(6060);
        string bpath = KK + "Adventurers/Barbarian.glb";
        var boss = Place(bpath, S3Arena + new Vector3(4f, 0, 0), 90, BossScale, null, false);
        if (boss == null) { rng = keepRng; return; }
        boss.name = "Boss_CreatureKnight";
        PrefabUtility.UnpackPrefabInstance(boss, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Pose(boss, bpath, "2H_Melee_Idle", 0f);
        // headless (a green health bar stands where his head was); only the big two-handed axe and the cape as gear
        foreach (var n in new[] { "1H_Axe_Offhand", "Barbarian_Round_Shield", "1H_Axe", "Mug", "Barbarian_Head", "Barbarian_Hat" })
        {
            var t = FindDeep(boss.transform, n);
            if (t != null) t.gameObject.SetActive(false);
        }
        BossGlow(boss, "Boss_", new Color(0.35f, 0.12f, 0.08f), new Color(6f, 0.5f, 0.15f));

        var ai = BossBody(boss, enemyLayer, new Color(1f, 0.3f, 0.12f), 3.4f);
        ai.bossName = "THE CREATURE KNIGHT";
        ai.introLine = "Who dares to wake me? I will bury you with the rest of this village.";
        ai.summonLine = "Rise, my servants! Tear this one apart!";
        ai.enragedLine = "ENOUGH! Now you will feel my true fury!";
        ai.deathLine = "This... is not the end. The REAL creature... is coming...";
        ai.playerDeadLine = "Another soul for my graveyard.";
        ai.taunts = new[] { "Run, little one. The dead are patient.", "Your bullets only make me angry.", "This arena will be your grave.",
                            "I have waited a hundred years for this night.", "Stand still and it will be quick." };
        // a glowing green health bar where his head was: it follows the neck and always faces the camera
        var headBone = FindDeep(boss.transform, "head");
        if (headBone != null)
        {
            const float barW = 2.0f, barH = 0.5f, barD = 0.3f;
            var bar = new GameObject("Head Health Bar").transform;
            bar.position = headBone.position + Vector3.up * 0.42f;
            bar.rotation = boss.transform.rotation;
            var back = Prim(PrimitiveType.Cube, "Bar Back", Vector3.zero, new Vector3(barW + 0.16f, barH + 0.16f, barD), Vector3.zero,
                            Mat("Boss_HeadBar_Back", new Color(0.03f, 0.04f, 0.03f), 0.3f), bar, false);
            var fill = Prim(PrimitiveType.Cube, "Bar Fill", new Vector3(0, 0, -0.03f), new Vector3(barW, barH, barD + 0.04f), Vector3.zero,
                            Mat("Boss_HeadBar_Fill", new Color(0.1f, 0.75f, 0.15f), 0.4f, 0f, new Color(0.05f, 0.7f, 0.1f)), bar, false);
            foreach (var r in bar.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            bar.SetParent(headBone, true);
            SetLayer(bar.gameObject, enemyLayer);
            ai.headBar = bar; ai.headBarFill = fill.transform; ai.headBarWidth = barW;
        }
        ai.telegraph = BossWarningDisc("Knight Warning");
        boss.transform.SetParent(chars, true);
        boss.SetActive(false);
        gm.boss = ai;

        // health back to full and plenty of ammo, laid out in the middle of the arena when the knight dies
        var reward = new GameObject("Knight Reward");
        reward.transform.SetParent(props, false);
        MakePickup("Health (Full)", Pickup.Kind.Health, 7, S3Arena + new Vector3(-2f, 0.7f, 0f), true).transform.SetParent(reward.transform, true);
        MakePickup("Health (Full)", Pickup.Kind.Health, 7, S3Arena + new Vector3(2f, 0.7f, 0f), true).transform.SetParent(reward.transform, true);
        foreach (var off in new[] { new Vector3(0, 0.6f, 2.5f), new Vector3(0, 0.6f, -2.5f), new Vector3(-4f, 0.6f, 0f), new Vector3(4f, 0.6f, 0f) })
            MakePickup("Ammo", Pickup.Kind.Ammo, 24, S3Arena + off, true).transform.SetParent(reward.transform, true);
        reward.SetActive(false);
        gm.knightReward = reward;

        // the arena house door: shut once the player has come out to fight
        var block = new GameObject("House Door Block");
        block.transform.SetParent(env, false);
        Vector3 e0 = S3Arena + new Vector3(Mathf.Cos(-6f * Mathf.Deg2Rad), 0, Mathf.Sin(-6f * Mathf.Deg2Rad)) * S3Radius;
        Vector3 e1 = S3Arena + new Vector3(Mathf.Cos(6f * Mathf.Deg2Rad), 0, Mathf.Sin(6f * Mathf.Deg2Rad)) * S3Radius;
        block.transform.position = (e0 + e1) * 0.5f;
        var keepL2 = l2; l2 = block.transform;
        L2FenceRun(e0, e1, new[] { "fence_wood_02a", "fence_wood_02b", "fence_wood_02c" }, 2f, true, 1.4f);
        l2 = keepL2;
        block.SetActive(false);
        gm.houseBlock = block;
        gm.houseDoorX = S3House.x - 5.2f;
        rng = keepRng;
    }

    // ---- THE REAL CREATURE: a giant Skeleton Warrior, very slow and very strong, glowing cold blue ----
    static void BuildBoss2(GameManager gm, int enemyLayer)
    {
        var keepRng = rng; rng = new System.Random(7070);
        var boss = Skeleton("Skeleton_Warrior", S3Arena + new Vector3(6f, 0, 0), 270, "Skeleton_Inactive_Standing_Pose", 0f, "Skeleton_Axe", Boss2Scale);
        if (boss == null) { rng = keepRng; return; }
        boss.name = "Boss_RealCreature";
        PrefabUtility.UnpackPrefabInstance(boss, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var handL = FindDeep(boss.transform, "handslot.l");
        var shield = AssetDatabase.LoadAssetAtPath<GameObject>(KK + "Skeletons/Weapons/Skeleton_Shield_Large_A.gltf");
        if (handL != null && shield != null)
        {
            var sh = (GameObject)PrefabUtility.InstantiatePrefab(shield);
            sh.transform.SetParent(handL, false);
            sh.transform.localPosition = Vector3.zero; sh.transform.localEulerAngles = new Vector3(0, 180, 0);
        }
        BossGlow(boss, "Boss2_", new Color(0.05f, 0.12f, 0.3f), new Color(0.4f, 2.5f, 6f));

        var ai = BossBody(boss, enemyLayer, new Color(0.35f, 0.6f, 1f), 4.2f);
        ai.bossName = "THE REAL CREATURE";
        ai.introLine = "You killed my knight. Now you face the REAL creature.";
        ai.summonLine = "Dead of this village, come to me!";
        ai.enragedLine = "I AM THE CREATURE! NO ONE LEAVES THIS ARENA ALIVE!";
        ai.deathLine = "Impossible... the village... is free...";
        ai.playerDeadLine = "The village is mine. Your soul is next.";
        ai.taunts = new[] { "I can smell your fear.", "Every step you take, I come closer.", "Keep running. It only makes it sweeter.",
                            "Your village belongs to me now.", "You cannot kill what is already dead." };
        ai.maxHealth = 90;
        ai.slamDamage = 4; ai.spinDamage = 3;
        ai.chaseSpeed = new[] { 1.3f, 1.5f, 2.3f };       // very slow; a little faster when enraged (player walks 3.6, runs 6.2)
        ai.attackPause = new[] { 2.6f, 2.2f, 1.8f };
        ai.windUpSpeed = new[] { 0.45f, 0.5f, 0.6f };
        ai.attackRange = 6f; ai.slamReach = 3.6f; ai.slamRadius = 3.8f; ai.spinRadius = 5.6f;
        ai.summonCount = 5;
        ai.introClip = "Skeletons_Awaken_Standing";
        ai.walkClip = "Walking_D_Skeletons"; ai.runClip = "Walking_A";
        ai.walkAnimPace = 3.4f; ai.runAnimPace = 3.6f;
        ai.deathClip = "Death_C_Skeletons";
        ai.glowColour = new[] { new Color(0.05f, 0.12f, 0.3f), new Color(0.08f, 0.2f, 0.45f), new Color(0.15f, 0.35f, 0.8f) };
        ai.telegraph = BossWarningDisc("Real Creature Warning");
        // one big skeleton (like the big enemies of the village) rises with the small ones in his second phase
        var big = Skeleton("Skeleton_Warrior", S3Arena + new Vector3(0, -40f, 0), 0, "Idle", 0f, "Skeleton_Blade", Big);
        if (big != null)
        {
            big.name = "BossBigSummonTemplate";
            SetupEnemy(big, enemyLayer);
            big.transform.SetParent(null, true);
            big.SetActive(false);
            ai.bigSummon = big;
        }
        ai.enragedHelper = BuildSoulGunner(enemyLayer);
        boss.transform.SetParent(chars, true);
        boss.SetActive(false);
        gm.boss2 = ai;
        rng = keepRng;
    }

    // THE REAL CREATURE's enraged phase: a glowing skeleton gunman rises on the arena house roof. His shots are
    // slow glowing bullets aimed where the player stands, so they can be dodged in the open (see SoulGunner).
    static GameObject BuildSoulGunner(int enemyLayer)
    {
        const float ridgeY = 7.5f;   // top of the arena house roof; the ridge runs north-south
        var g = Skeleton("Skeleton_Mage", new Vector3(S3House.x, ridgeY, S3House.z - 4.5f), 270, "Idle", 0f, null);
        if (g == null) return null;
        g.name = "Enemy_SoulGunner";
        BossGlow(g, "SoulGunner_", new Color(0.04f, 0.1f, 0.25f), new Color(0.4f, 2.5f, 6f));
        SetLayer(g, enemyLayer);
        var cap = g.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 1.15f, 0); cap.height = 2.3f; cap.radius = 0.6f;
        var gun = BuildHandgun(FindDeep(g.transform, "handslot.r"));
        SetLayer(gun, enemyLayer);
        var sg = g.AddComponent<SoulGunner>();
        sg.muzzle = FindDeep(gun.transform, "Muzzle");
        sg.pointA = new Vector3(S3House.x, ridgeY, S3House.z - 4.8f);
        sg.pointB = new Vector3(S3House.x, ridgeY, S3House.z + 4.8f);
        var house = env.Find("Small House Arena");
        sg.ownBuilding = house;
        sg.bulletMaterial = Mat("FX_SoulBullet", new Color(0.45f, 0.85f, 1f), 0f, 0f, new Color(1.6f, 4.5f, 9f));
        var lamp = new GameObject("Soul Glow").AddComponent<Light>();
        lamp.transform.SetParent(g.transform, false);
        lamp.transform.localPosition = new Vector3(0, 1.6f, 0.6f);
        lamp.type = LightType.Point; lamp.color = new Color(0.35f, 0.65f, 1f); lamp.intensity = 14f; lamp.range = 6f;
        g.SetActive(false);
        return g;
    }

    // ---------- characters ----------

    // =====================================================================
    // LEVEL 1 EXTRAS: the enemies that used to be in stage 2, moved here. Three gunmen on small roofs on the
    // left and right of the way through, the three big enemies at those houses, the door group that bursts out
    // of the red house in the village square, and the small enemies along the side loop and the square.
    // =====================================================================
    static void BuildLevel1Extras(int enemyLayer)
    {
        var keepRng = rng; rng = new System.Random(4242);   // own sequence: everything else is built as before
        var root = new GameObject("Level 1 Extra Enemies").transform;
        root.SetParent(chars, true);
        System.Action<GameObject> adopt = go => { if (go != null) go.transform.SetParent(root, true); };

        // ---- gunmen: Small House Start (left of the road), red house A (right), red house B (right, in the square) ----
        var roofs = new[] { ("Small House Start", 90f), ("building_home_A_red", 270f), ("building_home_B_red", 270f) };
        int gi = 0;
        foreach (var (houseName, yaw) in roofs)
        {
            var house = env.Find(houseName);
            if (house == null || !RoofLine(house.gameObject, 0.25f, out Vector3 r0, out Vector3 r1)) continue;
            string gname = "Enemy_Gunman_L1_" + (++gi);
            MakeGunman(gname, (r0 + r1) * 0.5f + Vector3.up * 0.5f, yaw, r0, r1, house, null, 30f, enemyLayer);
            var g = chars.Find(gname);
            if (g != null) adopt(g.gameObject);
        }

        // ---- the three big enemies, in front of those houses ----
        float[,] bigs = { { -4f, -36f, 90f }, { 12f, -31f, 270f }, { 11f, 28f, 270f } };
        for (int i = 0; i < bigs.GetLength(0); i++)
        {
            var e = Skeleton(i % 2 == 0 ? "Skeleton_Warrior" : "Skeleton_Rogue", new Vector3(bigs[i, 0], 0, bigs[i, 1]), bigs[i, 2], "Idle_Combat", 0.3f * i,
                             i % 2 == 0 ? "Skeleton_Axe" : "Skeleton_Blade", Big);
            if (e == null) continue;
            SetupEnemy(e, enemyLayer);
            adopt(e);
        }

        // ---- the red house in the square: its door bursts open and 7-9 small enemies run out ----
        {
            Vector3 door = new Vector3(9f, 0f, 24f), toK = Vector3.left, side = Vector3.forward;
            var template = Skeleton("Skeleton_Minion", door, 270f, "Idle", 0f, null);
            template.name = "L1HouseSpawnTemplate";
            SetupEnemy(template, enemyLayer);
            template.SetActive(false);
            adopt(template);
            var sp = new GameObject("Red House Spawner").AddComponent<GraveyardSpawner>();
            sp.transform.SetParent(root, false);
            sp.transform.position = door;
            sp.template = template;
            sp.fromHouse = true;
            sp.delay = 1.1f;
            sp.minCount = 7; sp.maxCount = 9;
            sp.triggerRadius = 11f;
            sp.message = "The door bursts open!";
            sp.chaseSpeed = 4.6f;
            sp.points = new[] { door, door + side * 1.2f, door - side * 1.2f, door + toK * 1.2f, door + toK * 1.2f + side, door + toK * 1.2f - side };
            PointLight("Red House Door Glow", door + Vector3.up * 2.2f + Vector3.right * 0.6f, new Color(1f, 0.3f, 0.2f), 6f, 7f).transform.SetParent(root, true);
        }

        // ---- small enemies: round the side loop and in the village square ----
        float[,] small = { { 10f, -38f }, { 20f, -38f }, { 30f, -38f }, { 40f, -30f }, { 40f, -20f }, { 40f, -10f }, { 30f, -2f }, { 20f, -2f },
                           { 18f, -24f }, { 17f, -36f }, { -20f, 32f }, { -16f, 58f }, { 20f, 32f }, { 22f, 52f } };
        for (int i = 0; i < small.GetLength(0); i++)
        {
            var e = Skeleton(i % 3 == 2 ? "Skeleton_Rogue" : "Skeleton_Minion", new Vector3(small[i, 0], 0, small[i, 1]), Rand(0, 360), "Walking_D_Skeletons", 0.15f * i,
                             i % 3 == 2 ? "Skeleton_Blade" : null);
            if (e == null) continue;
            SetupEnemy(e, enemyLayer);
            adopt(e);
        }
        rng = keepRng;
    }

    // =====================================================================
    // SPECIAL ENEMIES: the crawler (one jump scare per level) and the exploder that guards every key.
    // =====================================================================
    static void BuildSpecialEnemies(GameManager gm, int enemyLayer)
    {
        var keepRng = rng; rng = new System.Random(5353);
        var root = new GameObject("Special Enemies").transform;

        // crawler: fast, dies to one bullet, bursts out of the ground beside the player
        var crawler = Skeleton("Skeleton_Minion", new Vector3(0, -40f, 0), 0, "Idle", 0f, null);
        crawler.name = "CrawlerTemplate";
        SetupEnemy(crawler, enemyLayer);
        var cai = crawler.GetComponent<EnemyAI>();
        cai.kind = EnemyAI.Kind.Crawler;
        cai.minHits = cai.maxHits = 1;
        cai.chaseSpeed = 5.4f; cai.riseSpeed = 3.2f;
        cai.ammoDropChance = 0f;
        cai.minPainCries = cai.maxPainCries = 1;
        crawler.transform.SetParent(root, true);
        crawler.SetActive(false);
        System.Func<string, Vector3, AmbushTrigger> trap = (name, at) =>
        {
            var t = new GameObject(name).AddComponent<AmbushTrigger>();
            t.transform.SetParent(root, false);
            t.transform.position = at;
            t.template = crawler;
            return t;
        };
        trap("Crawler Level 1", new Vector3(0.5f, 0f, -23f));      // on the road, between the cover barrels
        trap("Crawler Stage 2", l2CrawlerSpot);                     // just inside the hamlet's gatehouse
        var s3 = trap("Crawler Stage 3", S3Arena);                  // silent: rises with the arena graveyard dead
        s3.manual = true; s3.silent = true;
        gm.stage3Crawler = s3;

        // exploder: glowing red, bursts after a 1-second warning; one appears beside every key
        var ex = Skeleton("Skeleton_Minion", new Vector3(0, -40f, 0), 0, "Idle", 0f, null);
        ex.name = "ExploderTemplate";
        PrefabUtility.UnpackPrefabInstance(ex, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        BossGlow(ex, "Exploder_", new Color(0.55f, 0.08f, 0.02f), new Color(6f, 1.2f, 0.2f));
        SetupEnemy(ex, enemyLayer);
        var eai = ex.GetComponent<EnemyAI>();
        eai.kind = EnemyAI.Kind.Exploder;
        eai.minHits = eai.maxHits = 2;
        eai.chaseSpeed = 4.0f;
        eai.noticeRadius = 12f;
        eai.ammoDropChance = 0.25f;
        eai.warningMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "FX_BossWarning.mat");
        var glow = new GameObject("Exploder Glow").AddComponent<Light>();
        glow.transform.SetParent(ex.transform, false);
        glow.transform.localPosition = new Vector3(0, 1.3f, 0.3f);
        glow.type = LightType.Point; glow.color = new Color(1f, 0.3f, 0.08f); glow.intensity = 10f; glow.range = 5f;
        ex.transform.SetParent(root, true);
        ex.SetActive(false);
        gm.exploderTemplate = ex;
        rng = keepRng;
    }

    static GameMenu.IntroLine Line(float at, float length, string text) { return new GameMenu.IntroLine { at = at, length = length, text = text }; }

    static GameMenu.Shot MenuShot(string caption, Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float seconds)
    {
        return new GameMenu.Shot { caption = caption, from = from, to = to, lookFrom = lookFrom, lookTo = lookTo, seconds = seconds };
    }

    static GameObject BuildHandgun(Transform hand)
    {
        var metal = Mat("Gun_Metal", new Color(0.13f, 0.13f, 0.15f), 0.55f, 0.75f);
        var grip = Mat("Gun_Grip", new Color(0.25f, 0.15f, 0.09f), 0.2f);
        var gun = new GameObject("Handgun");
        gun.transform.SetParent(hand, false);
        gun.transform.localPosition = new Vector3(0.02f, 0.02f, 0f);
        gun.transform.localEulerAngles = new Vector3(0, 270, 0);
        Prim(PrimitiveType.Cube, "Slide", new Vector3(0, 0.12f, 0.22f), new Vector3(0.11f, 0.13f, 0.66f), Vector3.zero, metal, gun.transform, false);
        Prim(PrimitiveType.Cube, "Frame", new Vector3(0, 0.035f, 0.16f), new Vector3(0.095f, 0.06f, 0.46f), Vector3.zero, metal, gun.transform, false);
        Prim(PrimitiveType.Cube, "Grip", new Vector3(0, -0.11f, -0.05f), new Vector3(0.1f, 0.32f, 0.16f), new Vector3(-14, 0, 0), grip, gun.transform, false);
        Prim(PrimitiveType.Cylinder, "Muzzle", new Vector3(0, 0.125f, 0.56f), new Vector3(0.06f, 0.02f, 0.06f), new Vector3(90, 0, 0), metal, gun.transform, false);
        Prim(PrimitiveType.Cube, "Sight", new Vector3(0, 0.2f, 0.5f), new Vector3(0.025f, 0.035f, 0.04f), Vector3.zero, metal, gun.transform, false);
        Prim(PrimitiveType.Cube, "Guard", new Vector3(0, -0.035f, 0.13f), new Vector3(0.04f, 0.02f, 0.2f), Vector3.zero, metal, gun.transform, false);
        Prim(PrimitiveType.Cube, "GuardFront", new Vector3(0, -0.005f, 0.225f), new Vector3(0.04f, 0.075f, 0.02f), Vector3.zero, metal, gun.transform, false);
        return gun;
    }

    static GameObject Skeleton(string type, Vector3 pos, float rotY, string clip, float time, string weapon, float scale = 1f)
    {
        string path = KK + "Skeletons/" + type + ".glb";
        var s = Place(path, pos, rotY, scale, chars, false);
        if (s == null) return null;
        s.name = "Enemy_" + type;
        Pose(s, path, clip, time);
        if (weapon != null)
        {
            var hand = FindDeep(s.transform, "handslot.r");
            var wp = AssetDatabase.LoadAssetAtPath<GameObject>(KK + "Skeletons/Weapons/" + weapon + ".gltf");
            if (hand != null && wp != null)
            {
                var w = (GameObject)PrefabUtility.InstantiatePrefab(wp);
                w.transform.SetParent(hand, false);
                w.transform.localPosition = Vector3.zero;
                w.transform.localEulerAngles = new Vector3(0, 180, 0);
            }
        }
        return s;
    }

    // The rogue's cape and hood, recoloured from green to red (a copy of the texture; the original is untouched).
    static void RedCape(GameObject player)
    {
        const string texPath = "Assets/Game/Textures/Rogue_RedCape.png", matPath = MatDir + "Rogue_RedCape.mat";
        var cape = FindDeep(player.transform, "Rogue_Cape");
        if (cape == null) return;
        var src = cape.GetComponent<Renderer>().sharedMaterial;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
        {
            var orig = src.GetTexture("baseColorTexture") as Texture2D;
            if (orig == null) return;
            var rt = RenderTexture.GetTemporary(orig.width, orig.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(orig, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var copy = new Texture2D(orig.width, orig.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, orig.width, orig.height), 0, 0); copy.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float h, out float s, out float v);
                if (s > 0.15f && h > 0.2f && h < 0.55f)   // greens and teals become red
                {
                    var c = Color.HSVToRGB(0.995f, Mathf.Min(1f, s * 1.15f + 0.1f), Mathf.Min(1f, v * 1.05f));
                    c.a = px[i].a; px[i] = c;
                }
            }
            copy.SetPixels(px); copy.Apply();
            System.IO.Directory.CreateDirectory("Assets/Game/Textures");
            System.IO.File.WriteAllBytes(texPath, copy.EncodeToPNG());
            Object.DestroyImmediate(copy);
            AssetDatabase.ImportAsset(texPath);
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(src); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("baseColorTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        EditorUtility.SetDirty(mat);
        foreach (var n in new[] { "Rogue_Cape", "Rogue_Head_Hooded" })
        {
            var t = FindDeep(player.transform, n);
            if (t != null) t.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    const float Big = 1.9f; // large enemies, about twice the player's height

    static void BuildCharacters()
    {
        string rogue = KK + "Adventurers/Rogue_Hooded.glb";
        var player = Place(rogue, PlayerStart, PlayerYaw, 1f, chars, false);
        player.name = "Player";
        playerGo = player;
        foreach (var n in new[] { "1H_Crossbow", "2H_Crossbow", "Knife", "Knife_Offhand", "Throwable" })
        {
            var t = FindDeep(player.transform, n);
            if (t != null) t.gameObject.SetActive(false);
        }
        Pose(player, rogue, "1H_Ranged_Aiming", 0f);
        var handR = FindDeep(player.transform, "handslot.r");
        if (handR != null) BuildHandgun(handR);
        RedCape(player);

        // route
        Skeleton("Skeleton_Minion", new Vector3(-1.0f, 0, -17f), 175, "Walking_D_Skeletons", 0.3f, null);
        Skeleton("Skeleton_Warrior", new Vector3(1.6f, 0, -9f), 190, "Idle_Combat", 0.2f, "Skeleton_Blade", Big);
        // graveyard
        Skeleton("Skeleton_Minion", new Vector3(-12f, 0, -9.5f), 100, "Walking_D_Skeletons", 0.6f, null);
        Skeleton("Skeleton_Rogue", new Vector3(-17f, 0, -13f), 90, "Idle_Combat", 0.5f, "Skeleton_Axe");
        Skeleton("Skeleton_Minion", new Vector3(-13.5f, 0, -17.5f), 60, "Skeletons_Awaken_Floor", 0.9f, null);
        // yard and house
        Skeleton("Skeleton_Warrior", new Vector3(-9.5f, 0, 15f), 170, "Idle_Combat", 0.1f, "Skeleton_Axe", Big);
        Skeleton("Skeleton_Minion", new Vector3(-3f, 0, 14f), 200, "Walking_D_Skeletons", 0.1f, null);
        Skeleton("Skeleton_Rogue", new Vector3(5.5f, 0, 11f), 215, "Idle_Combat", 0.7f, "Skeleton_Blade");
        Skeleton("Skeleton_Minion", new Vector3(-11f, 0.14f, 25.5f), 170, "Idle_B", 0.4f, null);
        Skeleton("Skeleton_Mage", new Vector3(10f, 0, 24f), 230, "Idle_Combat", 0.3f, "Skeleton_Staff");
        // exit
        Skeleton("Skeleton_Warrior", new Vector3(5f, 0, 40f), 180, "Idle_Combat", 0.6f, "Skeleton_Blade");
        Skeleton("Skeleton_Minion", new Vector3(8f, 0, 37f), 200, "Walking_D_Skeletons", 0.8f, null);
        Skeleton("Skeleton_Warrior", new Vector3(0f, 0, 61.5f), 180, "Idle_Combat", 0.3f, "Skeleton_Axe", Big);
        // half-circle start path
        Skeleton("Skeleton_Minion", new Vector3(-25f, 0, -70.9f), 280, "Walking_D_Skeletons", 0.4f, null);
        Skeleton("Skeleton_Rogue", new Vector3(-5.4f, 0, -62.8f), 230, "Idle_Combat", 0.2f, "Skeleton_Blade");
        Skeleton("Skeleton_Warrior", new Vector3(0f, 0, -46.8f), 180, "Idle_Combat", 0.5f, "Skeleton_Axe", Big); // guards the main gate
        // small house by the start
        Skeleton("Skeleton_Minion", new Vector3(-6.5f, 0, -32.5f), 110, "Walking_D_Skeletons", 0.5f, null);
        Skeleton("Skeleton_Rogue", new Vector3(-11.5f, 0, -26f), 150, "Idle_Combat", 0.4f, "Skeleton_Blade");
        // second graveyard
        Skeleton("Skeleton_Minion", new Vector3(-8f, 0, 48.5f), 100, "Walking_D_Skeletons", 0.2f, null);
        Skeleton("Skeleton_Rogue", new Vector3(-13f, 0, 45f), 90, "Idle_Combat", 0.6f, "Skeleton_Axe");
        Skeleton("Skeleton_Warrior", new Vector3(-9.5f, 0, 40.5f), 120, "Idle_Combat", 0.8f, "Skeleton_Blade", Big);
        // small house in the north and the loop path
        Skeleton("Skeleton_Minion", new Vector3(13.5f, 0, 53f), 200, "Idle_B", 0.3f, null);
        Skeleton("Skeleton_Mage", new Vector3(26f, 0, 16f), 250, "Idle_Combat", 0.5f, "Skeleton_Staff", Big);
        Skeleton("Skeleton_Minion", new Vector3(26.5f, 0, 42f), 190, "Walking_D_Skeletons", 0.7f, null);
        Skeleton("Skeleton_Warrior", new Vector3(-26f, 0, 28f), 175, "Idle_Combat", 0.4f, "Skeleton_Axe", Big);
        Skeleton("Skeleton_Minion", new Vector3(-20f, 0, 59.5f), 100, "Walking_D_Skeletons", 0.9f, null);
    }

    // ---------- camera ----------

    static void BuildCamera()
    {
        var go = new GameObject("Main Camera");
        cameraGo = go;
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        go.AddComponent<AudioListener>();
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        // over-the-shoulder view behind the player
        var facing = Quaternion.Euler(0, PlayerYaw, 0);
        go.transform.position = PlayerStart + facing * new Vector3(1.45f, 2.85f, -5.6f);
        go.transform.rotation = Quaternion.LookRotation((PlayerStart + facing * new Vector3(0.2f, 1.7f, 14f)) - go.transform.position);
    }

    // =====================================================================
    // GAMEPLAY: components, enemies, gunman, keys, gates, cover, pickups
    // =====================================================================

    static int EnsureLayer(string name)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++) if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        for (int i = 8; i < 32; i++)
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            {
                layers.GetArrayElementAtIndex(i).stringValue = name;
                tm.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
        return 0;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static void RemoveLeaves(GameObject gate, string prefix)
    {
        if (gate == null) return;
        foreach (var n in new[] { prefix + "_left", prefix + "_right" })
        {
            var t = FindDeep(gate.transform, n);
            if (t != null) t.gameObject.SetActive(false);
        }
    }

    static AudioClip[] Clips(string folder, params string[] names)
    {
        var list = new List<AudioClip>();
        foreach (var n in names)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ThirdParty/Kenney/" + folder + "/" + n + ".ogg");
            if (c != null) list.Add(c); else Debug.LogWarning("Missing sound: " + folder + "/" + n);
        }
        return list.ToArray();
    }

    static AudioClip[] Numbered(string folder, string baseName, int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++) names[i] = baseName + "_00" + i;
        return Clips(folder, names);
    }

    static void SetupEnemy(GameObject go, int enemyLayer)
    {
        bool big = go.transform.localScale.x > 1.5f;
        var hand = FindDeep(go.transform, "handslot.r");
        bool armed = hand != null && hand.childCount > 0;
        SetLayer(go, enemyLayer);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 1.15f, 0); cap.height = 2.3f; cap.radius = 0.55f;
        go.AddComponent<Rigidbody>().isKinematic = true;
        var ag = go.AddComponent<NavMeshAgent>();
        ag.enabled = false; // switched on at runtime, once the walkable area exists
        ag.radius = 0.5f; ag.height = 2.2f; ag.angularSpeed = 420f; ag.acceleration = 24f;
        ag.stoppingDistance = big ? 2.3f : 1.2f;
        ag.avoidancePriority = 30 + rng.Next(40);
        var ai = go.AddComponent<EnemyAI>();
        ai.big = big; ai.armed = armed;
        ai.minHits = big ? 15 : 5; ai.maxHits = big ? 21 : 7;   // bullets to kill
        ai.damage = big ? 2 : 1;                                 // big enemies hit twice as hard
        ai.chaseSpeed = big ? 2.7f : 3.3f;
        ai.attackRange = big ? 3.3f : 1.9f;
        ai.minPainCries = 2; ai.maxPainCries = big ? 3 : 4;      // times it cries out when shot
        ai.ammoDropChance = big ? 0.5f : 0.25f;                  // drops a small clip (6 bullets)
    }

    const float KeyHeight = 2.0f;   // keys float at this height so they are clear of the ground

    static GameObject MakePickup(string name, Pickup.Kind kind, int amount, Vector3 pos, bool active)
    {
        if (kind == Pickup.Kind.Ammo && name != "AmmoPickupTemplate") amount = Mathf.Max(amount, AmmoPerPickup);
        GameObject go;
        if (kind == Pickup.Kind.Key || kind == Pickup.Kind.FinalKey)
        {
            go = new GameObject(name);
            var model = Place(Dg("key"), Vector3.zero, 0, 2.6f, go.transform, false);
            if (model != null) { model.transform.localPosition = Vector3.zero; model.transform.localEulerAngles = new Vector3(0, 0, 90); }
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.3f); l.intensity = 8f; l.range = 6f;
        }
        else if (kind == Pickup.Kind.Ammo)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = new Vector3(0.5f, 0.32f, 0.34f);
            go.GetComponent<Renderer>().sharedMaterial = Mat("Pickup_Ammo", new Color(0.85f, 0.65f, 0.15f), 0.3f, 0.2f, new Color(1.6f, 1.1f, 0.2f));
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * 0.5f;
            go.GetComponent<Renderer>().sharedMaterial = Mat("Pickup_Health", new Color(0.2f, 0.8f, 0.3f), 0.3f, 0f, new Color(0.3f, 1.8f, 0.5f));
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        go.transform.SetParent(props, false);
        go.transform.position = pos;
        var p = go.AddComponent<Pickup>();
        p.kind = kind; p.amount = amount;
        go.SetActive(active);
        return go;
    }

    // Finds the ridge of a building's roof: the line across its middle where the roof stays highest.
    // Returns the two ends of a walk along it (fraction of the building's length either side of the middle).
    static bool RoofLine(GameObject building, float fraction, out Vector3 a, out Vector3 b)
    {
        Physics.SyncTransforms();
        var bb = BoundsOf(building);
        Vector3 c = bb.center; c.y = bb.max.y + 3f;
        float len = Mathf.Max(bb.size.x, bb.size.z);
        a = b = c; float best = float.MinValue;
        foreach (var axis0 in new[] { building.transform.right, building.transform.forward })
        {
            Vector3 axis = axis0; axis.y = 0; axis.Normalize();
            float low = float.MaxValue;
            for (float t = -0.3f; t <= 0.301f; t += 0.1f)
            {
                float h = float.MinValue;
                foreach (var hit in Physics.RaycastAll(c + axis * (t * len), Vector3.down, bb.size.y + 6f))
                    if (hit.collider.transform.IsChildOf(building.transform)) h = Mathf.Max(h, hit.point.y);
                low = Mathf.Min(low, h);
            }
            if (low > best)
            {
                best = low;
                a = c + axis * (-fraction * len); b = c + axis * (fraction * len);
                a.y = b.y = low;
            }
        }
        return best > float.MinValue;
    }

    static void MakeGunman(string name, Vector3 pos, float yaw, Vector3 a, Vector3 b, Transform building, GameObject key, float range, int enemyLayer)
    {
        string gpath = KK + "Skeletons/Skeleton_Rogue.glb";
        var gunner = Place(gpath, pos, yaw, 1f, chars, false);
        gunner.name = name;
        Pose(gunner, gpath, "Sit_Floor_Idle", 0f);
        SetLayer(gunner, enemyLayer);
        var cap = gunner.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 1.15f, 0); cap.height = 2.3f; cap.radius = 0.6f;
        var gun = BuildHandgun(FindDeep(gunner.transform, "handslot.r"));   // same handgun as the player's
        SetLayer(gun, enemyLayer);
        var rg = gunner.AddComponent<RoofGunner>();
        rg.muzzle = FindDeep(gun.transform, "Muzzle");
        rg.pointA = a; rg.pointB = b;
        rg.ownBuilding = building;
        rg.keyToDrop = key;
        rg.range = range;
    }

    static Material ParticleMat(string name, Color color)
    {
        string path = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildGameplay()
    {
        int enemyLayer = EnsureLayer("Enemy"), playerLayer = EnsureLayer("Player"), noNavLayer = EnsureLayer("NoNav");
        int ignore = 1 << 2;

        // ---- core objects ----
        var game = new GameObject("Game");
        var gm = game.AddComponent<GameManager>();
        gm.startLevel = TestStartLevel;
        var hud = game.AddComponent<Hud>();
        var menu = game.AddComponent<GameMenu>();
        menu.shots = new[]
        {
            MenuShot("STAGE 1   -   THE VILLAGE", new Vector3(-22f, 32f, -50f), new Vector3(12f, 32f, -52f), new Vector3(-2f, 0f, -4f), new Vector3(4f, 0f, -2f), 9f),
            MenuShot("STAGE 1   -   THE VILLAGE SQUARE", new Vector3(-34f, 30f, -2f), new Vector3(18f, 30f, -8f), new Vector3(-4f, 0f, 34f), new Vector3(6f, 0f, 34f), 9f),
            MenuShot("STAGE 2   -   THE HAMLET", new Vector3(-36f, 30f, 72f), new Vector3(-50f, 28f, 78f), new Vector3(-70f, 0f, 38f), new Vector3(-72f, 0f, 36f), 9f),
            MenuShot("STAGE 3   -   THE ARENA", new Vector3(34f, 30f, 44f), new Vector3(50f, 30f, 38f), new Vector3(66f, 0f, 72f), new Vector3(68f, 0f, 72f), 9f),
        };
        // the story intro: 18 seconds, 1:12 to 1:30 of the music; the last clip ends behind the player
        menu.introShots = new[]
        {
            MenuShot("", new Vector3(-30f, 26f, -64f), new Vector3(-14f, 21f, -46f), new Vector3(0f, 0f, 6f), new Vector3(0f, 0f, 14f), 3.8f),
            MenuShot("", new Vector3(-3f, 7f, -26f), new Vector3(-4f, 6f, -6f), new Vector3(-17f, 0.5f, -12f), new Vector3(-16f, 0.5f, -15f), 3.4f),
            MenuShot("", new Vector3(-22f, 13f, 4f), new Vector3(10f, 12f, 6f), new Vector3(-4f, 0f, 30f), new Vector3(6f, 0f, 30f), 2.8f),
            MenuShot("", new Vector3(-48f, 15f, 60f), new Vector3(-56f, 12f, 54f), new Vector3(-70f, 0f, 38f), new Vector3(-72f, 0f, 36f), 3.0f),
            MenuShot("", new Vector3(40f, 12f, 58f), new Vector3(50f, 10f, 62f), new Vector3(66f, 0f, 74f), new Vector3(68f, 0f, 72f), 2.4f),
            MenuShot("", new Vector3(-58f, 26f, -40f), Vector3.zero, new Vector3(-46f, 0f, -24f), Vector3.zero, 2.6f),
        };
        menu.introLines = new[]
        {
            Line(0.6f, 3.2f, "Long ago, a creature was locked away inside this village..."),
            Line(4.0f, 3.2f, "Tonight it has woken up, and the dead rise from their graves."),
            Line(7.3f, 2.6f, "They fill the streets, and they hide on every roof."),
            Line(10.0f, 2.8f, "In the hamlet, its servants guard the keys."),
            Line(12.9f, 2.0f, "Beyond lies the arena. It is waiting for you."),
        };
        var sfx = game.AddComponent<Sfx>();
        var nav = game.AddComponent<NavMeshRuntimeBaker>();
        nav.mask = ~((1 << enemyLayer) | (1 << playerLayer) | (1 << noNavLayer));
        nav.center = new Vector3(0f, 5f, 16f); nav.size = new Vector3(212f, 40f, 210f);   // covers level 2 and the arena too
        gm.hud = hud;
        gm.tracerMaterial = Mat("FX_Tracer", new Color(1f, 0.85f, 0.4f), 0f, 0f, new Color(3f, 2.2f, 0.8f));
        gm.laserMaterial = Mat("FX_Laser", new Color(1f, 0.1f, 0.1f), 0f, 0f, new Color(4f, 0.2f, 0.2f));
        gm.hitMaterial = ParticleMat("FX_HitSparks", new Color(2.2f, 1.6f, 0.7f));

        // ---- sounds (Kenney packs) ----
        sfx.footsteps = Numbered("ImpactSounds", "footstep_concrete", 5);
        sfx.gunFallback = Clips("ImpactSounds", "impactMetal_heavy_002", "impactPunch_heavy_001", "impactPlate_heavy_000");
        sfx.bulletImpact = Numbered("ImpactSounds", "impactGeneric_light", 5);
        sfx.enemyHit = Numbered("ImpactSounds", "impactSoft_medium", 5);
        sfx.enemyDeath = Numbered("ImpactSounds", "impactWood_heavy", 5);
        sfx.enemyAttack = Clips("RPGAudio", "knifeSlice", "knifeSlice2");
        sfx.playerHurt = Numbered("ImpactSounds", "impactPunch_medium", 5);
        sfx.pickup = Clips("RPGAudio", "handleCoins", "handleCoins2");
        sfx.reload = Clips("RPGAudio", "beltHandle1", "beltHandle2");
        sfx.spawn = Numbered("ImpactSounds", "impactSoft_heavy", 5);
        sfx.gate = Clips("RPGAudio", "doorOpen_1", "doorOpen_2");
        var click = Clips("ImpactSounds", "impactMetal_light_000"); sfx.emptyClick = click.Length > 0 ? click[0] : null;
        var bell = Clips("ImpactSounds", "impactBell_heavy_000"); sfx.key = bell.Length > 0 ? bell[0] : null;

        // ---- player ----
        SetLayer(playerGo, playerLayer);
        var cc = playerGo.AddComponent<CharacterController>();
        cc.center = new Vector3(0, 1.12f, 0); cc.height = 2.2f; cc.radius = 0.45f; cc.stepOffset = 0.35f; cc.slopeLimit = 40f;
        var pc = playerGo.AddComponent<PlayerController>();
        pc.muzzle = FindDeep(playerGo.transform, "Muzzle");
        pc.shootMask = ~((1 << playerLayer) | ignore);
        gm.player = pc;
        var tpc = cameraGo.AddComponent<ThirdPersonCamera>();
        tpc.target = playerGo.transform;
        tpc.collisionMask = ~((1 << playerLayer) | (1 << enemyLayer) | ignore);
        pc.cam = tpc;

        // ---- melee enemies already placed in the level ----
        var placed = new List<GameObject>();
        foreach (Transform c in chars) if (c.gameObject != playerGo) placed.Add(c.gameObject);
        foreach (var e in placed) SetupEnemy(e, enemyLayer);

        // ---- first graveyard: 7-9 small enemies rise one by one, 5 seconds apart ----
        var template = Skeleton("Skeleton_Minion", new Vector3(-16f, 0, -12f), 90, "Idle", 0f, null);
        template.name = "SpawnTemplate";
        SetupEnemy(template, enemyLayer);
        template.SetActive(false);
        var spawner = new GameObject("Graveyard Spawner").AddComponent<GraveyardSpawner>();
        spawner.transform.SetParent(env, false);
        spawner.transform.position = new Vector3(-16f, 0, -12f);
        spawner.template = template;
        spawner.chaseSpeed = 4.6f;   // faster than other enemies (3.3), slower than the running player (6.2)
        spawner.points = new[] {
            new Vector3(-13.6f, 0, -19.5f), new Vector3(-9.2f, 0, -16.5f), new Vector3(-13.6f, 0, -7.5f), new Vector3(-9.2f, 0, -4.5f),
            new Vector3(-13.6f, 0, -16.5f), new Vector3(-9.2f, 0, -19.5f), new Vector3(-13.6f, 0, -4.5f), new Vector3(-9.2f, 0, -7.5f),
            new Vector3(-18.5f, 0, -8f) };

        // ---- gunman 1: on the big house roof, carries the second key ----
        // he walks the roof edge that overlooks the route, up and over the ridge
        // he sits on the ground behind the big house (on the side loop), so he cannot be shot from the route
        var gunKey = MakePickup("Key (Gunman)", Pickup.Kind.Key, 1, new Vector3(LoopEastX - 1.2f, KeyHeight, -12f), false);
        var backHouse = env.Find("Big House Back");
        // he walks the north slope of the back roof: hidden from the route until the player is past the big house
        Vector3 ra = new Vector3(24.5f, 9f, -9.5f), rb = new Vector3(33f, 9f, -9.5f);
        MakeGunman("Enemy_Gunman", new Vector3(28.5f, 10f, -9.5f), 0, ra, rb, backHouse, gunKey, 36f, enemyLayer);
        var gun1 = chars.Find("Enemy_Gunman");
        if (gun1 != null)
        {
            var rg1 = gun1.GetComponent<RoofGunner>();
            rg1.dropMessage = "The gunman dropped a key behind the big house";
            rg1.wakesOnDeath = spawner;   // the graveyard starts raising enemies as soon as he dies
        }

        // ---- gunman 2: on the crypt of the second graveyard, watching the village square ----
        Transform crypt2 = null;
        var gy2 = env.Find("Graveyard 2");
        if (gy2 != null) foreach (Transform c in gy2) if (c.name == "crypt") crypt2 = c;
        MakeGunman("Enemy_Gunman_2", new Vector3(-15f, 7.2f, 46f), 90, new Vector3(-19.5f, 7.2f, 46f), new Vector3(-14.2f, 7.2f, 46f),
                   crypt2, null, 30f, enemyLayer);
        // cover from him around the square
        float[,] sqCover = { { -23.4f, 31f }, { -23.4f, 53f }, { -0.5f, 51.5f }, { 1.5f, 38f }, { -12f, 32.5f }, { 8.5f, 57.2f } };
        for (int i = 0; i < sqCover.GetLength(0); i++)
            Place(Dg("barrel_large"), new Vector3(sqCover[i, 0], 0, sqCover[i, 1]), Rand(0, 360), 1.7f, props).name = "Cover Barrel";

        BuildLevel1Extras(enemyLayer);

        // ---- second key: inside the small house near the exit ----
        var houseKeys = new List<GameObject>();
        foreach (var hn in new[] { "Small House North", "Small House Start" })
        {
            var hh = env.Find(hn);
            if (hh != null) houseKeys.Add(MakePickup("Key (" + hn + ")", Pickup.Kind.Key, 1, hh.TransformPoint(new Vector3(-1.5f, KeyHeight, 0.5f)), true));
        }
        houseKeys.Add(MakePickup("Key (House)", Pickup.Kind.Key, 1, new Vector3(-11.5f, KeyHeight, 24.5f), true));   // the original house (copies share its layout)
        gm.houseKeys = houseKeys.ToArray();

        // ---- big barrels: cover from the gunman along the route ----
        float[] bz = { -34f, -26.5f, -19f, -8.5f, 2.3f };   // the last one moved off the side-loop junction
        // two more set into the lane, for cover while coming up from the gate
        Place(Dg("barrel_large"), new Vector3(2.4f, 0, -40.5f), 40, 1.7f, props).name = "Cover Barrel";
        Place(Dg("barrel_large"), new Vector3(-2.6f, 0, -31f), 200, 1.7f, props).name = "Cover Barrel";
        foreach (float z in bz)
            Place(Dg("barrel_large"), new Vector3(3.9f, 0, z), Rand(0, 360), 1.7f, props).name = "Cover Barrel";

        // ---- gate into the village square (opens when the player walks up) ----
        var sq = Place(Hw("arch_gate"), new Vector3(0, 0, 4f), 0, 1.15f, props);
        sq.name = "Square Gate";
        for (int i = 0; i < 8; i++)
        {
            float fx = 4.43f + i * 4f;
            Place(Hw("fence"), new Vector3(fx, 0, 4f), 0, 1f, props);
            Place(Hw("fence"), new Vector3(-fx, 0, 4f), 0, 1f, props);
        }
        var sg = sq.AddComponent<Gate>();
        sg.leftLeaf = FindDeep(sq.transform, "arch_gate_left"); sg.rightLeaf = FindDeep(sq.transform, "arch_gate_right");
        sg.requiredKeys = 0; sg.openRadius = 8f;
        if (sg.leftLeaf != null) SetLayer(sg.leftLeaf.gameObject, noNavLayer);
        if (sg.rightLeaf != null) SetLayer(sg.rightLeaf.gameObject, noNavLayer);

        // ---- exit gate: needs both keys ----
        if (exitGateGo != null)
        {
            exitGateGo.name = "Exit Gate";
            var eg = exitGateGo.AddComponent<Gate>();
            eg.leftLeaf = FindDeep(exitGateGo.transform, "arch_gate_left"); eg.rightLeaf = FindDeep(exitGateGo.transform, "arch_gate_right");
            eg.requiredKeys = 2; eg.isExit = true; eg.openRadius = 6f;
            gm.exitGate = eg;
            gm.level2Start = L2Start; gm.level2Yaw = 270f;
            var arenaGateT = env.Find("Stage 3/Arena Gate");
            if (arenaGateT != null) gm.arenaGate = arenaGateT.GetComponent<Gate>();
            gm.arenaStart = new Vector3(S3GateX + 3f, 0f, L2LaneZ); gm.hamletCentre = L2Centre; gm.hamletRadius = L2Radius; gm.level2Corner = L2Corner; gm.arenaGateX = S3GateX;
        }

        BuildStage2Enemies(gm, enemyLayer);
        BuildBoss(gm, enemyLayer);
        BuildBoss2(gm, enemyLayer);
        BuildSpecialEnemies(gm, enemyLayer);

        // ---- pickups ----
        // ammo budget (with enemy drops): level 1 about 200, stage 2 about 150, stage 3 about 50 (+ the knight's reward)
        gm.ammoPickupTemplate = MakePickup("AmmoPickupTemplate", Pickup.Kind.Ammo, 6, new Vector3(0, -50, 0), false);
        float[,] ammo = { { -44f, -19f }, { 0f, -39f }, { 1f, -5f }, { -8f, 23f }, { 26f, 30f }, { -12f, 46.5f }, { 12f, 58f } };
        for (int i = 0; i < ammo.GetLength(0); i++)
            MakePickup("Ammo", Pickup.Kind.Ammo, 12, new Vector3(ammo[i, 0], 0.6f, ammo[i, 1]), true);
        float[,] health = { { -12.5f, -34f }, { 9.5f, 17.5f }, { -26f, 18f }, { 6f, 36f } };
        for (int i = 0; i < health.GetLength(0); i++)
            MakePickup("Health", Pickup.Kind.Health, 2, new Vector3(health[i, 0], 0.7f, health[i, 1]), true);
    }
}
