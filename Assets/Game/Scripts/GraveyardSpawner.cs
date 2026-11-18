using System.Collections;
using UnityEngine;

// When the player first comes near, raises a random number of small enemies from the graves, one at a time.
// It runs once: enemies that are killed do not come back.
public class GraveyardSpawner : MonoBehaviour
{
    public GameObject template;          // hidden small enemy that gets copied
    public Vector3[] points;             // grave positions
    public float triggerRadius = 19f;
    public int minCount = 7, maxCount = 9;
    public float delay = 5f;             // seconds between each enemy
    public bool fromHouse;               // enemies run out of a door instead of rising from graves
    public string message = "Something is stirring in the graveyard...";

    public float chaseSpeed;             // > 0: the enemies it raises run this fast (others run 3.3, the player 6.2)
    public bool manual;                  // only starts when Begin() is called (no proximity trigger)

    bool started, finished;
    readonly System.Collections.Generic.List<EnemyAI> raised = new System.Collections.Generic.List<EnemyAI>();

    // true once every enemy has been raised and all of them are dead
    public bool Cleared
    {
        get
        {
            if (!finished) return false;
            foreach (var e in raised) if (e != null && !e.IsDead) return false;
            return true;
        }
    }

    public void Begin()
    {
        if (started || !isActiveAndEnabled) return;
        started = true;
        StartCoroutine(Run());
    }

    void Update()
    {
        if (started || manual || GameManager.I == null || GameManager.I.player == null || GameManager.I.InMenu) return;
        Vector3 d = GameManager.I.player.transform.position - transform.position; d.y = 0;
        if (d.magnitude > triggerRadius) return;
        started = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        int count = Random.Range(minCount, maxCount + 1);
        if (!string.IsNullOrEmpty(message)) GameManager.I.hud.Message(message, 4f);
        if (fromHouse) Sfx.PlayAt(Sfx.I.gate, transform.position, 1f);
        for (int i = 0; i < count; i++)
        {
            yield return new WaitForSeconds(delay);
            if (GameManager.I.Ended) yield break;
            Vector3 p = points[i % points.Length] + new Vector3(Random.Range(-0.6f, 0.6f), 0, Random.Range(-0.6f, 0.6f));
            var go = Instantiate(template, p, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            go.name = "Enemy_Graveyard_" + (i + 1);
            var ai = go.GetComponent<EnemyAI>();
            if (fromHouse) ai.chaseOnStart = true; else ai.riseFromGround = true;
            if (chaseSpeed > 0f) ai.chaseSpeed = chaseSpeed;
            raised.Add(ai);
            go.SetActive(true);
        }
        finished = true;
    }
}
