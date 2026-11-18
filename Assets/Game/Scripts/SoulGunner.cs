using UnityEngine;

// THE REAL CREATURE's helper, called up when the creature becomes enraged: a glowing skeleton gunman on the
// arena house roof. Unlike the other gunmen he has no laser and his shots are not instant: each one is a slow
// glowing bullet that flies straight at the spot where the player stood when it was fired. A player who keeps
// moving in the open can always dodge them; standing still gets you hit. Big barrels stop the bullets too.
public class SoulGunner : MonoBehaviour, IDamageable
{
    public Vector3 pointA, pointB;          // ends of his walk along the roof ridge
    public Transform muzzle;
    public Transform ownBuilding;           // his own roof never blocks his shots
    public Material bulletMaterial;
    public Color glowColour = new Color(0.35f, 0.75f, 1f);
    public int health = 10;                 // bullets to kill him
    public int damage = 2;
    public float range = 62f;
    public float bulletSpeed = 15f;         // slow: about 2 seconds to cross the arena
    public float chargeTime = 0.9f;         // the glow at the gun grows before each shot
    public float cooldown = 1.7f;
    public float walkSpeed = 1.4f;

    enum State { Appear, Walk, Charge, Cooldown, Dead }
    State state = State.Appear;
    CharAnim anim;
    PlayerController player;
    HitFlash hitFlash;
    Transform charge;
    Light chargeLight;
    Vector3 goal;
    float stateUntil;

    void OnEnable()
    {
        if (anim != null) return;   // set up once
        player = GameManager.I.player;
        anim = new CharAnim(gameObject);
        hitFlash = gameObject.AddComponent<HitFlash>();
        goal = pointB;
        transform.position = OnRoof(transform.position);

        // the charging glow at the gun
        var c = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(c.GetComponent<Collider>());
        c.name = "Charge";
        c.GetComponent<Renderer>().sharedMaterial = bulletMaterial;
        c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        charge = c.transform;
        charge.SetParent(muzzle, false);
        charge.localScale = Vector3.zero;
        chargeLight = c.AddComponent<Light>();
        chargeLight.type = LightType.Point; chargeLight.color = glowColour; chargeLight.range = 6f; chargeLight.intensity = 0f;

        float t = anim.Play("Skeletons_Awaken_Standing", WrapMode.Once, 0.05f, 1f, true);
        stateUntil = Time.time + (t > 0f ? t : 1.2f);
        GameManager.I.hud.Message("A SKELETON GUNMAN rises on the house roof! Keep moving to dodge his glowing shots.", 5f);
        Sfx.PlayAt(Sfx.I.spawn, transform.position, 1f);
    }

    void Update()
    {
        if (state == State.Dead || player == null) return;
        if (player.Dead || GameManager.I.Ended) { SetCharge(0f); anim.Play("Idle"); return; }

        bool canShoot = Vector3.Distance(transform.position, player.Chest) < range && CanSee();

        switch (state)
        {
            case State.Appear:
                Face(player.transform.position);
                if (Time.time >= stateUntil) state = State.Walk;
                break;

            case State.Walk:
                if (canShoot) { BeginCharge(); break; }
                anim.Play("Walking_A");
                transform.position = OnRoof(Vector3.MoveTowards(transform.position, goal, walkSpeed * Time.deltaTime));
                Face(goal);
                if (Flat(transform.position - goal).sqrMagnitude < 0.05f) goal = goal == pointA ? pointB : pointA;
                break;

            case State.Charge:
                Face(player.transform.position);
                anim.Play("1H_Ranged_Aiming");
                SetCharge(1f - Mathf.Clamp01((stateUntil - Time.time) / chargeTime));
                if (Time.time >= stateUntil) Fire();
                break;

            case State.Cooldown:
                Face(player.transform.position);
                // he shuffles along the ridge between shots, so he is not a sitting target either
                transform.position = OnRoof(Vector3.MoveTowards(transform.position, goal, walkSpeed * 0.6f * Time.deltaTime));
                if (Flat(transform.position - goal).sqrMagnitude < 0.05f) goal = goal == pointA ? pointB : pointA;
                if (Time.time >= stateUntil) state = State.Walk;
                break;
        }
    }

    void BeginCharge()
    {
        state = State.Charge;
        stateUntil = Time.time + chargeTime;
    }

    void SetCharge(float k)
    {
        if (charge == null) return;
        charge.localScale = Vector3.one * (0.35f * k / Mathf.Max(0.01f, transform.lossyScale.x));
        chargeLight.intensity = 30f * k;
    }

