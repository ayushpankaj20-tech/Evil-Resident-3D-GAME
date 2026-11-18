using UnityEngine;

// Thin wrapper around the legacy Animation component on the KayKit characters.
public class CharAnim
{
    readonly Animation anim;
    string current, upperHeld;

    public CharAnim(GameObject go)
    {
        anim = go.GetComponentInChildren<Animation>();
        if (anim != null) { anim.playAutomatically = false; anim.Stop(); }
    }

    public bool Has(string clip) { return anim != null && anim[clip] != null; }

    public float Length(string clip) { return Has(clip) ? anim[clip].length : 0f; }

    // Plays a clip on the upper body only (from the given bone up), on top of whatever the legs are doing.
    public float PlayUpper(string clip, string fromBone, float speed = 1f)
    {
        if (!Has(clip)) return 0f;
        var st = anim[clip];
        MakeUpper(st, fromBone);
        st.wrapMode = WrapMode.Once;
        st.speed = speed;
        st.time = 0f;
        anim.CrossFade(clip, 0.1f, PlayMode.StopSameLayer);
        upperHeld = null;
        return st.length / Mathf.Max(0.01f, speed);
    }

    void MakeUpper(AnimationState st, string fromBone)
    {
        if (st.layer == 1) return;
        st.layer = 1;
        foreach (var t in anim.GetComponentsInChildren<Transform>(true))
            if (t.name == fromBone) { st.AddMixingTransform(t, true); break; }
    }

    // Holds a looping pose on the upper body only (aiming) until ReleaseUpper. Safe to call every frame.
    public void HoldUpper(string clip, string fromBone)
    {
        if (!Has(clip) || upperHeld == clip) return;
        var st = anim[clip];
        MakeUpper(st, fromBone);
        st.wrapMode = WrapMode.Loop;
        st.speed = 1f;
        anim.CrossFade(clip, 0.12f, PlayMode.StopSameLayer);
        upperHeld = clip;
    }

    public void ReleaseUpper()
    {
        if (upperHeld == null) return;
        anim.Blend(upperHeld, 0f, 0.15f);
        upperHeld = null;
    }

    public void Stop(string clip) { if (Has(clip)) anim.Stop(clip); }

    // Cross-fades to a clip. Returns its duration at the given speed.
    public float Play(string clip, WrapMode wrap = WrapMode.Loop, float fade = 0.15f, float speed = 1f, bool restart = false)
    {
        if (!Has(clip)) return 0f;
        var st = anim[clip];
        st.wrapMode = wrap;
        st.speed = speed;
        if (restart) st.time = 0f;
        if (current != clip || restart || !anim.IsPlaying(clip)) anim.CrossFade(clip, fade);
        current = clip;
        return st.length / Mathf.Max(0.01f, speed);
    }
}

public interface IDamageable
{
    void TakeHit(Vector3 point);
}
