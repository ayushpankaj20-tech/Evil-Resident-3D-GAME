using UnityEngine;

// All sound effects. Clips are assigned by the level builder. The gun and reload sounds are loaded by file name
// from Assets/Game/Resources/Audio (names below, without the extension).
public class Sfx : MonoBehaviour
{
    public static Sfx I;

    public AudioClip[] footsteps, gunFallback, bulletImpact, enemyHit, enemyDeath, enemyAttack, playerHurt, pickup, reload, spawn, gate;
    public AudioClip emptyClick, key;
    public string playerGunFile = "desert-eagle-cs";
    public string playerReloadFile = "reload-gun-sound_CpHCnmK";
    public string enemyGunFile = "gunshot-sound";
    [Range(0, 1)] public float enemyGunVolume = 0.9f;
    public float enemyGunHearing = 80f;   // metres at which the gunman's shots fade to silence
    [Range(0, 1)] public float gunVolume = 0.4f;    // kept low so the zombies can be heard over the shots
    [Range(0, 1)] public float windVolume = 0.3f;        // background wind: average level ...
    [Range(0, 1)] public float windSwing = 0.15f;        // ... and how far it rises and falls (random gusts)

    // zombie sounds (files in Assets/Game/Resources/Audio, names without the extension)
    public string zombieGrowlFile = "minecraft-zombie-sounds";            // small enemies, while they chase (3 growls in one file, split at load)
    public string zombiePainFile = "quake-1-zombie-pain";                // small enemies, when shot
    public string zombieCallFile = "town-zombie-call1";                  // far-off call in the background
    public string victoryFile = "zg-survival-victory-sound";             // a level is finished
    public string defeatFile = "zg-survival-defeat-sound";               // the player dies
    public string bossScreamFile = "tom-scream-sound-effect-useful-sound-effects-5";   // a boss becomes enraged
    [Range(0, 1)] public float defeatVolume = 0.9f, bossScreamVolume = 1f;
    [Range(0, 1)] public float zombieGrowlVolume = 1f;
    [Range(0, 1)] public float zombiePainVolume = 1f;
    public float zombieFullVolumeDistance = 9f;   // zombie voices are at full volume within this many metres
    public float zombieHearing = 45f;             // ... and fade to silence at this distance
    [Range(0, 1)] public float zombieCallVolume = 0.75f;
    [Range(0, 1)] public float victoryVolume = 0.9f;
    public float callMinGap = 60f, callMaxGap = 60f;   // the background call repeats every minute (a new area plays it at once)
    public float growlGap = 0.7f;                      // shortest time between two growls from any enemies

    AudioClip zombiePain, zombieCall, victory, defeat, bossScream;
    AudioClip[] zombieGrowls;
    float nextCall, nextGrowl;

    AudioSource oneShot, wind;
    AudioClip playerGun, playerReload, enemyGun;

    void Awake()
    {
        I = this;
        oneShot = gameObject.AddComponent<AudioSource>();
        oneShot.spatialBlend = 0f;
        playerGun = Resources.Load<AudioClip>("Audio/" + playerGunFile);
        playerReload = Resources.Load<AudioClip>("Audio/" + playerReloadFile);
        enemyGun = Resources.Load<AudioClip>("Audio/" + enemyGunFile);
        zombieGrowls = SplitAtSilence(Resources.Load<AudioClip>("Audio/" + zombieGrowlFile));
        defeat = Resources.Load<AudioClip>("Audio/" + defeatFile);
        bossScream = Resources.Load<AudioClip>("Audio/" + bossScreamFile);
        zombiePain = Resources.Load<AudioClip>("Audio/" + zombiePainFile);
        zombieCall = Resources.Load<AudioClip>("Audio/" + zombieCallFile);
        victory = Resources.Load<AudioClip>("Audio/" + victoryFile);
        nextCall = Time.time + callMinGap;

        wind = gameObject.AddComponent<AudioSource>();
        wind.clip = MakeWind();
        wind.loop = true;
        wind.volume = windVolume;
        wind.spatialBlend = 0f;
        wind.Play();
    }

    void Update()
    {
        // the wind gusts: its volume drifts up and down at random
        if (wind != null)
            wind.volume = Mathf.Clamp01(windVolume + windSwing * (Mathf.PerlinNoise(Time.time * 0.13f, 7.3f) * 2f - 1f) * 1.6f);
        // background call on a timer; entering a new area plays it at once and restarts the timer
        var gm = GameManager.I;
        if (Time.time >= nextCall && gm != null && !gm.Ended && !gm.Transitioning && !gm.InMenu) ZombieCall();
    }

    public static void ZombieCall()
    {
        if (I == null) return;
        if (I.zombieCall != null) I.oneShot.PlayOneShot(I.zombieCall, I.zombieCallVolume);
        I.nextCall = Time.time + Random.Range(I.callMinGap, I.callMaxGap);
    }

    // pushes the next background call back (used when another big sound is playing)
    public static void DelayCall(float seconds) { if (I != null) I.nextCall = Mathf.Max(I.nextCall, Time.time + seconds); }

    // returns false when another enemy growled a moment ago, so a crowd does not all growl at once
    public static bool ZombieGrowl(Vector3 pos)
    {
        if (I == null || I.zombieGrowls == null || I.zombieGrowls.Length == 0 || Time.time < I.nextGrowl) return false;
        I.nextGrowl = Time.time + I.growlGap;
        I.PlayVoice(Pick(I.zombieGrowls), pos, I.zombieGrowlVolume);
        return true;
    }

