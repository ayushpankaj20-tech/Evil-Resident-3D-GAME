using UnityEngine;

// Ammo, health and key pickups. Collected by walking into them.
public class Pickup : MonoBehaviour
{
    public enum Kind { Ammo, Health, Key, FinalKey }
    public Kind kind;
    public int amount = 18;
    public float radius = 1.7f;
    public float spin = 90f;

    Vector3 basePos;
    bool guarded;

    void OnEnable() { basePos = transform.position; Guard(); }
    void Start() { Guard(); }

    // every key gets an exploder standing a few metres from it, the moment the key appears
    void Guard()
    {
        if (guarded || (kind != Kind.Key && kind != Kind.FinalKey) || transform.position.y < -10f) return;
        var gm = GameManager.I;
        if (gm == null || gm.exploderTemplate == null) return;
        guarded = true;
        gm.SpawnExploderNear(transform.position);
    }

    void Update()
    {
        transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * 2.4f) * 0.12f);
        var gm = GameManager.I;
        if (gm == null || gm.player == null || gm.player.Dead) return;
        Vector3 d = gm.player.transform.position - basePos; d.y = 0;
        if (d.sqrMagnitude > radius * radius) return;

        switch (kind)
        {
            case Kind.Ammo:
                gm.player.AddAmmo(amount);
                gm.hud.Message("+" + amount + " ammo", 1.5f);
                Sfx.Play(Sfx.I.pickup, 0.8f);
                break;
            case Kind.Health:
                if (gm.player.HitsTaken == 0) return; // leave it for later
                gm.player.Heal(Mathf.CeilToInt(amount * gm.player.hitsToDie / 7f));   // easy mode heals twice as much
                gm.hud.Message("Health restored", 1.5f);
                Sfx.Play(Sfx.I.pickup, 0.8f);
                break;
            case Kind.Key:
                gm.AddKey();
                Sfx.Play(Sfx.I.key, 0.9f);
                break;
            case Kind.FinalKey:
                gm.FinalKeyTaken();
                Sfx.Play(Sfx.I.key, 1f);
                break;
        }
        Destroy(gameObject);
    }
}
