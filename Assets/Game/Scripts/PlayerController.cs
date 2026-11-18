using UnityEngine;
using UnityEngine.InputSystem;

// Third-person movement (no jump), aiming, handgun shooting, reloading and health.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3.6f;
    public float runSpeed = 6.2f;
    public float aimWalkSpeed = 2.4f;  // walking speed while aiming
    public float turnSpeed = 14f;
    public float gravity = -25f;

    [Header("Handgun")]
    public int magazineSize = 12;
    public int magazine = 12;
    public int reserveAmmo = 72;
    public float fireInterval = 0.32f;
    public float range = 120f;
    public Transform muzzle;
    public LayerMask shootMask = ~0;

    [Header("Health")]
    public int hitsToDie = 7;          // the player dies on the 7th hit
    public float hurtCooldown = 0.7f;  // short safety window after a hit

    public ThirdPersonCamera cam;

    public int HitsTaken { get; private set; }
    public bool Dead { get; private set; }
    public Vector3 Chest { get { return transform.position + Vector3.up * 1.3f; } }

    CharacterController cc;
    CharAnim anim;
    Camera mainCam;
    float vy, nextFire, reloadUntil, busyUntil, nextHurt, stepTimer, flashUntil;
    bool reloading;
    Light flash;
    LineRenderer tracer;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        anim = new CharAnim(gameObject);
        mainCam = Camera.main;
        anim.Play("Idle");

        var fl = new GameObject("Muzzle Flash");
        fl.transform.SetParent(muzzle != null ? muzzle : transform, false);
        flash = fl.AddComponent<Light>();
        flash.type = LightType.Point; flash.color = new Color(1f, 0.8f, 0.45f); flash.intensity = 30f; flash.range = 9f;
        flash.enabled = false;

        var tr = new GameObject("Tracer");
        tracer = tr.AddComponent<LineRenderer>();
        tracer.positionCount = 2; tracer.startWidth = 0.05f; tracer.endWidth = 0.02f;
        tracer.sharedMaterial = GameManager.I.tracerMaterial;
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.enabled = false;

        UpdateHud();
        GameManager.I.hud.SetHealth(HitsTaken, hitsToDie);
    }

    void UpdateHud() { GameManager.I.hud.SetAmmo(magazine, reserveAmmo); }

    void Update()
    {
        if (flash.enabled && Time.time > flashUntil) { flash.enabled = false; tracer.enabled = false; }

        // gravity only: there is no jump
        vy = cc.isGrounded ? -2f : vy + gravity * Time.deltaTime;

        var kb = Keyboard.current; var mouse = Mouse.current;
        if (Dead || GameManager.I.Ended || GameManager.I.Transitioning || GameManager.I.InMenu || GameManager.I.Paused || kb == null || mouse == null)
        {
            cam.Aiming = false;
            anim.ReleaseUpper();
            cc.Move(Vector3.up * vy * Time.deltaTime);
            return;
        }

        if (reloading && Time.time >= reloadUntil)
        {
            reloading = false;
            int take = Mathf.Min(magazineSize - magazine, reserveAmmo);
            magazine += take; reserveAmmo -= take;
            UpdateHud();
        }
        if (!reloading && kb.rKey.wasPressedThisFrame) TryReload();

        bool aiming = mouse.rightButton.isPressed && !reloading;
        cam.Aiming = aiming;
        GameManager.I.hud.SetCrosshair(aiming);

        float ix = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        float iz = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        var camYaw = Quaternion.Euler(0f, cam.Yaw, 0f);
        Vector3 move = Vector3.zero;

        if (aiming)
        {
            // face where the camera looks; the legs can still walk slowly in any direction while the arms aim
            transform.rotation = Quaternion.Slerp(transform.rotation, camYaw, 1f - Mathf.Exp(-22f * Time.deltaTime));
            Vector3 dir = camYaw * new Vector3(ix, 0f, iz);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            move = dir * aimWalkSpeed;
            if (dir.sqrMagnitude > 0.01f)
            {
                string legs = Mathf.Abs(ix) > Mathf.Abs(iz) ? (ix > 0f ? "Running_Strafe_Right" : "Running_Strafe_Left")
                            : iz < 0f ? "Walking_Backwards" : "Walking_A";
                if (!anim.Has(legs)) legs = "Walking_A";
                anim.Play(legs, WrapMode.Loop, 0.15f, legs.StartsWith("Running") ? 0.55f : 0.9f);
                stepTimer -= Time.deltaTime;
                if (stepTimer <= 0f) { Sfx.Play(Sfx.I.footsteps, 0.3f); stepTimer = 0.55f; }
            }
            else anim.Play("Idle");
            if (Time.time >= busyUntil) anim.HoldUpper("1H_Ranged_Aiming", "spine");
            if (mouse.leftButton.wasPressedThisFrame && Time.time >= nextFire) Fire();
        }
        else
        {
            if (!reloading) anim.ReleaseUpper();
            Vector3 dir = camYaw * new Vector3(ix, 0f, iz);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            bool run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            float speed = run ? runSpeed : walkSpeed;
            if (reloading) speed = walkSpeed * 0.8f;
            move = dir * speed;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
                if (Time.time >= busyUntil) anim.Play(run && !reloading ? "Running_A" : "Walking_A", WrapMode.Loop, 0.15f, run ? 1.15f : 1.2f);
                stepTimer -= Time.deltaTime;
                if (stepTimer <= 0f) { Sfx.Play(Sfx.I.footsteps, 0.35f); stepTimer = run ? 0.3f : 0.45f; }
            }
            else if (Time.time >= busyUntil) anim.Play("Idle");
        }

        cc.Move((move + Vector3.up * vy) * Time.deltaTime);
    }

    void TryReload()
    {
        if (magazine >= magazineSize || reserveAmmo <= 0) return;
        reloading = true;
        // arms reload while the legs keep walking or standing
        float len = anim.PlayUpper("1H_Ranged_Reload", "spine");
        if (len <= 0f) len = 1.2f;
        reloadUntil = Time.time + len;
        Sfx.Reload();
    }

    void Fire()
    {
        if (magazine <= 0)
        {
            Sfx.Play(Sfx.I.emptyClick, 0.8f);
            nextFire = Time.time + 0.25f;
            if (reserveAmmo > 0) TryReload(); else GameManager.I.hud.Message("Out of ammo", 1.5f);
            return;
        }
        magazine--;
        nextFire = Time.time + fireInterval;
        busyUntil = Time.time + 0.28f;
        anim.PlayUpper("1H_Ranged_Shoot", "spine", 1.6f);   // arms only, so the legs keep walking
        UpdateHud();

        Vector3 from = muzzle != null ? muzzle.position : Chest;
        Sfx.Gunshot(from, true);
        GameManager.AlertNear(transform.position, 22f);

        // shoot where the crosshair points
        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 end = ray.origin + ray.direction * range;
        var hits = Physics.RaycastAll(ray, range, shootMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            // ignore the player and anything behind the player (between the camera and their back)
            if (h.collider.transform.IsChildOf(transform) || Vector3.Dot(h.point - Chest, ray.direction) < -0.6f) continue;
            end = h.point;
            var target = h.collider.GetComponentInParent<IDamageable>();
            if (target != null) for (int d = 0; d < GameManager.GunDamage; d++) target.TakeHit(h.point);   // easy: double damage
            else Sfx.PlayAt(Sfx.I.bulletImpact, h.point, 0.6f);
            break;
        }

        flash.enabled = true; tracer.enabled = true;
        tracer.SetPosition(0, from); tracer.SetPosition(1, end);
        flashUntil = Time.time + 0.05f;
    }

    // hits: 1 for a small enemy, 2 for a big one, 3 for the gunman
    public void TakeDamage(int hits)
    {
        if (Dead || Time.time < nextHurt || GameManager.I.Ended || GameManager.I.Transitioning) return;
        nextHurt = Time.time + hurtCooldown;
        HitsTaken = Mathf.Min(hitsToDie, HitsTaken + hits);
        GameManager.I.hud.SetHealth(HitsTaken, hitsToDie);
        GameManager.I.hud.DamageFlash();
        Sfx.Play(Sfx.I.playerHurt, 0.9f);
        if (HitsTaken >= hitsToDie) { Die(); return; }
        anim.Play("Hit_A", WrapMode.Once, 0.05f, 1.3f, true); busyUntil = Time.time + 0.35f;
    }

    // Moves the player instantly (the CharacterController must be off while the transform is set).
    public void Teleport(Vector3 position, float yaw)
    {
        var c = GetComponent<CharacterController>();
        c.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        c.enabled = true;
        vy = 0f;
    }

    public void SetMaxHealth(int hits)
    {
        hitsToDie = hits;
        if (GameManager.I != null) GameManager.I.hud.SetHealth(HitsTaken, hitsToDie);
    }

    public void Heal(int hits)
    {
        HitsTaken = Mathf.Max(0, HitsTaken - hits);
        GameManager.I.hud.SetHealth(HitsTaken, hitsToDie);
    }

    public void AddAmmo(int rounds)
    {
        reserveAmmo += rounds;
        UpdateHud();
    }

    void Die()
    {
        Dead = true;
        cam.Aiming = false;
        anim.ReleaseUpper();
        anim.Stop("1H_Ranged_Shoot");
        GameManager.I.hud.SetCrosshair(false);
        anim.Stop("1H_Ranged_Reload");
        anim.Play("Death_A", WrapMode.ClampForever, 0.1f, 1f, true);
        GameManager.I.PlayerDied();
    }
}
