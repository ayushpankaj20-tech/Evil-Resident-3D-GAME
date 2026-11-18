using UnityEngine;

// Tints a character a colour for a moment when it is hit.
public class HitFlash : MonoBehaviour
{
    public Color tint = new Color(1f, 0.18f, 0.12f);
    public Color glow = new Color(0.9f, 0.08f, 0.04f);
    public float duration = 0.14f;

    static readonly int BaseColor = Shader.PropertyToID("baseColorFactor");   // KayKit (glTF) materials
    static readonly int BaseColorUrp = Shader.PropertyToID("_BaseColor");     // standard URP materials
    static readonly int Emissive = Shader.PropertyToID("emissiveFactor");

    Renderer[] renderers;
    MaterialPropertyBlock block;
    float until;
    bool on;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    public void Flash()
    {
        until = Time.time + duration;
        if (on) return;
        on = true;
        block.SetColor(BaseColor, tint);
        block.SetColor(BaseColorUrp, tint);
        block.SetColor(Emissive, glow);
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(block);
    }

    void Update()
    {
        if (!on || Time.time < until) return;
        on = false;
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null);
    }
}
