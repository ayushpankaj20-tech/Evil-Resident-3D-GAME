using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Holds game state: keys, win/lose, restart.
public class GameManager : MonoBehaviour
{
    public static GameManager I;
    public static readonly List<EnemyAI> Enemies = new List<EnemyAI>();

    public PlayerController player;
    public Hud hud;
    public GameObject ammoPickupTemplate;
    public int requiredKeys = 2;
    public float winZ = 68.5f;          // level 1 is finished once the player walks past the open exit gate
    public Gate exitGate;               // shut behind the player when level 2 begins
    public Vector3 level2Start = new Vector3(0f, 0f, 71f);
    public float level2Yaw = 270f;      // faces west, down the new path
    public float fadeTime = 0.7f;
    public GameObject level2Enemies;    // hidden until level 2 begins
    public GameObject level2EnemyKey;   // given to one random Shield Skeleton of stage 2
    public GameObject level2ThrowerKey; // given to one random Bone Thrower of stage 2
    public GameObject exploderTemplate; // an exploder appears near every key
    public GameObject[] houseKeys;      // level 1: the house key is in one of these houses, picked at random each game
    public AmbushTrigger stage3Crawler; // rises silently with the arena graveyard dead
    public int level2Keys = 2;          // keys that open the arena gate
    public GraveyardSpawner[] arenaSpawners;   // the two graveyards beside the arena
    public GameObject finalKey;         // appears in the arena house once stage 3 is cleared
    public Gate arenaGate;
    public BossAI boss;                 // THE CREATURE KNIGHT: hidden until the final key is taken
    public BossAI boss2;                // THE REAL CREATURE: comes 60 seconds after the knight dies
    public GameObject knightReward;     // full health and ammo, laid out in the arena when the knight dies
    public float boss2Delay = 60f;      // time to gear up between the two bosses
    public string boss2BannerText = "THE REAL CREATURE HAS CHOSEN TO FIGHT YOU";
    public GameObject houseBlock;       // shuts the arena house once the player has come out to fight
    public float houseDoorX = 91.3f;

    public Vector3 arenaStart = new Vector3(37f, 0f, 72f);   // where a restart at the arena checkpoint begins
    public int startLevel = 1;          // 2 = start at the level-2 gate (for testing)
    public string areaBannerText = "A CREATURE TRAPPED IN A VILLAGE HAS WOKEN UP";   // red banner shown with the zombie call
    public string villageBannerText = "THE CREATURE HAS ABANDONED THE VILLAGE AND RUN AWAY";   // red banner on the way into the level-2 village
    public string finalBannerText = "THE CREATURE HAS ENTERED THE ARENA. KILL IT OR GET KILLED";   // red banner after the final key
    public Vector3 hamletCentre = new Vector3(-70f, 0f, 38f);   // middle of the level-2 hamlet
    public float hamletRadius = 25f;
    public Vector3 level2Corner = new Vector3(-38f, 0f, 72f);   // the left turn in level 2: the red banner shows here
    public float arenaGateX = 34f;                              // east of this (in level 2) is stage 3, the arena
    public Material tracerMaterial;
    public Material laserMaterial;
    public Material hitMaterial;

    ParticleSystem hitFx;
    int areasSeen = 1;   // bit per area the player has entered (bit 0 = the start camp)
    bool cornerAnnounced, finalKeyShown, bossStarted;

    public int Keys { get; private set; }
    public bool ExitOpen { get; set; }
    public bool Ended { get; private set; }
    public int Level { get; private set; } = 1;
    public bool Transitioning { get; private set; }

    // Furthest checkpoint reached: 2 = the level-2 gate, 3 = the arena, 4 = the knight fight, 5 = the real creature fight.
    // Kept across a restart (not across quitting).
    public static int Checkpoint = 1;
    // true when the scene should open on the main menu (the first load, and after "Main Menu" or a win);
    // a restart after dying goes straight back into the game
    public static bool ShowMenu = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCheckpoint() { Checkpoint = 1; ShowMenu = true; Hard = PlayerPrefs.GetInt(HardPref, 0) == 1; }

