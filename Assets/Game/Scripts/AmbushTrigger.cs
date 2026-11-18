using UnityEngine;
using UnityEngine.AI;

// The crawler jump scare: when the player walks into this spot, a crawler bursts out of the ground right beside
// (slightly behind) them with the creature's scream. Silent ones are sprung by the game instead (stage 3).
public class AmbushTrigger : MonoBehaviour
{
    public GameObject template;       // the hidden crawler
    public float radius = 4.5f;
    public bool manual;               // only springs when Spring() is called
    public bool silent;               // no scream (stage 3: it rises with the graveyard dead)
    bool done;

    void Update()
    {
        var gm = GameManager.I;
        if (done || manual || gm == null || gm.player == null || gm.InMenu || gm.player.Dead) return;
        Vector3 d = gm.player.transform.position - transform.position; d.y = 0;
        if (d.magnitude < radius) Spring();
    }

    public void Spring()
    {
        if (done || template == null) return;
        var gm = GameManager.I;
        if (gm == null || gm.player == null || gm.Ended) return;
        done = true;
        var p = gm.player.transform;
        // beside and a little behind the player, wherever there is ground to stand on
        Vector3[] tries = { -p.forward * 2.6f + p.right * 1.4f, -p.forward * 2.6f - p.right * 1.4f, p.right * 2.6f, -p.right * 2.6f, -p.forward * 3.2f, p.forward * 3f };
        Vector3 pos = p.position + tries[0];
        foreach (var t in tries)
            if (NavMesh.SamplePosition(p.position + t, out NavMeshHit h, 1.2f, NavMesh.AllAreas)) { pos = h.position; break; }
        Vector3 look = p.position - pos; look.y = 0;
        var go = Instantiate(template, pos, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        go.name = "Enemy_Crawler";
        go.GetComponent<EnemyAI>().riseFromGround = true;
        go.SetActive(true);
        if (!silent)
        {
            Sfx.ZombieCall();                                  // the creature's scream
            Sfx.ZombiePain(pos + Vector3.up);
            gm.hud.DamageFlash();
        }
    }
}