    public static void ZombiePain(Vector3 pos)
    {
        if (I != null && I.zombiePain != null) I.PlayVoice(I.zombiePain, pos, I.zombiePainVolume);
    }

    // A 3D sound that stays loud nearby and fades evenly with distance (the default 3D sound is very quiet a few metres away).
    void PlayVoice(AudioClip clip, Vector3 pos, float volume)
    {
        var go = new GameObject("Zombie Voice");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = zombieFullVolumeDistance;
        src.maxDistance = zombieHearing;
        src.dopplerLevel = 0f;
        src.Play();
        Destroy(go, clip.length + 0.2f);
    }

    public static void Defeat()
    {
        if (I != null && I.defeat != null) I.oneShot.PlayOneShot(I.defeat, I.defeatVolume);
    }

    public static void BossScream()
    {
        if (I != null && I.bossScream != null) I.oneShot.PlayOneShot(I.bossScream, I.bossScreamVolume);
    }

    // One file holding several sounds with silence between them becomes one clip per sound.
    static AudioClip[] SplitAtSilence(AudioClip clip)
    {
        if (clip == null) return new AudioClip[0];
        int ch = clip.channels, n = clip.samples, win = Mathf.Max(1, clip.frequency / 50);   // 20 ms windows
        var data = new float[n * ch];
        if (!clip.GetData(data, 0)) return new[] { clip };
        int windows = (n + win - 1) / win;
        var loud = new bool[windows];
        for (int w = 0; w < windows; w++)
        {
            double sum = 0; int cnt = 0;
            for (int i = w * win; i < Mathf.Min(n, (w + 1) * win); i++) { float v = data[i * ch]; sum += v * v; cnt++; }
            loud[w] = System.Math.Sqrt(sum / Mathf.Max(1, cnt)) > 0.02;
        }
        var parts = new System.Collections.Generic.List<AudioClip>();
        int start = -1, quiet = 0;
        for (int w = 0; w <= windows; w++)
        {
            bool on = w < windows && loud[w];
            if (on) { if (start < 0) start = w; quiet = 0; }
            else if (start >= 0 && (++quiet >= 3 || w == windows))      // 60 ms of quiet ends a sound
            {
                int a = Mathf.Max(0, start - 1) * win, b = Mathf.Min(n, (w + 1) * win);
                if (b - a > clip.frequency / 5)                            // ignore clicks shorter than 0.2 s
                {
                    var part = new float[(b - a) * ch];
                    System.Array.Copy(data, a * ch, part, 0, part.Length);
                    var c = AudioClip.Create(clip.name + "_" + (parts.Count + 1), b - a, ch, clip.frequency, false);
                    c.SetData(part, 0);
                    parts.Add(c);
                }
                start = -1; quiet = 0;
            }
        }
        return parts.Count > 0 ? parts.ToArray() : new[] { clip };
    }

    public static void Victory()
    {
        if (I != null && I.victory != null) I.oneShot.PlayOneShot(I.victory, I.victoryVolume);
    }

    static AudioClip Pick(AudioClip[] set)
    {
        return set == null || set.Length == 0 ? null : set[Random.Range(0, set.Length)];
    }

    // 2D sound (things the player does or that happen to the player)
    public static void Play(AudioClip[] set, float volume = 1f) { Play(Pick(set), volume); }
    public static void Play(AudioClip clip, float volume = 1f)
    {
        if (I != null && clip != null) I.oneShot.PlayOneShot(clip, volume);
    }

    // 3D sound at a place in the world
    public static void PlayAt(AudioClip[] set, Vector3 pos, float volume = 1f)
    {
        var clip = Pick(set);
        if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, volume);
    }

    public static void Gunshot(Vector3 pos, bool isPlayer)
    {
        if (I == null) return;
        float far = 1f;
        if (!isPlayer && Camera.main != null)
            far = Mathf.Clamp01(1f - Vector3.Distance(Camera.main.transform.position, pos) / I.enemyGunHearing);
        var clip = isPlayer ? I.playerGun : I.enemyGun;
        if (clip != null)
        {
            I.oneShot.PlayOneShot(clip, isPlayer ? I.gunVolume : I.enemyGunVolume * (0.35f + 0.65f * far));
            return;
        }
        // no gunshot file yet: layer the heavy impact sounds
        if (I.gunFallback == null) return;
        foreach (var c in I.gunFallback)
        {
            if (c == null) continue;
            if (isPlayer) I.oneShot.PlayOneShot(c, I.gunVolume);
            else AudioSource.PlayClipAtPoint(c, pos, 1f);
        }
    }

    public static void Reload()
    {
        if (I == null) return;
        if (I.playerReload != null) I.oneShot.PlayOneShot(I.playerReload, 0.9f);
        else Play(I.reload, 0.8f);
    }

    // soft looping wind made from filtered noise
    static AudioClip MakeWind()
    {
        const int rate = 22050, seconds = 6;
        int n = rate * seconds;
        var data = new float[n];
        var rnd = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)rnd.NextDouble() * 2f - 1f;
            low += (white - low) * 0.035f;
            float t = (float)i / n;
            float gust = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2f) * Mathf.Sin(t * Mathf.PI * 6f);
            data[i] = low * gust * 2.2f;
        }
        int fade = rate / 2; // blend the end into the start so the loop has no click
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            data[i] = data[i] * k + data[n - fade + i] * (1f - k);
        }
        var clip = AudioClip.Create("Wind", n - fade, 1, rate, false);
        var trimmed = new float[n - fade];
        System.Array.Copy(data, trimmed, n - fade);
        clip.SetData(trimmed, 0);
        return clip;
    }
}
