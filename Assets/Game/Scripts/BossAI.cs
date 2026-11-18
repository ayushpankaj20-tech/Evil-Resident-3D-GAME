using UnityEngine;
using UnityEngine.AI;

// The final boss: a giant barbarian with a skeleton's face. Slow and very tough; every attack is warned
// by a red circle on the ground, so a player who watches the circle can always step out in time.
// Phase 1 (full health):    walks after the player and slams the axe down in front of him.
// Phase 2 (half health):    roars, calls skeletons up from the arena graveyards, moves faster.
// Phase 3 (quarter health): enraged: glows brighter, moves faster still, mixes slams with a spinning attack.
// Even at his fastest he is slower than the running player.
public class BossAI : MonoBehaviour, IDamageable
{
    public string bossName = "THE CREATURE";
    public int maxHealth = 60;                               // bullets to kill
    public int slamDamage = 3, spinDamage = 2;               // hits taken by the player (he dies on the 7th)
    public float[] chaseSpeed = { 2.4f, 3.0f, 3.6f };        // per phase; the player walks 3.6 and runs 6.2
    public float[] attackPause = { 2.4f, 1.8f, 1.3f };       // rest after each attack, per phase
    public float[] windUpSpeed = { 0.5f, 0.6f, 0.7f };       // attack animation speed: slower = longer warning
    public float attackRange = 5f;                           // starts an attack when the player is this close
    public float slamReach = 2.8f, slamRadius = 3.2f;        // the slam lands this far in front, this wide
    public float spinRadius = 4.6f;                          // the spin hits all round him
    public int summonCount = 4;                              // skeletons called up in phase 2
    public GameObject bigSummon;
    public GameObject enragedHelper;                         // optional: appears when he becomes enraged (the roof gunman)
    public Transform headBar, headBarFill;                   // optional: a health bar standing in for his head
    public float headBarWidth = 1f;                             // optional: one big skeleton called up with them
    public Renderer telegraph;                               // the red warning circle
    public Light glowLamp;
    public string introClip = "Cheer", walkClip = "Walking_A", runClip = "Running_A", idleClip = "2H_Melee_Idle", deathClip = "Death_A";
    public float walkAnimPace = 2.9f, runAnimPace = 5.2f;   // ground speed at which the walk/run animations look right at this size
    public Color glowLight = new Color(1f, 0.3f, 0.12f);
    [Header("What he says to the player")]
    public string introLine = "", summonLine = "", enragedLine = "", deathLine = "", playerDeadLine = "";
    public string[] taunts = new string[0];
    public float tauntGap = 16f;
    float nextTaunt;
    bool screamed, gloated;
    public Color[] glowColour = { new Color(0.35f, 0.12f, 0.08f), new Color(0.5f, 0.12f, 0.06f), new Color(0.9f, 0.1f, 0.05f) };

    enum State { Intro, Chase, Attack, Recover, Roar, Dead }
    State state = State.Intro;
    int phase, health;
    float stateUntil, hitAt, nextRepath, nextPain;
    bool hitDone, spinning, summoned;
    Vector3 strikeCentre;
    float strikeRadius;
    NavMeshAgent agent;
    CharAnim anim;
    HitFlash flash;
    PlayerController player;
    Material[] glowMats;
    static readonly int Emissive = Shader.PropertyToID("emissiveFactor");

    public float Health01 { get { return Mathf.Clamp01((float)health / maxHealth); } }

    void Start()
    {
        player = GameManager.I.player;
        anim = new CharAnim(gameObject);
        flash = gameObject.AddComponent<HitFlash>();
        health = maxHealth;
        agent = GetComponent<NavMeshAgent>();
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) transform.position = hit.position;
        agent.enabled = true;
        agent.isStopped = true;