    // ---- difficulty (chosen in the main menu, remembered) ----
    // Easy: 14 hits of health, the gun does double damage, the arena gate is already open.
    // Hard: 7 hits, normal damage, every enemy a little faster, the bosses twice as fast, the arena gate locked.
    const string HardPref = "EvilResident_Hard";
    public static bool Hard;
    public static int GunDamage { get { return Hard ? 1 : 2; } }
    public static int MaxHealth { get { return Hard ? 7 : 14; } }
    public static void SetDifficulty(bool hard) { Hard = hard; PlayerPrefs.SetInt(HardPref, hard ? 1 : 0); PlayerPrefs.Save(); }

    public bool InMenu { get; private set; }
    public bool Paused { get; private set; }
    GameMenu menu;
    bool won, knightDead, arenaSealed;

    void Awake()
    {
        I = this;
        if (startLevel >= 2 && Checkpoint < 2) Checkpoint = 2;
        Enemies.Clear();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        menu = GetComponent<GameMenu>();
        InMenu = ShowMenu && menu != null;
        if (player != null) player.hitsToDie = MaxHealth;
        Theme.Apply(Theme.Morning);
        // the level-1 house key: one house at random, the others are removed
        if (houseKeys != null && houseKeys.Length > 0)
        {
            int pick = Random.Range(0, houseKeys.Length);
            for (int i = 0; i < houseKeys.Length; i++) if (i != pick && houseKeys[i] != null) { houseKeys[i].SetActive(false); Destroy(houseKeys[i]); }
        }
        Cursor.lockState = InMenu ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = InMenu;
    }