    void Fire()
    {
        SetCharge(0f);
        anim.Play("1H_Ranged_Shoot", WrapMode.Once, 0.03f, 1.2f, true);
        Sfx.Gunshot(muzzle.position, false);

        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(b.GetComponent<Collider>());
        b.name = "Soul Bullet";
        b.transform.position = muzzle.position;
        b.transform.localScale = Vector3.one * 0.38f;
        var r = b.GetComponent<Renderer>();
        r.sharedMaterial = bulletMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var l = b.AddComponent<Light>();
        l.type = LightType.Point; l.color = glowColour; l.intensity = 22f; l.range = 7f;
        var trail = b.AddComponent<TrailRenderer>();
        trail.sharedMaterial = bulletMaterial; trail.time = 0.35f;
        trail.startWidth = 0.32f; trail.endWidth = 0f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var sb = b.AddComponent<SoulBullet>();
        sb.velocity = (player.Chest - muzzle.position).normalized * bulletSpeed;   // where the player is now: no leading
        sb.damage = damage;
        sb.shooter = transform;
        sb.ownBuilding = ownBuilding;

        state = State.Cooldown;
        stateUntil = Time.time + cooldown;
    }

    bool CanSee()
    {
        Vector3 from = muzzle.position, to = player.Chest, dir = to - from;
        float dist = dir.magnitude;
        var hits = Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var t = h.collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(player.transform) || h.collider.gameObject.layer == 2) continue;
            if (ownBuilding != null && t.IsChildOf(ownBuilding)) continue;
            if (h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<BossAI>() != null) continue;
            return false;
        }
        return true;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    Vector3 OnRoof(Vector3 pos)
    {
        var hits = Physics.RaycastAll(new Vector3(pos.x, pos.y + 6f, pos.z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits) if (!h.collider.transform.IsChildOf(transform) && h.point.y > best) best = h.point.y;
        if (best > float.MinValue) pos.y = best;
        return pos;
    }

    void Face(Vector3 point)
    {
        Vector3 d = Flat(point - transform.position);
        if (d.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    public void TakeHit(Vector3 point)
    {
        if (state == State.Dead || state == State.Appear) return;
        health--;
        Sfx.PlayAt(Sfx.I.enemyHit, point, 1f);
        hitFlash.Flash();
        GameManager.I.HitSparks(point, false);
        if (health <= 0) Collapse();
    }

    // shot down, or his master is dead
    public void Collapse()
    {
        if (state == State.Dead) return;
        state = State.Dead;
        SetCharge(0f);
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        anim.Play("Death_A", WrapMode.ClampForever, 0.1f, 1f, true);
        Sfx.PlayAt(Sfx.I.enemyDeath, transform.position, 1f);
        foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
    }
}

// One slow glowing bullet. Flies straight; hurts the player if it passes through him, stops on anything solid.
public class SoulBullet : MonoBehaviour
{
    public Vector3 velocity;
    public int damage = 2;
    public Transform shooter, ownBuilding;
    public float life = 7f;
    public float gravity;      // 0 = straight line; the bone throwers' bones fall in an arc
    public float spin;         // degrees per second (thrown bones tumble)
    float dieAt;

    void Start() { dieAt = Time.time + life; }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (Time.time > dieAt || GameManager.I == null || GameManager.I.Ended) { Destroy(gameObject); return; }
        Vector3 a = transform.position, step = velocity * dt;

        var p = GameManager.I.player;
        if (p != null && !p.Dead)
        {
            // closest point of this frame's flight to the player's body
            Vector3 c = p.Chest;
            float t = Mathf.Clamp01(Vector3.Dot(c - a, step) / step.sqrMagnitude);
            Vector3 q = a + step * t;
            Vector3 flat = q - p.transform.position; float h = flat.y; flat.y = 0;
            if (flat.magnitude < 0.55f && h > 0.1f && h < 2.0f)
            {
                p.TakeDamage(damage);
                GameManager.I.HitSparks(q, false);
                Destroy(gameObject);
                return;
            }
        }

        var hits = Physics.RaycastAll(a, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 point = Vector3.zero;
        foreach (var hit in hits)
        {
            var tr = hit.collider.transform;
            if (shooter != null && tr.IsChildOf(shooter)) continue;
            if (ownBuilding != null && tr.IsChildOf(ownBuilding)) continue;
            if (p != null && tr.IsChildOf(p.transform)) continue;
            if (hit.collider.gameObject.layer == 2) continue;
            if (hit.collider.GetComponentInParent<EnemyAI>() != null || hit.collider.GetComponentInParent<BossAI>() != null) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; }
        }
        if (best < float.MaxValue)
        {
            GameManager.I.HitSparks(point, false);
            Sfx.PlayAt(Sfx.I.bulletImpact, point, 0.8f);
            Destroy(gameObject);
            return;
        }
        transform.position = a + step;
        velocity += Vector3.down * gravity * dt;
        if (spin != 0f) transform.Rotate(spin * dt, spin * 0.37f * dt, 0f, Space.Self);
    }
}