        // own copies of the glowing materials, so the glow can change per phase
        var list = new System.Collections.Generic.List<Material>();
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r != telegraph && !r.name.Contains("Eyes") && (headBar == null || !r.transform.IsChildOf(headBar))) list.AddRange(r.materials);
        glowMats = list.ToArray();
        SetGlow(0);

        // a roar to announce himself
        stateUntil = Time.time + anim.Play(introClip, WrapMode.Once, 0.1f, 0.8f, true);
        Sfx.ZombieCall();
        GameManager.I.hud.SetBoss(1f);
        Say(introLine);
        nextTaunt = Time.time + tauntGap;
    }

    void Say(string line, float seconds = 4.5f)
    {
        if (string.IsNullOrEmpty(line)) return;
        GameManager.I.hud.BossSay(bossName, line, seconds);
        nextTaunt = Time.time + tauntGap;
    }

    void SetGlow(int p)
    {
        foreach (var m in glowMats) if (m != null && m.HasProperty(Emissive)) m.SetColor(Emissive, glowColour[p]);
        if (glowLamp != null) { glowLamp.color = glowLight; glowLamp.intensity = 25f + 20f * p; glowLamp.range = 11f + 3f * p; }
    }

    void Update()
    {
        if (state == State.Dead || player == null) return;
        if (glowLamp != null) glowLamp.intensity = (25f + 20f * phase) * (0.85f + 0.15f * Mathf.Sin(Time.time * 4f));
        if (player.Dead && !gloated) { gloated = true; Say(playerDeadLine, 6f); }
        if (state == State.Chase && taunts.Length > 0 && Time.time >= nextTaunt) Say(taunts[Random.Range(0, taunts.Length)], 4f);
        if (player.Dead || GameManager.I.Ended || GameManager.I.InMenu)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            HideWarning();
            anim.Play(idleClip);
            return;
        }

        Vector3 toPlayer = player.transform.position - transform.position; toPlayer.y = 0;
        float dist = toPlayer.magnitude;

        switch (state)
        {
            case State.Intro:
            case State.Roar:
                Face(player.transform.position, 3f);
                if (Time.time >= stateUntil)
                {
                    if (state == State.Roar && phase == 1 && !summoned) Summon();
                    BeginChase();
                }
                break;

            case State.Chase:
                if (agent.isOnNavMesh && Time.time >= nextRepath)
                {
                    agent.SetDestination(player.transform.position);
                    nextRepath = Time.time + 0.3f;
                }
                anim.Play(phase == 2 ? runClip : walkClip, WrapMode.Loop, 0.2f, Speed / (phase == 2 ? runAnimPace : walkAnimPace));
                if (dist <= attackRange) BeginAttack(phase == 2 && Random.value < 0.45f);
                break;

            case State.Attack:
                if (Time.time < hitAt - 0.35f) Face(player.transform.position, 6f);   // tracks the player until just before the blow
                if (!spinning) strikeCentre = transform.position + transform.forward * slamReach;
                ShowWarning(1f - Mathf.Clamp01((hitAt - Time.time) / Mathf.Max(0.01f, hitAt - (stateUntil - AttackLength()))));
                if (!hitDone && Time.time >= hitAt)
                {
                    hitDone = true;
                    HideWarning();
                    Sfx.PlayAt(Sfx.I.enemyDeath, strikeCentre, 1f);
                    Sfx.PlayAt(Sfx.I.spawn, strikeCentre, 1f);
                    GameManager.I.HitSparks(strikeCentre + Vector3.up * 0.2f, true);
                    Vector3 d = player.transform.position - strikeCentre; d.y = 0;
                    if (d.magnitude <= strikeRadius) player.TakeDamage(spinning ? spinDamage : slamDamage);
                }
                if (Time.time >= stateUntil)
                {
                    state = State.Recover;
                    stateUntil = Time.time + attackPause[phase];
                    anim.Play(idleClip, WrapMode.Loop, 0.2f);
                    CheckPhase();   // a phase change waits until the swing is over
                }
                break;

            case State.Recover:
                Face(player.transform.position, 2f);
                if (Time.time >= stateUntil) BeginChase();
                break;
        }
    }

    void LateUpdate()
    {
        if (headBar == null) return;
        // the head bar turns to face the camera and shows his health
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 d = headBar.position - cam.transform.position; d.y = 0;
            if (d.sqrMagnitude > 0.01f) headBar.rotation = Quaternion.LookRotation(d);
        }
        if (headBarFill != null)
        {
            float h = Health01;
            headBarFill.localScale = new Vector3(headBarWidth * Mathf.Max(0.001f, h), headBarFill.localScale.y, headBarFill.localScale.z);
            headBarFill.localPosition = new Vector3(-headBarWidth * (1f - h) * 0.5f, headBarFill.localPosition.y, headBarFill.localPosition.z);
        }
    }

    float Speed { get { return chaseSpeed[phase] * (GameManager.Hard ? 2f : 1f); } }   // hard mode: twice as fast

    float attackLen;
    float AttackLength() { return attackLen; }

    void BeginChase()
    {
        state = State.Chase;
        if (agent.isOnNavMesh) { agent.isStopped = false; agent.speed = Speed; }
        nextRepath = 0f;
    }

    void BeginAttack(bool spin)
    {
        state = State.Attack;
        spinning = spin;
        hitDone = false;
        if (agent.isOnNavMesh) agent.isStopped = true;
        string clip = spin ? "2H_Melee_Attack_Spin" : "2H_Melee_Attack_Chop";
        attackLen = anim.Play(clip, WrapMode.Once, 0.1f, windUpSpeed[phase], true);
        if (attackLen <= 0f) attackLen = 1.6f;
        stateUntil = Time.time + attackLen;
        hitAt = Time.time + attackLen * (spin ? 0.55f : 0.62f);
        strikeRadius = spin ? spinRadius : slamRadius;
        strikeCentre = spin ? transform.position : transform.position + transform.forward * slamReach;
    }

    void ShowWarning(float grow)
    {
        if (telegraph == null) return;
        if (!telegraph.gameObject.activeSelf) telegraph.gameObject.SetActive(true);
        float r = strikeRadius * Mathf.Lerp(0.25f, 1f, grow);
        telegraph.transform.position = new Vector3(strikeCentre.x, transform.position.y + 0.06f, strikeCentre.z);
        telegraph.transform.localScale = new Vector3(r * 2f, 0.01f, r * 2f);
    }

    void HideWarning() { if (telegraph != null) telegraph.gameObject.SetActive(false); }

    void Face(Vector3 point, float rate)
    {
        Vector3 d = point - transform.position; d.y = 0;
        if (d.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    void Roar(int newPhase)
    {
        phase = newPhase;
        SetGlow(phase);
        HideWarning();
        state = State.Roar;
        if (agent.isOnNavMesh) agent.isStopped = true;
        stateUntil = Time.time + anim.Play("Cheer", WrapMode.Once, 0.1f, 0.9f, true);
        Sfx.ZombieCall();
        GameManager.I.hud.Message(phase == 1 ? bossName + " calls the dead!" : bossName + " is enraged!", 4f);
        Say(phase == 1 ? summonLine : enragedLine, 5f);
        if (phase == 2 && !screamed) { screamed = true; Sfx.BossScream(); }   // the scream, once
        if (phase == 2 && enragedHelper != null) enragedHelper.SetActive(true);
    }

    // phase 2: skeletons rise from the arena graveyards
    void Summon()
    {
        summoned = true;
        var spawners = GameManager.I.arenaSpawners;
        if (spawners == null || spawners.Length == 0) return;
        for (int i = 0; i < summonCount; i++)
        {
            var sp = spawners[i % spawners.Length];
            if (sp == null || sp.template == null || sp.points == null || sp.points.Length == 0) continue;
            Vector3 p = sp.points[Random.Range(0, sp.points.Length)] + new Vector3(Random.Range(-0.6f, 0.6f), 0, Random.Range(-0.6f, 0.6f));
            var go = Instantiate(sp.template, p, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.name = "Enemy_Summoned_" + (i + 1);
            go.GetComponent<EnemyAI>().riseFromGround = true;
            go.SetActive(true);
        }
        if (bigSummon != null)
        {
            // the big one rises a few metres in front of the boss
            Vector3 p = transform.position + transform.forward * 5f;
            if (NavMesh.SamplePosition(p, out NavMeshHit h, 5f, NavMesh.AllAreas)) p = h.position;
            var big = Instantiate(bigSummon, p, transform.rotation);
            big.name = "Enemy_Summoned_Big";
            big.GetComponent<EnemyAI>().riseFromGround = true;
            big.SetActive(true);
        }
    }

    public void TakeHit(Vector3 point)
    {
        if (state == State.Dead || state == State.Intro) return;
        health--;
        flash.Flash();
        Sfx.PlayAt(Sfx.I.enemyHit, point, 1f);
        GameManager.I.HitSparks(point, true);
        if (Time.time >= nextPain) { Sfx.ZombiePain(point); nextPain = Time.time + Random.Range(2.5f, 4.5f); }
        GameManager.I.hud.SetBoss(Health01);
        if (health <= 0) { Die(); return; }
        if (state != State.Attack) CheckPhase();
    }

    void CheckPhase()
    {
        if (state == State.Roar) return;
        if (phase == 0 && health <= maxHealth / 2) Roar(1);
        else if (phase == 1 && health <= maxHealth / 4) Roar(2);
    }

    void Die()
    {
        state = State.Dead;
        HideWarning();
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        anim.Play(deathClip, WrapMode.ClampForever, 0.1f, 0.7f, true);
        Sfx.PlayAt(Sfx.I.enemyDeath, transform.position, 1f);
        if (glowLamp != null) glowLamp.enabled = false;
        if (headBar != null) headBar.gameObject.SetActive(false);
        foreach (var m in glowMats) if (m != null && m.HasProperty(Emissive)) m.SetColor(Emissive, Color.black);
        if (enragedHelper != null && enragedHelper.activeSelf) { var g = enragedHelper.GetComponent<SoulGunner>(); if (g != null) g.Collapse(); }
        Say(deathLine, 5f);
        GameManager.I.BossDefeated(this);
    }

    void OnDestroy()
    {
        if (glowMats != null) foreach (var m in glowMats) if (m != null) Destroy(m);
    }
}
