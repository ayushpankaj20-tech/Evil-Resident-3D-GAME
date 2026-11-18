using System.Collections;
using UnityEngine;

// A two-leaf gate. Opens when the player comes close, if they hold enough keys.
public class Gate : MonoBehaviour
{
    public Transform leftLeaf, rightLeaf;
    public int requiredKeys;           // 0 = opens for anyone, 2 = the exit gate
    public bool isExit;
    public float openRadius = 7f;
    public float openAngle = 100f;
    public float openTime = 1.2f;

    bool open, haveRest;
    float nextHint;
    Quaternion leftRest, rightRest;

    // Opens at once, without the swing or the key check (used when the game restarts at the arena checkpoint).
    public void OpenNow()
    {
        if (open) return;
        open = true;
        foreach (var c in GetComponentsInChildren<Collider>())
            if ((leftLeaf != null && c.transform.IsChildOf(leftLeaf)) || (rightLeaf != null && c.transform.IsChildOf(rightLeaf))) c.enabled = false;
        if (leftLeaf != null) { leftRest = leftLeaf.localRotation; leftLeaf.localRotation = leftRest * Quaternion.Euler(0, openAngle, 0); }
        if (rightLeaf != null) { rightRest = rightLeaf.localRotation; rightLeaf.localRotation = rightRest * Quaternion.Euler(0, -openAngle, 0); }
        haveRest = true;
    }

    // Shuts the gate and keeps it shut (used behind the player when a level is finished).
    public void CloseAndSeal(bool sound)
    {
        StopAllCoroutines();
        open = true; // Update() never tries to open it again
        if (haveRest)
        {
            if (leftLeaf != null) leftLeaf.localRotation = leftRest;
            if (rightLeaf != null) rightLeaf.localRotation = rightRest;
        }
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
        if (sound && Sfx.I != null) Sfx.PlayAt(Sfx.I.gate, transform.position, 1f);
    }

    void Update()
    {
        if (open || GameManager.I == null || GameManager.I.player == null) return;
        Vector3 d = GameManager.I.player.transform.position - transform.position; d.y = 0;
        bool near = d.sqrMagnitude < openRadius * openRadius;
        if (!near && requiredKeys == 0)
        {
            // also let chasing enemies through
            foreach (var e in GameManager.Enemies)
                if (e != null && (e.transform.position - transform.position).sqrMagnitude < 9f) { near = true; break; }
        }
        if (!near) return;
        if (GameManager.I.Keys >= requiredKeys) StartCoroutine(Open());
        else if (Time.time >= nextHint)
        {
            GameManager.I.hud.Message("Locked. You need " + requiredKeys + " keys (" + GameManager.I.Keys + "/" + requiredKeys + ")", 2.5f);
            nextHint = Time.time + 3f;
        }
    }

    IEnumerator Open()
    {
        open = true;
        Sfx.PlayAt(Sfx.I.gate, transform.position, 1f);
        foreach (var c in GetComponentsInChildren<Collider>())
            if ((leftLeaf != null && c.transform.IsChildOf(leftLeaf)) || (rightLeaf != null && c.transform.IsChildOf(rightLeaf))) c.enabled = false;
        Quaternion l0 = leftLeaf != null ? leftLeaf.localRotation : Quaternion.identity;
        Quaternion r0 = rightLeaf != null ? rightLeaf.localRotation : Quaternion.identity;
        leftRest = l0; rightRest = r0; haveRest = true;
        for (float t = 0; t < 1f; t += Time.deltaTime / openTime)
        {
            float a = Mathf.SmoothStep(0, openAngle, t);
            if (leftLeaf != null) leftLeaf.localRotation = l0 * Quaternion.Euler(0, a, 0);
            if (rightLeaf != null) rightLeaf.localRotation = r0 * Quaternion.Euler(0, -a, 0);
            yield return null;
        }
        if (isExit)
        {
            GameManager.I.ExitOpen = true;
            GameManager.I.hud.Message("The exit is open. Go!", 4f);
        }
    }
}
