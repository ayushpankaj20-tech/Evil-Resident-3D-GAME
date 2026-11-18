using UnityEngine;
using UnityEngine.AI;

// Melee enemy: wanders near its spot until it notices the player, then follows for the rest of the level.
// Special kinds:
//   Shield   - carries a big shield: bullets hitting it from the front are blocked; shoot it from the side or back.
//              It turns slowly, so the player can walk round it.
//   Thrower  - keeps its distance and throws slow, arcing bones at where the player stands (they can be dodged).
//   Crawler  - the jump scare: rises from the ground right beside the player and runs fast, but dies to one bullet.
//   Exploder - glows red; when it gets close it stops and a red circle grows for 1 second, then it bursts.
//              The burst hurts the player (2 hits) and every enemy inside the circle (2 bullets' worth).
//              Shooting it down makes it burst at once, so kill it from far away.
public class EnemyAI : MonoBehaviour, IDamageable
{
    public enum Kind { Normal, Shield, Thrower, Crawler, Exploder }
    public Kind kind = Kind.Normal;
    public float riseSpeed = 1.6f;            // animation speed of rising from the ground (crawler: fast)
    [Header("Thrower")]
    public float throwRange = 17f, keepAway = 7f, throwCooldown = 2.6f;
    public Material boneMaterial;
    [Header("Exploder")]
    public float fuseTime = 1f, blastRadius = 3.6f, fuseStartRange = 2.6f;
    public Material warningMaterial;
    public bool big;
    public bool armed;
    public int minHits = 5, maxHits = 7;      // bullets needed to kill (picked at random)
    public int damage = 1;                    // hits dealt to the player per attack
    public float wanderSpeed = 1.1f;
    public float chaseSpeed = 3.3f;
    public float wanderRadius = 5f;
    public float noticeRadius = 13f;
    public float attackRange = 1.9f;
    public float attackCooldown = 1.0f;
    [Range(0, 1)] public float ammoDropChance = 0.5f;
    public bool riseFromGround;               // set by the graveyard spawner
    public bool chaseOnStart;                 // set for enemies that burst out of a house
    public GameObject keyToDrop;              // hidden key that appears where this enemy dies
    public int minPainCries = 2, maxPainCries = 4;   // how many times this enemy cries out when shot (big ones: 2-3)

    enum State { Rising, Wander, Chase, Attack, Throw, Fuse, Dead }
    State state = State.Wander;
    NavMeshAgent agent;
    CharAnim anim;
    PlayerController player;
    Vector3 home;
    int health, painCriesLeft;
    float stateUntil, nextRepath, nextAttack, waitUntil, stunUntil, nextGrowl;
    bool hitDone;
    HitFlash flash;
    float nextThrow, nextBlockNote;
    Transform disc;
    static float nextShieldHint;

    // hard mode: every enemy moves a little faster
    float ChaseSpeedNow { get { return chaseSpeed * (GameManager.Hard ? 1.15f : 1f); } }

    string WalkClip { get { return big ? "Walking_A" : "Walking_D_Skeletons"; } }
    string ChaseClip { get { return big || kind == Kind.Shield ? "Walking_A" : "Running_A"; } }
    string AttackClip { get { return kind == Kind.Shield ? "Block_Attack" : armed ? "1H_Melee_Attack_Chop" : "Unarmed_Melee_Attack_Punch_A"; } }

