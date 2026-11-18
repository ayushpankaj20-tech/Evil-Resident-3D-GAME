using UnityEngine;
using UnityEngine.UI;

// Builds the on-screen display in code: health circle (bottom right), ammo, keys, crosshair, messages, end screen.
public class Hud : MonoBehaviour
{
    // circle colour after 0, 1, 2 ... 6 hits taken; the last one (red) means the next hit kills
    public Color[] healthColors =
    {
        new Color(0.20f, 0.85f, 0.35f), new Color(0.55f, 0.88f, 0.25f), new Color(0.95f, 0.90f, 0.20f),
        new Color(1.00f, 0.72f, 0.15f), new Color(1.00f, 0.52f, 0.12f), new Color(0.98f, 0.33f, 0.12f),
        new Color(0.90f, 0.08f, 0.08f)
    };

    Image healthCircle, crosshair, flash, endPanel, fade, bossBack, bossFill;
    Text bossName, bossSpeech;
    float speechUntil;
    Text healthText, ammoText, keyText, messageText, endTitle, endSub, areaText;
    public float bannerExtraSeconds = 10f;  // added to every banner of 4 s or more (short notes like "+12 ammo" stay short)

    public float areaFadeIn = 0.8f, areaHold = 3.5f, areaFadeOut = 1.2f;   // timing of the red banner in the middle of the screen

    GameObject gameplayRoot;
    public void ShowGameplay(bool on) { if (gameplayRoot != null) gameplayRoot.SetActive(on); }

    float messageUntil, flashAlpha, pulse, areaStart = -100f;
    Font font;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("HUD Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // everything of the game's display sits under one object, so the main menu can hide it
        var gameplay = new GameObject("Gameplay", typeof(RectTransform));
        gameplay.transform.SetParent(canvasGo.transform, false);
        Stretch((RectTransform)gameplay.transform);
        gameplayRoot = gameplay;
        var root = gameplay.transform;
        var circle = MakeCircle(128);

        flash = MakeImage(root, "Damage Flash", null, new Color(0.8f, 0f, 0f, 0f));
        Stretch(flash.rectTransform);

        // health: dark ring, coloured circle, hits-left number
        var ring = MakeImage(root, "Health Ring", circle, new Color(0f, 0f, 0f, 0.55f));
        Anchor(ring.rectTransform, new Vector2(1, 0), new Vector2(-130, 130), new Vector2(190, 190));
        healthCircle = MakeImage(root, "Health Circle", circle, healthColors[0]);
        Anchor(healthCircle.rectTransform, new Vector2(1, 0), new Vector2(-130, 130), new Vector2(160, 160));
        healthText = MakeText(root, "Health Text", 64, TextAnchor.MiddleCenter, new Color(0.05f, 0.05f, 0.05f));
        Anchor(healthText.rectTransform, new Vector2(1, 0), new Vector2(-130, 130), new Vector2(160, 160));

        ammoText = MakeText(root, "Ammo", 54, TextAnchor.LowerLeft, Color.white);
        Anchor(ammoText.rectTransform, new Vector2(0, 0), new Vector2(330, 80), new Vector2(560, 90));
        keyText = MakeText(root, "Keys", 44, TextAnchor.UpperRight, new Color(1f, 0.85f, 0.35f));
        Anchor(keyText.rectTransform, new Vector2(1, 1), new Vector2(-250, -60), new Vector2(420, 70));

        crosshair = MakeImage(root, "Crosshair", circle, new Color(1f, 1f, 1f, 0.9f));
        Anchor(crosshair.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12));
        crosshair.enabled = false;

        // banner: top centre, medium size
        messageText = MakeText(root, "Message", 38, TextAnchor.MiddleCenter, Color.white);
        Anchor(messageText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(1400, 70));