    void Start()
    {
        // one shared burst of sparks, emitted wherever a bullet lands on an enemy
        var fx = new GameObject("Hit Sparks");
        hitFx = fx.AddComponent<ParticleSystem>();
        hitFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = hitFx.main;
        main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.15f));
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emission = hitFx.emission; emission.enabled = false;
        var shape = hitFx.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.12f;
        var psr = fx.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = hitMaterial;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        hud.SetKeys(Keys, requiredKeys);
        if (InMenu) { hud.ShowGameplay(false); menu.OpenMain(); return; }
        BeginPlay();
    }

    // called by the main menu: START GAME (from level 1) or CONTINUE (from the last checkpoint)
    public void StartFromMenu(bool newGame)
    {
        if (!InMenu) return;
        if (newGame) Checkpoint = 1;
        InMenu = false;
        ShowMenu = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        hud.ShowGameplay(true);
        if (level2Enemies != null && Level < 2) level2Enemies.SetActive(false);   // shown during the story intro only
        if (player != null) player.SetMaxHealth(MaxHealth);
        Sfx.DelayCall(8f);
        BeginPlay();
    }

    void BeginPlay()
    {
        if (Checkpoint >= 2)
        {
            // restarted after reaching level 2: begin at its gate
            EnterLevel2(false);
            hud.SetFade(1f);
            StartCoroutine(Fade(1f, 0f));
            if (Checkpoint >= 3) EnterArena();
            if (Checkpoint >= 4) { finalKeyShown = true; if (finalKey != null) Destroy(finalKey); }
            if (Checkpoint >= 5) { bossStarted = true; knightDead = true; if (knightReward != null) knightReward.SetActive(true); StartCoroutine(Boss2After(10f)); }
            else if (Checkpoint >= 4) StartBoss();
            else hud.Message(Level2Objective, 6f);
        }
        else hud.Message(Level1Objective, 6f);
    }

    // Level 1 is over: the gate shuts behind the player and everything left in level 1 is removed.
    void EnterLevel2(bool sound)
    {
        Level = 2;
        Checkpoint = Mathf.Max(Checkpoint, 2);   // never lowers the arena checkpoint
        areasSeen |= 1 << 3;   // the level-2 lanes: no call here, the victory sound is playing
        if (exitGate != null) exitGate.CloseAndSeal(sound);
        foreach (var s in FindObjectsByType<GraveyardSpawner>(FindObjectsSortMode.None)) Destroy(s.gameObject);
        foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) Destroy(e.gameObject);
        foreach (var g in FindObjectsByType<RoofGunner>(FindObjectsSortMode.None)) Destroy(g.gameObject);
        if (level2Enemies != null) level2Enemies.SetActive(true);
        // one random Shield Skeleton carries a key, one random Bone Thrower the other
        if (level2Enemies != null)
        {
            var all = level2Enemies.GetComponentsInChildren<EnemyAI>();
            var shields = System.Array.FindAll(all, e => e.kind == EnemyAI.Kind.Shield);
            var throwers = System.Array.FindAll(all, e => e.kind == EnemyAI.Kind.Thrower);
            if (level2EnemyKey != null && shields.Length > 0) shields[Random.Range(0, shields.Length)].keyToDrop = level2EnemyKey;
            if (level2ThrowerKey != null && throwers.Length > 0) throwers[Random.Range(0, throwers.Length)].keyToDrop = level2ThrowerKey;
        }
        Keys = 0;
        requiredKeys = level2Keys;
        if (player != null)
        {
            player.Teleport(level2Start, level2Yaw);
            if (player.cam != null) player.cam.Snap(level2Yaw);
        }
        hud.SetKeys(Keys, requiredKeys);
    }

    // Restart at the arena checkpoint: stage 2 is skipped (its enemies removed, both keys held, the gate open)
    // and the player stands just inside the arena gate. The arena graveyards start as soon as the arena is entered.
    void EnterArena()
    {
        if (level2Enemies != null)
            foreach (Transform t in level2Enemies.transform)
            {
                bool keep = false;
                if (arenaSpawners != null)
                    foreach (var sp in arenaSpawners)
                        if (sp != null && (sp.transform == t || (sp.template != null && sp.template.transform == t))) keep = true;
                if (!keep) Destroy(t.gameObject);
            }
        Keys = requiredKeys;
        hud.SetKeys(Keys, requiredKeys);
        if (arenaGate != null) arenaGate.OpenNow();
        if (player != null)
        {
            player.Teleport(arenaStart, 90f);
            if (player.cam != null) player.cam.Snap(90f);
        }
    }

    IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime))
        {
            hud.SetFade(Mathf.Lerp(from, to, t));
            yield return null;
        }
        hud.SetFade(to);
    }

    IEnumerator GoToLevel2()
    {
        Transitioning = true;
        Sfx.Victory();
        Sfx.DelayCall(20f);
        hud.Message("Level 1 complete", 2f);
        yield return Fade(0f, 1f);
        EnterLevel2(true);
        yield return new WaitForSecondsRealtime(0.35f);
        hud.Message(Level2Objective, 6f);
        yield return Fade(1f, 0f);
        Transitioning = false;
    }

    const string Level1Objective = "Find 2 keys to open the exit gate: one gunman has a key, the other is hidden in a random house";
    const string Level2Objective = "Find 2 keys to open the arena gate: one Bone Thrower has one, one Shield Skeleton has the other";

    // the final key was picked up: the boss fight will start here
    public void FinalKeyTaken()
    {
        hud.Message("You found the final key!", 5f);
        hud.AreaBanner(finalBannerText);
        StartBoss();
    }

    // the boss appears in the arena; dying from now on restarts straight into this fight
    void StartBoss()
    {
        if (boss == null || bossStarted) return;
        bossStarted = true;
        Checkpoint = Mathf.Max(Checkpoint, 4);
        boss.gameObject.SetActive(true);
        hud.ShowBoss(boss.bossName);
    }

    public void BossDefeated(BossAI who)
    {
        hud.HideBoss();
        if (who == boss && boss2 != null)
        {
            // the knight is down: health and ammo appear in the arena, and the real creature comes in a minute
            Checkpoint = Mathf.Max(Checkpoint, 5);
            knightDead = true;
            hud.Message("THE CREATURE KNIGHT IS DEAD! Health and ammo have appeared in the arena.", 5f);
            if (knightReward != null) knightReward.SetActive(true);
            StartCoroutine(Boss2After(boss2Delay));
            return;
        }
        Checkpoint = 1;   // the next game starts from the beginning
        won = true;
        StartCoroutine(Victory());
    }

    IEnumerator Boss2After(float seconds)
    {
        float end = Time.time + seconds;
        yield return new WaitForSeconds(Mathf.Min(6f, seconds * 0.5f));   // let the first message be read
        while (Time.time < end)
        {
            if (Ended) yield break;
            int left = Mathf.CeilToInt(end - Time.time);
            hud.Message("Gear up! Something worse is coming... " + left, 1.2f);
            yield return new WaitForSeconds(1f);
        }
        if (Ended || boss2 == null) yield break;
        hud.AreaBanner(boss2BannerText);
        Sfx.ZombieCall();
        boss2.gameObject.SetActive(true);
        hud.ShowBoss(boss2.bossName);
    }

    IEnumerator Victory()
    {
        hud.Message("The creature is dead!", 3f);
        yield return new WaitForSecondsRealtime(3f);
        if (Ended) yield break;
        Ended = true;
        Sfx.Victory();
        string sub = "The creature is dead. Press Enter for the main menu";
        if (!Theme.Morning && !Theme.DayUnlocked) { Theme.DayUnlocked = true; sub = "You beat the NIGHT! DAY theme unlocked in the main menu.  Press Enter"; }
        hud.ShowEnd("YOU SURVIVED", sub, new Color(0.3f, 0.8f, 0.4f));
    }

    public void AddKey()
    {
        Keys++;
        hud.SetKeys(Keys, requiredKeys);
        string gateName = Level >= 2 ? "arena gate" : "exit gate";
        hud.Message(Keys >= requiredKeys ? "You have both keys. Head for the " + gateName + "." : "Key found. One more to go.", 4f);
    }

    public void PlayerDied()
    {
        if (Ended) return;
        Ended = true;
        Sfx.Defeat();
        hud.ShowEnd("YOU DIED", "Press Enter to restart", new Color(0.75f, 0.1f, 0.1f));
    }

    void Update()
    {
        if (InMenu) return;
        // a far-off zombie call the first time each area is entered
        if (!Ended && !Transitioning && player != null && !player.Dead)
        {
            int area = AreaAt(player.transform.position);
            if (area >= 0 && (areasSeen & (1 << area)) == 0)
            {
                areasSeen |= 1 << area;
                Sfx.ZombieCall();
                if (area == 1) hud.AreaBanner(areaBannerText);   // the red text: first area only ...
                if (area == 5)
                {
                    hud.SetTopRight("Stage 3"); hud.Message("Stage 3", 4f);
                    Checkpoint = Mathf.Max(Checkpoint, 3);   // dying from now on restarts at the arena gate
                    if (Checkpoint < 4 && arenaSpawners != null) foreach (var sp in arenaSpawners) if (sp != null) sp.Begin();
                    if (Checkpoint < 4 && stage3Crawler != null) StartCoroutine(SpringLater(stage3Crawler, 7f));
                }
            }
            if (Level >= 2 && !cornerAnnounced)
            {
                Vector3 toCorner = player.transform.position - level2Corner; toCorner.y = 0;
                if (toCorner.magnitude < 8f)
                {
                    cornerAnnounced = true;                         // ... and at the left turn of level 2
                    Sfx.ZombieCall();
                    hud.AreaBanner(villageBannerText);
                }
            }
        }

        // hard: once the player is well inside the arena, its main gate slams shut (easy: it stays open, a way back out)
        if (Hard && !arenaSealed && Level >= 2 && arenaGate != null && player != null && player.transform.position.x > arenaGateX + 5f)
        {
            arenaSealed = true;
            arenaGate.CloseAndSeal(true);
            hud.Message("The arena gate slams shut behind you! There is no way out.", 4f);
        }

        // stage 3 cleared: every enemy from both arena graveyards raised and killed
        // the house shuts once the player has come out to face the boss, so the fight cannot be waited out inside
        if (bossStarted && houseBlock != null && !houseBlock.activeSelf && player != null && player.transform.position.x < houseDoorX - 2f)
        {
            houseBlock.SetActive(true);
            Sfx.PlayAt(Sfx.I.gate, houseBlock.transform.position, 1f);
            hud.Message("The house door slams shut behind you!", 3f);
        }

        if (!finalKeyShown && Level >= 2 && finalKey != null && arenaSpawners != null && arenaSpawners.Length > 0)
        {
            bool clear = true;
            foreach (var sp in arenaSpawners) if (sp != null && !sp.Cleared) clear = false;
            if (clear)
            {
                finalKeyShown = true;
                finalKey.SetActive(true);
                hud.Message("The arena is clear. A key has appeared in the house.", 6f);
                Sfx.Play(Sfx.I.key, 1f);
            }
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        if (!Ended && !Transitioning && Level == 1 && ExitOpen && player != null && !player.Dead && player.transform.position.z > winZ)
            StartCoroutine(GoToLevel2());
        if (Ended && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
        {
            ShowMenu = won;   // after a win back to the main menu; after dying straight back in at the checkpoint
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        // Esc pauses; P does too (in a web browser Esc only frees the mouse and never reaches the game)
        if ((kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) && !Ended && !Transitioning && menu != null)
        {
            if (!Paused) SetPaused(true);
            else menu.Back();   // closes the controls page first, then resumes
        }
    }

    IEnumerator SpringLater(AmbushTrigger t, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (t != null && !Ended) t.Spring();
    }

    // an exploder stands guard a few metres from a key
    public void SpawnExploderNear(Vector3 keyPos)
    {
        if (exploderTemplate == null) return;
        Vector3 pos = new Vector3(keyPos.x + 4f, 0f, keyPos.z);
        for (int i = 0; i < 12; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 q = new Vector3(keyPos.x + Mathf.Cos(a) * Random.Range(3.5f, 5f), 0f, keyPos.z + Mathf.Sin(a) * Random.Range(3.5f, 5f));
            if (NavMesh.SamplePosition(q, out NavMeshHit h, 1.5f, NavMesh.AllAreas)) { pos = h.position; break; }
        }
        var go = Instantiate(exploderTemplate, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = "Enemy_Exploder";
        go.SetActive(true);
    }

    // story intro: the stage-2 enemies stand in the hamlet for the camera, then go back into hiding
    public void ShowLevel2ForIntro(bool on) { if (level2Enemies != null && Level < 2) level2Enemies.SetActive(on); }

    // ---- pause menu ----

    public void SetPaused(bool p)
    {
        if (Paused == p || InMenu) return;
        Paused = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;
        Cursor.lockState = p ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = p;
        hud.ShowGameplay(!p);
        if (menu != null) { if (p) menu.OpenPause(StageName, Objective); else menu.ClosePause(); }
    }

    public void RestartCheckpoint()
    {
        Time.timeScale = 1f; AudioListener.pause = false;
        ShowMenu = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f; AudioListener.pause = false;
        ShowMenu = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public string StageName
    {
        get
        {
            string mode = Hard ? "   (HARD)" : "   (EASY)";
            if (Level < 2) return "STAGE 1  -  THE VILLAGE" + mode;
            if (Checkpoint >= 4) return "FINAL STAGE  -  THE ARENA" + mode;
            if (Checkpoint >= 3) return "STAGE 3  -  THE ARENA" + mode;
            return "STAGE 2  -  THE HAMLET" + mode;
        }
    }

    // what the player has to do right now (shown in the pause menu)
    public string Objective
    {
        get
        {
            if (Level < 2)
                return Keys >= requiredKeys ? "You have both keys. Go through the exit gate at the north end of the village."
                                            : Level1Objective + ".  (" + Keys + " / " + requiredKeys + " found)";
            if (boss2 != null && boss2.gameObject.activeSelf)
                return "Kill THE REAL CREATURE. Step out of the circles on the ground before he strikes. If a gunman appears on the house roof, keep moving: his glowing shots can be dodged.";
            if (knightDead) return "Gear up: full health and ammo are in the arena. Something worse is coming...";
            if (bossStarted) return "Kill THE CREATURE KNIGHT. Step out of the red circles before his axe lands.";
            if (finalKeyShown) return "The arena is clear. Take the final key from the house at the far end of the arena.";
            if (Checkpoint >= 3) return "Survive the arena: kill every skeleton rising from the two graveyards beside it. Ammo lies in both graveyards.";
            return Keys >= requiredKeys ? "You have both keys. Open the arena gate at the end of the east lane."
                                        : Level2Objective + ".  (" + Keys + " / " + requiredKeys + " found)";
        }
    }

    // 0 start camp and path, 1 route past the main gate, 2 village square, 3 level-2 lanes, 4 hamlet, 5 arena (stage 3)
    int AreaAt(Vector3 p)
    {
        if (Level >= 2)
        {
            if (p.x > arenaGateX) return 5;
            Vector3 d = p - hamletCentre; d.y = 0;
            return d.magnitude < hamletRadius + 1f ? 4 : 3;
        }
        if (p.z >= 5f) return 2;
        if (p.z > -43f && p.x > -30f) return 1;
        return 0;
    }

    public void HitSparks(Vector3 point, bool big)
    {
        if (hitFx == null) return;
        var ep = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = true };
        if (big) ep.startSize = Random.Range(0.18f, 0.34f);
        hitFx.Emit(ep, big ? 28 : 10);
    }

    // Wakes every enemy within a radius of a point (used when one notices the player or a shot lands).
    public static void AlertNear(Vector3 point, float radius)
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (e != null && (e.transform.position - point).sqrMagnitude < radius * radius) e.Alert();
        }
    }
}
