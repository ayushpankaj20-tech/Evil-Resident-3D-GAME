using UnityEngine;

// The one enemy with a gun. Walks the roof ridge, sits at the ends, and shoots when the player is in range.
// A red laser shows where he is aiming before each shot; anything solid in the way (the big barrels) stops the bullet.
public class RoofGunner : MonoBehaviour, IDamageable
{
    public Vector3 pointA, pointB;       // ends of the roof walk
    public Transform muzzle;
    public GameObject keyToDrop;         // hidden key that appears on the ground when he dies
    public GraveyardSpawner wakesOnDeath;    // this graveyard starts raising enemies when he dies
    public string dropMessage = "The gunman dropped a key by the big house door";
    public Transform ownBuilding;        // the house he stands on never blocks his own shots
    public int minHits = 5, maxHits = 7;
    public int damage = 3;               // triple damage
    public float range = 42f;
    public float aimTime = 1.7f;         // laser warning before the shot
    public float lockTime = 0.3f;        // the aim point stops tracking this long before the shot
    public float cooldown = 2.5f;
    public float walkSpeed = 1.2f;
    public float sitTime = 4f;
    public bool needsSight = true;       // only aims when nothing solid is between him and the player

    enum State { Walk, Sit, Aim, Cooldown, Dead }
    State state = State.Walk;
    CharAnim anim;
    PlayerController player;
    LineRenderer laser;
    Light flash;
    Vector3 goal, aimPoint;
    int health;
    float stateUntil, flashUntil;
    HitFlash hitFlash;

    void Start()
    {
        player = GameManager.I.player;
        anim = new CharAnim(gameObject);
        health = Random.Range(minHits, maxHits + 1);
        hitFlash = gameObject.AddComponent<HitFlash>();
        goal = pointB;

        var lg = new GameObject("Laser");
        laser = lg.AddComponent<LineRenderer>();
        laser.positionCount = 2; laser.startWidth = 0.035f; laser.endWidth = 0.035f;
        laser.sharedMaterial = GameManager.I.laserMaterial;
        laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laser.enabled = false;

        var fl = new GameObject("Muzzle Flash");
        fl.transform.SetParent(muzzle, false);
        flash = fl.AddComponent<Light>();
        flash.type = LightType.Point; flash.color = new Color(1f, 0.8f, 0.45f); flash.intensity = 40f; flash.range = 12f;
        flash.enabled = false;
    }

    void OnDestroy() { if (laser != null) Destroy(laser.gameObject); }

    void Update()
    {
        if (flash != null && flash.enabled && Time.time > flashUntil) flash.enabled = false;
        if (state == State.Dead || player == null) return;
        if (player.Dead || GameManager.I.Ended || GameManager.I.InMenu) { laser.enabled = false; anim.Play("Idle"); return; }

        bool inRange = Vector3.Distance(transform.position, player.Chest) < range;
        if (inRange && needsSight && state != State.Cooldown) { FirstSolid(muzzle.position, player.Chest, out bool sees); inRange = sees; }

        switch (state)
        {
            case State.Walk:
                if (inRange) { BeginAim(); break; }
                anim.Play("Walking_A");
                Vector3 pos = Vector3.MoveTowards(transform.position, goal, walkSpeed * Time.deltaTime);
                transform.position = OnRoof(pos);
                FaceFlat(goal);
                if (Flat(transform.position - goal).sqrMagnitude < 0.05f)
                {
                    goal = goal == pointA ? pointB : pointA;
                    state = State.Sit;
                    stateUntil = Time.time + sitTime;
                    anim.Play("Sit_Floor_Down", WrapMode.ClampForever, 0.2f, 1f, true);
                }
                break;

            case State.Sit:
                if (inRange) { BeginAim(); break; }
                if (Time.time >= stateUntil) state = State.Walk;
                break;

            case State.Aim:
                if (!inRange) { laser.enabled = false; state = State.Walk; break; }
                if (Time.time < stateUntil - lockTime) aimPoint = player.Chest;
                FaceFlat(aimPoint);
                anim.Play("1H_Ranged_Aiming");
                laser.enabled = true;
                laser.SetPosition(0, muzzle.position);
                laser.SetPosition(1, FirstSolid(muzzle.position, aimPoint, out _));
                if (Time.time >= stateUntil) Shoot();
                break;

            case State.Cooldown:
                FaceFlat(player.Chest);
                if (Time.time >= stateUntil) { if (inRange) BeginAim(); else state = State.Walk; }
                break;
        }
    }

    void BeginAim()
    {
        state = State.Aim;
        stateUntil = Time.time + aimTime;
        aimPoint = player.Chest;
    }

    void Shoot()
    {
        laser.enabled = false;
        anim.Play("1H_Ranged_Shoot", WrapMode.Once, 0.03f, 1.4f, true);
        Sfx.Gunshot(muzzle.position, false);
        flash.enabled = true; flashUntil = Time.time + 0.06f;
        Vector3 end = FirstSolid(muzzle.position, aimPoint, out bool hitPlayer);
        if (hitPlayer) player.TakeDamage(damage);
        else Sfx.PlayAt(Sfx.I.bulletImpact, end, 1f);
        state = State.Cooldown;
        stateUntil = Time.time + cooldown;
    }

    // First thing the shot would hit on the way to the target. Other enemies do not block it.
    Vector3 FirstSolid(Vector3 from, Vector3 to, out bool hitPlayer)
    {
        hitPlayer = false;
        Vector3 dir = (to - from).normalized;
        var hits = Physics.RaycastAll(from, dir, range + 10f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<EnemyAI>() != null) continue;
            if (ownBuilding != null && h.collider.transform.IsChildOf(ownBuilding)) continue;
            if (h.collider.gameObject.layer == 2) continue; // invisible path walls
            hitPlayer = h.collider.transform.IsChildOf(player.transform);
            return h.point;
        }
        return from + dir * (range + 10f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    Vector3 OnRoof(Vector3 pos)
    {
        var hits = Physics.RaycastAll(new Vector3(pos.x, pos.y + 6f, pos.z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
            if (!h.collider.transform.IsChildOf(transform) && h.point.y > best) best = h.point.y;
        if (best > float.MinValue) pos.y = best;
        return pos;
    }

    void FaceFlat(Vector3 point)
    {
        Vector3 d = Flat(point - transform.position);
        if (d.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    public void TakeHit(Vector3 point)
    {
        if (state == State.Dead) return;
        health--;
        Sfx.PlayAt(Sfx.I.enemyHit, point, 1f);
        hitFlash.Flash();
        GameManager.I.HitSparks(point, false);
        if (health > 0) return;
        state = State.Dead;
        laser.enabled = false;
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        anim.Play("Death_A", WrapMode.ClampForever, 0.1f, 1f, true);
        Sfx.PlayAt(Sfx.I.enemyDeath, transform.position, 1f);
        if (wakesOnDeath != null) wakesOnDeath.Begin();
        if (keyToDrop != null)
        {
            keyToDrop.SetActive(true);
            GameManager.I.hud.Message(dropMessage, 5f);
        }
    }
}