    void Start()
    {
        player = GameManager.I.player;
        anim = new CharAnim(gameObject);
        flash = gameObject.AddComponent<HitFlash>();
        health = Random.Range(minHits, maxHits + 1);
        painCriesLeft = Random.Range(minPainCries, maxPainCries + 1);
        home = transform.position;
        agent = GetComponent<NavMeshAgent>();
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas)) transform.position = hit.position;
        agent.enabled = true;
        agent.updateRotation = true;
        GameManager.Enemies.Add(this);

        if (riseFromGround)
        {
            state = State.Rising;
            agent.isStopped = true;
            stateUntil = Time.time + anim.Play("Spawn_Ground_Skeletons", WrapMode.ClampForever, 0f, riseSpeed, true);
            Sfx.PlayAt(Sfx.I.spawn, transform.position, 0.8f);
        }
        else if (chaseOnStart) BeginChase();
        else
        {
            waitUntil = Time.time + Random.Range(0f, 2.5f);
            anim.Play("Idle");
        }
    }

    public bool IsDead { get { return state == State.Dead; } }

    void OnDestroy() { GameManager.Enemies.Remove(this); }

    public void Alert()
    {
        if (state == State.Wander) BeginChase();
    }

    void BeginChase()
    {
        state = State.Chase;
        if (agent.isOnNavMesh) agent.isStopped = false;
        agent.speed = ChaseSpeedNow;
        nextRepath = 0f;
    }

    void Update()
    {
        if (state == State.Dead || player == null) return;
        if (player.Dead || GameManager.I.Ended || GameManager.I.InMenu)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            anim.Play("Idle");
            return;
        }
        float dist = Vector3.Distance(transform.position, player.transform.position);

        switch (state)
        {
            case State.Rising:
                if (Time.time >= stateUntil) BeginChase();
                break;

            case State.Wander:
                if (dist < noticeRadius && (dist < 6f || CanSee()))
                {
                    GameManager.AlertNear(transform.position, 9f);
                    BeginChase();
                    break;
                }
                if (!agent.isOnNavMesh) break;
                agent.speed = wanderSpeed;
                bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
                if (arrived)
                {
                    anim.Play("Idle");
                    if (waitUntil == 0f) waitUntil = Time.time + Random.Range(1f, 3.5f);
                    if (Time.time >= waitUntil)
                    {
                        Vector2 r = Random.insideUnitCircle * wanderRadius;
                        if (NavMesh.SamplePosition(home + new Vector3(r.x, 0, r.y), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                            agent.SetDestination(hit.position);
                        waitUntil = 0f;
                    }
                }
                else anim.Play(WalkClip);
                break;

            case State.Chase:
                if (Time.time < stunUntil) { if (agent.isOnNavMesh) agent.isStopped = true; break; }
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                    if (Time.time >= nextRepath) { agent.SetDestination(player.transform.position); nextRepath = Time.time + 0.25f; }
                }
                agent.speed = ChaseSpeedNow;
                if (kind == Kind.Thrower && dist > 4.5f && dist < throwRange && CanSee())
                {
                    // stands off and throws; only walks in when the player is out of range or hidden
                    if (Time.time >= nextThrow) { BeginThrow(); break; }
                    if (dist < keepAway + 4f) { if (agent.isOnNavMesh) agent.isStopped = true; Face(player.transform.position); anim.Play("Idle"); break; }
                }
                if (kind == Kind.Exploder && dist <= fuseStartRange) { BeginFuse(); break; }
                anim.Play(ChaseClip, WrapMode.Loop, 0.15f, big ? 1.5f : kind == Kind.Crawler ? 1.35f : 1f);
                if (!big && kind != Kind.Exploder && Time.time >= nextGrowl)
                {
                    // growls when it starts after the player, then every few seconds
                    nextGrowl = Time.time + (Sfx.ZombieGrowl(transform.position + Vector3.up * 1.3f) ? Random.Range(5f, 10f) : Random.Range(0.8f, 2f));
                }
                if (kind != Kind.Exploder && dist <= attackRange && Time.time >= nextAttack) BeginAttack();
                break;

            case State.Throw:
                Face(player.transform.position);
                if (!hitDone && Time.time >= stateUntil - anim.Length("Throw") * 0.55f)
                {
                    hitDone = true;
                    ThrowBone();
                }
                if (Time.time >= stateUntil) { nextThrow = Time.time + throwCooldown; BeginChase(); }
                break;

            case State.Fuse:
            {
                float k = 1f - Mathf.Clamp01((stateUntil - Time.time) / fuseTime);
                if (disc != null)
                {
                    float r = blastRadius * Mathf.Lerp(0.3f, 1f, k);
                    disc.position = transform.position + Vector3.up * 0.06f;
                    disc.localScale = new Vector3(r * 2f, 0.01f, r * 2f);
                }
                flash.Flash();
                if (Time.time >= stateUntil) Explode();
                break;
            }

            case State.Attack:
                Face(player.transform.position);
                if (!hitDone && Time.time >= stateUntil - anim.Length(AttackClip) * 0.55f / 1.2f)
                {
                    hitDone = true;
                    Sfx.PlayAt(Sfx.I.enemyAttack, transform.position, 0.7f);
                    if (dist <= attackRange + 0.8f) player.TakeDamage(damage);
                }
                if (Time.time >= stateUntil) { nextAttack = Time.time + attackCooldown; BeginChase(); }
                break;
        }
    }

    void BeginAttack()
    {
        state = State.Attack;
        hitDone = false;
        if (agent.isOnNavMesh) agent.isStopped = true;
        stateUntil = Time.time + anim.Play(AttackClip, WrapMode.Once, 0.1f, 1.2f, true);
    }

    void BeginThrow()
    {
        state = State.Throw;
        hitDone = false;
        if (agent.isOnNavMesh) agent.isStopped = true;
        float len = anim.Play("Throw", WrapMode.Once, 0.1f, 1f, true);
        stateUntil = Time.time + (len > 0f ? len : 1.2f);
    }

    // one bone, lobbed in an arc to land where the player stands right now
    void ThrowBone()
    {
        var hand = FindDeep(transform, "handslot.r");
        Vector3 from = hand != null ? hand.position : transform.position + Vector3.up * 1.6f;
        Vector3 to = player.transform.position + Vector3.up * 1.0f;
        float flight = Mathf.Clamp(Vector3.Distance(from, to) / 11f, 0.8f, 1.7f);
        const float g = 14f;
        Vector3 v = (to - from) / flight + Vector3.up * (0.5f * g * flight);

        var b = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(b.GetComponent<Collider>());
        b.name = "Thrown Bone";
        b.transform.position = from;
        b.transform.localScale = new Vector3(0.14f, 0.32f, 0.14f);
        var r = b.GetComponent<Renderer>();
        if (boneMaterial != null) r.sharedMaterial = boneMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var l = b.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.95f, 0.8f); l.intensity = 6f; l.range = 3.5f;
        var sb = b.AddComponent<SoulBullet>();
        sb.velocity = v; sb.gravity = g; sb.spin = 720f;
        sb.damage = 1; sb.shooter = transform; sb.life = 4f;
        Sfx.PlayAt(Sfx.I.enemyAttack, from, 0.7f);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    void BeginFuse()
    {
        state = State.Fuse;
        if (agent.isOnNavMesh) agent.isStopped = true;
        stateUntil = Time.time + fuseTime;
        anim.Play("Cheer", WrapMode.Loop, 0.1f, 2f);
        Sfx.ZombieGrowl(transform.position + Vector3.up);
        var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(d.GetComponent<Collider>());
        d.name = "Blast Warning";
        if (warningMaterial != null) d.GetComponent<Renderer>().sharedMaterial = warningMaterial;
        d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc = d.transform;
    }

    // the burst: 2 hits to the player and 2 bullets' worth to every other enemy inside the circle
    void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector3 c = transform.position + Vector3.up * 0.8f;
        if (disc != null) Destroy(disc.gameObject);
        for (int i = 0; i < 4; i++) GameManager.I.HitSparks(c + Random.insideUnitSphere * 0.8f, true);
        Sfx.PlayAt(Sfx.I.enemyDeath, c, 1f);
        Sfx.PlayAt(Sfx.I.spawn, c, 1f);
        Sfx.Gunshot(c, false);
        var boom = new GameObject("Blast Light").AddComponent<Light>();
        boom.transform.position = c + Vector3.up;
        boom.type = LightType.Point; boom.color = new Color(1f, 0.35f, 0.1f); boom.intensity = 80f; boom.range = 14f;
        Destroy(boom.gameObject, 0.15f);

        Vector3 pf = player.transform.position - transform.position; pf.y = 0;
        if (pf.magnitude <= blastRadius) player.TakeDamage(2);
        var list = new System.Collections.Generic.List<EnemyAI>(GameManager.Enemies);
        foreach (var e in list)
        {
            if (e == null || e == this || e.IsDead) continue;
            Vector3 d = e.transform.position - transform.position; d.y = 0;
            if (d.magnitude <= blastRadius) { var hp = e.transform.position + Vector3.up * 1.2f; e.TakeHit(hp); if (!e.IsDead) e.TakeHit(hp); }
        }
        if (state != State.Dead) { health = 0; Die(); }
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;   // nothing left of it
        foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
    }
    bool exploded;

    bool CanSee()
    {
        Vector3 eye = transform.position + Vector3.up * 1.4f * transform.localScale.y;
        Vector3 to = player.Chest - eye;
        if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.IsChildOf(player.transform) || hit.collider.GetComponentInParent<EnemyAI>() != null;
        return true;
    }

    void Face(Vector3 point)
    {
        Vector3 d = point - transform.position; d.y = 0;
        if (d.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-(kind == Kind.Shield ? 2f : 10f) * Time.deltaTime));   // the shield turns slowly
    }

    public void TakeHit(Vector3 point)
    {
        if (state == State.Dead) return;
        if (kind == Kind.Shield && state != State.Rising)
        {
            // the shield covers its front: those bullets bounce off
            Vector3 toHit = point - transform.position; toHit.y = 0;
            if (toHit.sqrMagnitude > 0.0001f && Vector3.Dot(toHit.normalized, transform.forward) > 0.35f)
            {
                Sfx.PlayAt(Sfx.I.bulletImpact, point, 1f);
                GameManager.I.HitSparks(point, false);
                if (state == State.Wander) BeginChase();
                if (Time.time >= nextShieldHint)
                {
                    nextShieldHint = Time.time + 12f;
                    GameManager.I.hud.Message("The shield blocks your bullets! Shoot it from the side or the back.", 3f);
                }
                return;
            }
        }
        // the cries are spread at random over the hits this enemy can still take
        bool cry = painCriesLeft > 0 && Random.value < (float)painCriesLeft / Mathf.Max(1, health);
        health--;
        Sfx.PlayAt(Sfx.I.enemyHit, point, big ? 1f : 0.9f);
        if (cry) { painCriesLeft--; Sfx.ZombiePain(point); }
        flash.Flash();
        GameManager.I.HitSparks(point, big);
        if (health <= 0) { if (kind == Kind.Exploder) { state = State.Chase; Explode(); } else Die(); return; }
        if (state == State.Wander) BeginChase();
        GameManager.AlertNear(transform.position, 9f);
        if (state == State.Chase && kind != Kind.Exploder)
        {
            // small enemies flinch and stop for a moment; big ones only stagger briefly
            anim.Play("Hit_A", WrapMode.Once, 0.05f, big ? 2.2f : 1.6f, true);
            stunUntil = Time.time + (big ? 0.14f : 0.3f);
        }
    }

    void Die()
    {
        state = State.Dead;
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        anim.Play(anim.Has("Death_C_Skeletons") ? "Death_C_Skeletons" : "Death_A", WrapMode.ClampForever, 0.1f, 1.2f, true);
        Sfx.PlayAt(Sfx.I.enemyDeath, transform.position, 1f);
        if (disc != null) Destroy(disc.gameObject);
        if (Random.value < ammoDropChance && GameManager.I.ammoPickupTemplate != null)
        {
            var drop = Instantiate(GameManager.I.ammoPickupTemplate, transform.position + Vector3.up * 0.6f, Quaternion.identity);
            drop.SetActive(true);
        }
        if (keyToDrop != null)
        {
            keyToDrop.transform.position = new Vector3(transform.position.x, keyToDrop.transform.position.y, transform.position.z);
            keyToDrop.SetActive(true);
            GameManager.I.hud.Message("An enemy dropped a key!", 5f);
        }
        GameManager.Enemies.Remove(this);
        Destroy(gameObject, 12f); // dead enemies never come back
    }
}