        // red banner in the middle of the screen: fades in and out when a new area is entered
        areaText = MakeText(root, "Area Banner", 58, TextAnchor.MiddleCenter, new Color(0.86f, 0.08f, 0.08f, 0f));
        Anchor(areaText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1800, 90));
        areaText.enabled = false;

        // full-screen black used to fade between levels (drawn under the end screen)
        fade = MakeImage(root, "Fade", null, new Color(0f, 0f, 0f, 0f));
        Stretch(fade.rectTransform);

        // boss name and health bar, top centre
        bossBack = MakeImage(root, "Boss Bar", null, new Color(0f, 0f, 0f, 0.65f));
        Anchor(bossBack.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -42), new Vector2(900, 28));
        bossFill = MakeImage(bossBack.transform, "Boss Fill", null, new Color(0.85f, 0.1f, 0.08f));
        var bf = bossFill.rectTransform;
        bf.anchorMin = Vector2.zero; bf.anchorMax = Vector2.one; bf.pivot = new Vector2(0f, 0.5f);
        bf.offsetMin = new Vector2(3, 3); bf.offsetMax = new Vector2(-3, -3);
        bossName = MakeText(root, "Boss Name", 34, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.8f));
        Anchor(bossName.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(900, 44));
        bossBack.gameObject.SetActive(false); bossName.enabled = false;
        // what the boss says to the player, under his health bar
        bossSpeech = MakeText(root, "Boss Speech", 36, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f));
        Anchor(bossSpeech.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -190), new Vector2(1700, 60));
        bossSpeech.fontStyle = FontStyle.BoldAndItalic;
        bossSpeech.enabled = false;

        endPanel = MakeImage(root, "End Panel", null, new Color(0f, 0f, 0f, 0.72f));
        Stretch(endPanel.rectTransform);
        endTitle = MakeText(endPanel.transform, "Title", 150, TextAnchor.MiddleCenter, Color.white);
        Anchor(endTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1700, 220));
        endSub = MakeText(endPanel.transform, "Sub", 48, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f));
        Anchor(endSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1400, 80));
        endPanel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (messageText.enabled && Time.unscaledTime > messageUntil) messageText.enabled = false;
        if (bossSpeech.enabled && Time.time > speechUntil) bossSpeech.enabled = false;
        if (areaText.enabled)
        {
            float t = Time.unscaledTime - areaStart, a;
            if (t < areaFadeIn) a = t / areaFadeIn;
            else if (t < areaFadeIn + areaHold) a = 1f;
            else a = 1f - (t - areaFadeIn - areaHold) / areaFadeOut;
            var c = areaText.color; c.a = Mathf.Clamp01(a); areaText.color = c;
            if (a <= 0f) areaText.enabled = false;
        }
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - Time.unscaledDeltaTime * 1.4f);
            flash.color = new Color(0.8f, 0f, 0f, flashAlpha);
        }
        if (pulse > 0f)
        {
            pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime * 3f);
            healthCircle.rectTransform.localScale = Vector3.one * (1f + 0.25f * pulse);
        }
    }

    public void SetHealth(int hitsTaken, int hitsToDie)
    {
        int left = Mathf.Max(0, hitsToDie - hitsTaken);
        healthCircle.color = left == 0 ? new Color(0.25f, 0.02f, 0.02f) : healthColors[Mathf.Clamp(Mathf.FloorToInt(hitsTaken * (float)healthColors.Length / Mathf.Max(1, hitsToDie)), 0, healthColors.Length - 1)];
        healthText.text = left.ToString();
        pulse = 1f;
    }

    public void SetAmmo(int magazine, int reserve)
    {
        ammoText.text = magazine + " / " + reserve;
        ammoText.color = magazine == 0 ? new Color(1f, 0.35f, 0.3f) : Color.white;
    }

    public void SetKeys(int have, int need) { keyText.text = "Keys " + have + " / " + need; }
    public void SetCrosshair(bool on) { crosshair.enabled = on; }
    public void DamageFlash() { flashAlpha = 0.45f; }
    public void AreaBanner(string text)
    {
        areaText.text = text;
        areaText.fontSize = text.Length > 46 ? 44 : 58;   // long lines smaller so they fit the screen
        var c = areaText.color; c.a = 0f; areaText.color = c;
        areaText.enabled = true;
        areaStart = Time.unscaledTime;
    }

    public void ShowBoss(string name) { bossName.text = name; bossName.enabled = true; bossBack.gameObject.SetActive(true); SetBoss(1f); }
    public void SetBoss(float health01) { bossFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(health01), 1f, 1f); }
    public void HideBoss() { bossName.enabled = false; bossBack.gameObject.SetActive(false); }
    public void BossSay(string who, string line, float seconds = 4.5f)
    {
        bossSpeech.text = who + ":  \u201C" + line + "\u201D";
        bossSpeech.enabled = true;
        speechUntil = Time.time + seconds;
    }

    public void SetFade(float alpha) { fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha)); }
    public void SetTopRight(string text) { keyText.text = text; }

    public void Message(string text, float seconds)
    {
        messageText.text = text;
        messageText.enabled = true;
        messageUntil = Time.unscaledTime + seconds + (seconds >= 4f ? bannerExtraSeconds : 0f);
    }

    public void ShowEnd(string title, string sub, Color color)
    {
        endPanel.gameObject.SetActive(true);
        endTitle.text = title; endTitle.color = color;
        endSub.text = sub;
        crosshair.enabled = false;
        areaText.enabled = false;
    }

    // ---- builders ----

    Image MakeImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = anchor; t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.8f); shadow.effectDistance = new Vector2(2, -2);
        return t;
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Sprite MakeCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size / 2f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                byte a = (byte)(Mathf.Clamp01(r - 1f - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
