using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// EVIL RESIDENT: main menu (START / CONTINUE / CONTROLS / QUIT) with the camera flying slow clips over
// stage 1, 2 and 3 behind it, and the pause menu (Esc) that shows the current objective.
// Music for the main menu: put an audio file in Assets/Game/Resources/Audio/Music (any name); the first one found plays.
public class GameMenu : MonoBehaviour
{
    [System.Serializable]
    public struct Shot
    {
        public string caption;
        public Vector3 from, to;            // the camera glides from here to here
        public Vector3 lookFrom, lookTo;    // while looking from this point to this one
        public float seconds;
    }

    [System.Serializable]
    public struct IntroLine { public float at, length; public string text; }

    public string gameTitle = "EVIL RESIDENT";
    [Header("Story intro (new game)")]
    public Shot[] introShots;               // the last one ends behind the player
    public IntroLine[] introLines;
    public float introMusicStart = 72f, introMusicEnd = 90f;   // 1:12 to 1:30 of the music
    public string creditLine = "A GAME BY AYUSH PANKAJ   -   SOLO GAME DEV";
    public string githubUrl = "https://github.com/ayushpankaj20-tech";
    public string tagline = "A creature trapped in a village has woken up.";
    public Shot[] shots;
    public float shotFade = 0.9f;
    public string musicFolder = "Audio/Music";
    public float musicVolume = 0.6f;

    static readonly Color Red = new Color(0.78f, 0.05f, 0.04f);
    static readonly Color Gold = new Color(1f, 0.8f, 0.35f);

    Font font;
    GameObject canvasGo, mainPanel, controlsPanel, pausePanel, difficultyPanel, introPanel, creditsPanel;
    Text themeLabel, notice;
    float noticeUntil;

    void Notice(string text)
    {
        notice.text = text;
        notice.enabled = true;
        noticeUntil = Time.unscaledTime + 3.5f;
    }
    Image fade, shade;
    Text caption, pauseStage, pauseObjective, introText, creditTitle, creditSub, skipHint;
    bool skipIntro;
    Button continueButton;
    AudioSource music;
    Camera cam;
    bool menuRunning, starting, controlsFromPause;
    int shot;
    float shotStart;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUi();
    }

    // ---------------- main menu ----------------

    public void OpenMain()
    {
        cam = Camera.main;
        canvasGo.SetActive(true);
        mainPanel.SetActive(true);
        shade.gameObject.SetActive(true);
        caption.gameObject.SetActive(false);   // no stage names over the menu clips
        bool canContinue = GameManager.Checkpoint >= 2;
        continueButton.gameObject.SetActive(canContinue);
        if (canContinue) continueButton.GetComponentInChildren<Text>().text = "CONTINUE  (" + (GameManager.Hard ? "HARD" : "EASY") + ")";
        Select(canContinue ? continueButton.gameObject : mainPanel.transform.Find("START GAME").gameObject);

        var clips = Resources.LoadAll<AudioClip>(musicFolder);
        if (clips != null && clips.Length > 0)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.clip = clips[0]; music.loop = true; music.volume = musicVolume; music.spatialBlend = 0f;
            music.ignoreListenerPause = true;
            music.Play();
        }
        menuRunning = true;
        shot = 0; shotStart = Time.unscaledTime;
    }

    void Update()
    {
        if (notice != null && notice.enabled && Time.unscaledTime > noticeUntil) notice.enabled = false;
        if (!menuRunning || cam == null || shots == null || shots.Length == 0) return;
        var s = shots[shot];
        float t = (Time.unscaledTime - shotStart) / Mathf.Max(1f, s.seconds);
        if (t >= 1f)
        {
            shot = (shot + 1) % shots.Length;
            shotStart = Time.unscaledTime;
            s = shots[shot]; t = 0f;
        }
        float k = Mathf.SmoothStep(0f, 1f, t);
        Vector3 pos = Vector3.Lerp(s.from, s.to, k);
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(Vector3.Lerp(s.lookFrom, s.lookTo, k) - pos);
        cam.fieldOfView = 55f;

        // each clip fades in from black and out again
        float age = Time.unscaledTime - shotStart, left = s.seconds - age;
        float a = Mathf.Max(1f - age / shotFade, 1f - left / shotFade);
        if (!starting) fade.color = new Color(0, 0, 0, Mathf.Clamp01(a));
        caption.text = s.caption;
        caption.color = new Color(0.85f, 0.85f, 0.85f, Mathf.Clamp01(1f - a) * 0.9f);
    }

    void StartGame(bool newGame)
    {
        if (starting) return;
        starting = true;
        StartCoroutine(StartRoutine(newGame));
    }

    IEnumerator StartRoutine(bool newGame)
    {
        EventSystem.current.SetSelectedGameObject(null);
        float from = fade.color.a, v0 = music != null ? music.volume : 0f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.9f)
        {
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, 1f, t));
            if (music != null) music.volume = Mathf.Lerp(v0, 0f, t);
            yield return null;
        }
        fade.color = Color.black;
        menuRunning = false;
        if (music != null) music.Stop();
        mainPanel.SetActive(false); shade.gameObject.SetActive(false); caption.gameObject.SetActive(false);
        GameManager.I.StartFromMenu(newGame);
        yield return null;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.9f)
        {
            fade.color = new Color(0, 0, 0, 1f - t);
            yield return null;
        }
        fade.color = Color.clear;
        starting = false;
    }

    // ---------------- theme and credits ----------------

    static string ThemeText() { return "THEME:  " + (Theme.Morning ? "DAY" : "NIGHT") + (Theme.DayUnlocked ? "" : "   (DAY LOCKED)"); }

    void ToggleTheme()
    {
        if (!Theme.DayUnlocked)
        {
            Notice("BEAT THE GAME AT NIGHT TO UNLOCK THE DAY THEME");
            return;
        }
        Theme.Morning = !Theme.Morning;
        Theme.Apply(Theme.Morning);
        themeLabel.text = ThemeText();
    }

    void ShowCredits(bool on)
    {
        creditsPanel.SetActive(on);
        mainPanel.SetActive(!on);
        shade.gameObject.SetActive(!on);
        Select(on ? creditsPanel.transform.Find("BACK").gameObject : mainPanel.transform.Find("CREDITS").gameObject);
    }

    // ---------------- difficulty ----------------

    void ShowDifficulty(bool on)
    {
        difficultyPanel.SetActive(on);
        mainPanel.SetActive(!on);
        Select(on ? difficultyPanel.transform.Find(GameManager.Hard ? "HARD START" : "EASY START").gameObject
                  : mainPanel.transform.Find("START GAME").gameObject);
    }

    void ChooseDifficulty(bool hard)
    {
        if (starting) return;
        GameManager.SetDifficulty(hard);
        starting = true;
        StartCoroutine(IntroRoutine());
    }

    // ---------------- story intro ----------------
    // 18 seconds of the music (1:12 - 1:30): slow clips over every stage and its enemies with the story told
    // line by line, the credit, and the last clip ends behind the player, where the game begins. SPACE skips.

    IEnumerator IntroRoutine()
    {
        EventSystem.current.SetSelectedGameObject(null);
        float from = fade.color.a, v0 = music != null ? music.volume : 0f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
        {
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, 1f, t));
            if (music != null) music.volume = Mathf.Lerp(v0, 0f, t);
            yield return null;
        }
        fade.color = Color.black;
        menuRunning = false;
        mainPanel.SetActive(false); difficultyPanel.SetActive(false); shade.gameObject.SetActive(false); caption.gameObject.SetActive(false);
        GameManager.I.ShowLevel2ForIntro(true);

        float dur = introMusicEnd - introMusicStart;
        if (music != null && music.clip != null)
        {
            music.Stop();
            music.time = Mathf.Min(introMusicStart, Mathf.Max(0f, music.clip.length - dur));
            music.volume = 0f;
            music.Play();
        }
        introPanel.SetActive(true);
        skipIntro = false;
        var player = GameManager.I.player;
        var tpc = player.cam;
        float yaw = player.transform.eulerAngles.y;
        Quaternion endRot = Quaternion.Euler(8f, yaw, 0f);
        Vector3 endPos = player.transform.position + Vector3.up * tpc.pivotHeight + endRot * new Vector3(tpc.shoulderOffset, 0f, -tpc.distance);

        float total = 0f; foreach (var sh in introShots) total += sh.seconds;
        float scale = dur / Mathf.Max(1f, total);   // the clips fill the music exactly
        float t0 = Time.unscaledTime;
        while (true)
        {
            float t = Time.unscaledTime - t0;
            if (t >= dur || skipIntro) break;
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) { skipIntro = true; break; }
            if (music != null) music.volume = musicVolume * Mathf.Clamp01(Mathf.Min(t / 1.2f, (dur - t) / 1.5f + 0.35f));

            // which clip, and how far through it
            float acc = 0f; int i = 0;
            for (; i < introShots.Length - 1; i++) { float len = introShots[i].seconds * scale; if (t < acc + len) break; acc += len; }
            var s = introShots[i];
            float slen = s.seconds * scale, local = Mathf.Clamp01((t - acc) / slen), k = Mathf.SmoothStep(0f, 1f, local);
            bool last = i == introShots.Length - 1;
            Vector3 pos; Quaternion rot;
            if (!last)
            {
                pos = Vector3.Lerp(s.from, s.to, k);
                rot = Quaternion.LookRotation(Vector3.Lerp(s.lookFrom, s.lookTo, k) - pos);
            }
            else
            {
                // the last clip swoops down from above the camp to the player's back
                pos = Vector3.Lerp(s.from, endPos, k);
                rot = Quaternion.Slerp(Quaternion.LookRotation(s.lookFrom - s.from), endRot, k);
            }
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = Mathf.Lerp(55f, tpc.normalFov, last ? k : 0f);

            // black between clips (not at the very end: the game starts straight from the last frame)
            float edgeIn = (t - acc) / 0.5f, edgeOut = last ? 9f : (acc + slen - t) / 0.4f;
            fade.color = new Color(0, 0, 0, Mathf.Clamp01(1f - Mathf.Min(edgeIn, edgeOut)));

            // the story, one line at a time
            string line = ""; float la = 0f;
            foreach (var l in introLines)
                if (t >= l.at && t < l.at + l.length) { line = l.text; la = Mathf.Clamp01(Mathf.Min((t - l.at) / 0.6f, (l.at + l.length - t) / 0.6f)); }
            introText.text = line;
            introText.color = new Color(1f, 1f, 1f, la);
            // the credit over the last 4 seconds
            float ca = Mathf.Clamp01(Mathf.Min((t - (dur - 3.6f)) / 0.7f, (dur - t) / 0.6f));
            creditTitle.color = new Color(Red.r, Red.g, Red.b, ca);
            creditSub.color = new Color(1f, 1f, 1f, ca);
            yield return null;
        }

        if (skipIntro)
        {
            fade.color = Color.black;
            cam.transform.SetPositionAndRotation(endPos, endRot);
        }
        introPanel.SetActive(false);
        GameManager.I.StartFromMenu(true);
        float mv = music != null ? music.volume : 0f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.2f)
        {
            if (skipIntro) fade.color = new Color(0, 0, 0, 1f - t);
            if (music != null) music.volume = Mathf.Lerp(mv, 0f, t);
            yield return null;
        }
        fade.color = Color.clear;
        if (music != null) music.Stop();
        starting = false;
    }

    // ---------------- pause menu ----------------

    public void OpenPause(string stage, string objective)
    {
        canvasGo.SetActive(true);
        fade.color = Color.clear;
        pauseStage.text = stage;
        pauseObjective.text = objective;
        pausePanel.SetActive(true);
        controlsPanel.SetActive(false);
        Select(pausePanel.transform.Find("RESUME").gameObject);
    }

    public void ClosePause()
    {
        pausePanel.SetActive(false);
        controlsPanel.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // Esc: closes the controls page if it is open, otherwise resumes the game
    public void Back()
    {
        if (difficultyPanel.activeSelf) { ShowDifficulty(false); return; }
        if (creditsPanel.activeSelf) { ShowCredits(false); return; }
        if (controlsPanel.activeSelf) { ShowControls(false); return; }
        if (GameManager.I.Paused) GameManager.I.SetPaused(false);
    }

    void ShowControls(bool on)
    {
        controlsFromPause = on ? pausePanel.activeSelf : controlsFromPause;
        controlsPanel.SetActive(on);
        if (controlsFromPause) pausePanel.SetActive(!on); else mainPanel.SetActive(!on);
        if (on) Select(controlsPanel.transform.Find("BACK").gameObject);
        else Select(controlsFromPause ? pausePanel.transform.Find("RESUME").gameObject
                                      : (continueButton.gameObject.activeSelf ? continueButton.gameObject : mainPanel.transform.Find("START GAME").gameObject));
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Select(GameObject go) { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go); }

    // ---------------- building the screens ----------------

    void BuildUi()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        canvasGo = new GameObject("Menu Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // over the game's display
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var root = canvasGo.transform;

        // dark fade on the left so the title and buttons read well over the moving world
        shade = Img(root, "Shade", GradientSprite(), new Color(0, 0, 0, 0.88f));
        var sr = shade.rectTransform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 0.5f);
        sr.sizeDelta = new Vector2(1150, 0); sr.anchoredPosition = Vector2.zero;
        shade.gameObject.SetActive(false);

        caption = Txt(root, "Caption", 30, TextAnchor.LowerRight, new Color(0.85f, 0.85f, 0.85f, 0f));
        Place(caption.rectTransform, new Vector2(1, 0), new Vector2(-60, 60), new Vector2(900, 50), new Vector2(1, 0));
        caption.gameObject.SetActive(false);

        // ---- main ----
        mainPanel = Panel(root, "Main");
        var title = Txt(mainPanel.transform, "Title", 150, TextAnchor.MiddleLeft, Red);
        Place(title.rectTransform, new Vector2(0, 0.5f), new Vector2(120, 290), new Vector2(1400, 190), new Vector2(0, 0.5f));
        title.text = gameTitle;
        var o = title.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(4, -4);
        var tag = Txt(mainPanel.transform, "Tagline", 34, TextAnchor.MiddleLeft, new Color(0.8f, 0.78f, 0.75f));
        Place(tag.rectTransform, new Vector2(0, 0.5f), new Vector2(128, 190), new Vector2(1200, 50), new Vector2(0, 0.5f));
        tag.fontStyle = FontStyle.Italic; tag.text = tagline;

        float y = 90f;
        continueButton = MenuButton(mainPanel.transform, "CONTINUE", new Vector2(128, y), () => StartGame(false)); y -= 84f;
        MenuButton(mainPanel.transform, "START GAME", new Vector2(128, y), () => ShowDifficulty(true)); y -= 84f;
        MenuButton(mainPanel.transform, "CONTROLS", new Vector2(128, y), () => ShowControls(true)); y -= 84f;
        var tb = MenuButton(mainPanel.transform, "THEME", new Vector2(128, y), ToggleTheme); y -= 84f;
        themeLabel = tb.GetComponentInChildren<Text>();
        themeLabel.text = ThemeText();
        MenuButton(mainPanel.transform, "CREDITS", new Vector2(128, y), () => ShowCredits(true)); y -= 84f;
        MenuButton(mainPanel.transform, "QUIT", new Vector2(128, y), Quit);

        // bottom left: the maker, on a blue highlight
        var badge = Img(mainPanel.transform, "Made By", null, new Color(0.08f, 0.32f, 0.95f, 0.9f));
        Place(badge.rectTransform, new Vector2(0, 0), new Vector2(40, 36), new Vector2(560, 62), new Vector2(0, 0));
        badge.gameObject.AddComponent<Outline>().effectColor = new Color(0.5f, 0.75f, 1f, 0.9f);
        var bt = Txt(badge.transform, "Text", 32, TextAnchor.MiddleCenter, Color.white);
        Stretch(bt.rectTransform);
        bt.text = "A GAME BY AYUSH PANKAJ";
        var copy = MenuButton(mainPanel.transform, "COPY GITHUB", Vector2.zero, () => { GUIUtility.systemCopyBuffer = githubUrl; Notice("GITHUB COPIED:  " + githubUrl.Replace("https://", "")); });
        var crt0 = (RectTransform)copy.transform;
        crt0.anchorMin = crt0.anchorMax = new Vector2(0, 0); crt0.pivot = new Vector2(0, 0);
        crt0.anchoredPosition = new Vector2(620, 36); crt0.sizeDelta = new Vector2(330, 62);
        copy.GetComponent<Image>().color = new Color(0.08f, 0.32f, 0.95f, 0.35f);
        var ct0 = copy.GetComponentInChildren<Text>(); ct0.fontSize = 30; ct0.alignment = TextAnchor.MiddleCenter;
        notice = Txt(mainPanel.transform, "Notice", 30, TextAnchor.MiddleLeft, Gold);
        Place(notice.rectTransform, new Vector2(0, 0), new Vector2(40, 116), new Vector2(1400, 44), new Vector2(0, 0));
        notice.enabled = false;
        mainPanel.SetActive(false);

        // ---- credits ----
        creditsPanel = Panel(root, "Credits");
        var crb = Img(creditsPanel.transform, "Dim", null, new Color(0, 0, 0, 0.88f)); Stretch(crb.rectTransform);
        var crt = Txt(creditsPanel.transform, "Title", 96, TextAnchor.MiddleCenter, Red);
        Place(crt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 420), new Vector2(1400, 120), new Vector2(0.5f, 0.5f));
        crt.text = "CREDITS";
        var by = Txt(creditsPanel.transform, "Made By", 54, TextAnchor.MiddleCenter, Gold);
        Place(by.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 320), new Vector2(1600, 70), new Vector2(0.5f, 0.5f));
        by.text = "A GAME MADE BY AYUSH PANKAJ";
        var by2 = Txt(creditsPanel.transform, "Role", 32, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.85f));
        Place(by2.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 268), new Vector2(1600, 50), new Vector2(0.5f, 0.5f));
        by2.text = "Solo game developer  -  design, levels, gameplay and story";
        string[,] credits =
        {
            { "MUSIC", "Meaningful Love (slowed instrumental)" },
            { "SOUND EFFECTS", "Minecraft zombie sounds  -  Quake 1 zombie pain  -  Town zombie call  -  ZG survival victory & defeat\nTom scream  -  Desert Eagle (CS) & gunshot  -  Kenney: Impact Sounds & RPG Audio" },
            { "3D ASSETS", "KayKit by Kay Lousberg: Adventurers, Skeletons, Halloween, Dungeon & Medieval packs\nRPG Poly Pack - Lite (Unity Asset Store)" },
            { "CODE", "C# in Unity" },
            { "GITHUB", "github.com/ayushpankaj20-tech   (GitHub_Ayush_Here)  -  see the code" },
        };
        for (int i = 0; i < credits.GetLength(0); i++)
        {
            float cy = 180f - i * 112f;
            var h = Txt(creditsPanel.transform, "H" + i, 30, TextAnchor.MiddleCenter, Red);
            Place(h.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, cy), new Vector2(1700, 40), new Vector2(0.5f, 0.5f));
            h.text = credits[i, 0];
            var v = Txt(creditsPanel.transform, "V" + i, 28, TextAnchor.UpperCenter, Color.white);
            Place(v.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, cy - 58f), new Vector2(1700, 70), new Vector2(0.5f, 0.5f));
            v.fontStyle = FontStyle.Normal;
            v.text = credits[i, 1];
        }
        MenuButton(creditsPanel.transform, "BACK", new Vector2(0, -420), () => ShowCredits(false), true);
        creditsPanel.SetActive(false);

        // ---- pause ----
        pausePanel = Panel(root, "Pause");
        var pb = Img(pausePanel.transform, "Dim", null, new Color(0, 0, 0, 0.78f)); Stretch(pb.rectTransform);
        var pt = Txt(pausePanel.transform, "Title", 110, TextAnchor.MiddleCenter, Red);
        Place(pt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(1200, 140), new Vector2(0.5f, 0.5f));
        pt.text = "PAUSED";
        pt.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
        pauseStage = Txt(pausePanel.transform, "Stage", 34, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.85f));
        Place(pauseStage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 275), new Vector2(1400, 50), new Vector2(0.5f, 0.5f));
        var box = Img(pausePanel.transform, "Objective Box", null, new Color(0.08f, 0.02f, 0.02f, 0.85f));
        Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(1300, 190), new Vector2(0.5f, 0.5f));
        box.gameObject.AddComponent<Outline>().effectColor = new Color(0.6f, 0.05f, 0.04f, 0.9f);
        var oh = Txt(box.transform, "Header", 32, TextAnchor.UpperLeft, Gold);
        Place(oh.rectTransform, new Vector2(0, 1), new Vector2(30, -18), new Vector2(1240, 44), new Vector2(0, 1));
        oh.text = "OBJECTIVE";
        pauseObjective = Txt(box.transform, "Objective", 34, TextAnchor.UpperLeft, Color.white);
        Place(pauseObjective.rectTransform, new Vector2(0, 1), new Vector2(30, -66), new Vector2(1240, 120), new Vector2(0, 1));
        pauseObjective.horizontalOverflow = HorizontalWrapMode.Wrap;
        pauseObjective.fontStyle = FontStyle.Normal;
        y = -10f;
        MenuButton(pausePanel.transform, "RESUME", new Vector2(0, y), () => GameManager.I.SetPaused(false), true); y -= 84f;
        MenuButton(pausePanel.transform, "CONTROLS", new Vector2(0, y), () => ShowControls(true), true); y -= 84f;
        MenuButton(pausePanel.transform, "RESTART CHECKPOINT", new Vector2(0, y), () => GameManager.I.RestartCheckpoint(), true); y -= 84f;
        MenuButton(pausePanel.transform, "MAIN MENU", new Vector2(0, y), () => GameManager.I.GoToMainMenu(), true); y -= 84f;
        MenuButton(pausePanel.transform, "QUIT", new Vector2(0, y), Quit, true);
        pausePanel.SetActive(false);

        // ---- controls ----
        controlsPanel = Panel(root, "Controls");
        var cb = Img(controlsPanel.transform, "Dim", null, new Color(0, 0, 0, 0.86f)); Stretch(cb.rectTransform);
        var ct = Txt(controlsPanel.transform, "Title", 96, TextAnchor.MiddleCenter, Red);
        Place(ct.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 380), new Vector2(1200, 130), new Vector2(0.5f, 0.5f));
        ct.text = "CONTROLS";
        string[,] rows =
        {
            { "W  A  S  D   /   Arrow keys", "Move" },
            { "Shift (hold)", "Run" },
            { "Mouse", "Look around" },
            { "Right mouse (hold)", "Aim  (you can walk while aiming)" },
            { "Left mouse  (while aiming)", "Shoot" },
            { "R", "Reload" },
            { "Walk over an item", "Pick up keys, ammo and health" },
            { "Esc  or  P", "Pause menu  /  objective" },
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float ry = 250f - i * 68f;
            var k = Txt(controlsPanel.transform, "Key " + i, 36, TextAnchor.MiddleRight, Gold);
            Place(k.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-40, ry), new Vector2(700, 60), new Vector2(1, 0.5f));
            k.text = rows[i, 0];
            var a = Txt(controlsPanel.transform, "Action " + i, 36, TextAnchor.MiddleLeft, Color.white);
            Place(a.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40, ry), new Vector2(800, 60), new Vector2(0, 0.5f));
            a.text = rows[i, 1]; a.fontStyle = FontStyle.Normal;
        }
        MenuButton(controlsPanel.transform, "BACK", new Vector2(0, -330), () => ShowControls(false), true);
        controlsPanel.SetActive(false);

        // ---- difficulty ----
        difficultyPanel = Panel(root, "Difficulty");
        var db = Img(difficultyPanel.transform, "Dim", null, new Color(0, 0, 0, 0.8f)); Stretch(db.rectTransform);
        var dt = Txt(difficultyPanel.transform, "Title", 90, TextAnchor.MiddleCenter, Red);
        Place(dt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 440), new Vector2(1400, 120), new Vector2(0.5f, 0.5f));
        dt.text = "CHOOSE DIFFICULTY";
        DifficultyCard("EASY", new Vector2(-430, 10), new Color(0.3f, 0.85f, 0.4f), "EASY  -  FOR KIDS",
            "Double health: you can take 14 hits.\n\nYour gun does double damage.\n\nIn stage 3 the arena main gate stays OPEN behind you, so you can step back out.",
            () => ChooseDifficulty(false));
        DifficultyCard("HARD", new Vector2(430, 10), new Color(0.95f, 0.15f, 0.1f), "HARDCORE  -  RECOMMENDED",
            "The real game: 7 hits of health, normal gun damage.\n\nEvery enemy is a little faster and the bosses are TWICE as fast.\n\nIn stage 3 the arena main gate slams SHUT behind you: no way out.",
            () => ChooseDifficulty(true));
        MenuButton(difficultyPanel.transform, "BACK", new Vector2(0, -420), () => ShowDifficulty(false), true);
        difficultyPanel.SetActive(false);

        // ---- story intro: one line of story at a time, then the credit ----
        introPanel = Panel(root, "Intro");
        introText = Txt(introPanel.transform, "Story", 50, TextAnchor.MiddleCenter, Color.white);
        Place(introText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 190), new Vector2(1800, 80), new Vector2(0.5f, 0.5f));
        introText.fontStyle = FontStyle.BoldAndItalic;
        creditTitle = Txt(introPanel.transform, "Credit Title", 150, TextAnchor.MiddleCenter, Red);
        Place(creditTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1600, 190), new Vector2(0.5f, 0.5f));
        creditTitle.text = gameTitle;
        creditTitle.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
        creditSub = Txt(introPanel.transform, "Credit", 44, TextAnchor.MiddleCenter, Color.white);
        Place(creditSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1600, 70), new Vector2(0.5f, 0.5f));
        creditSub.text = creditLine;
        skipHint = Txt(introPanel.transform, "Skip", 26, TextAnchor.LowerRight, new Color(0.8f, 0.8f, 0.8f, 0.6f));
        Place(skipHint.rectTransform, new Vector2(1, 0), new Vector2(-50, 40), new Vector2(600, 40), new Vector2(1, 0));
        skipHint.text = "SPACE  -  skip";
        introPanel.SetActive(false);

        // in a web browser: no QUIT (a web page cannot close itself), and GitHub opens instead of copying
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            foreach (var t in root.GetComponentsInChildren<Button>(true)) if (t.name == "QUIT") t.gameObject.SetActive(false);
            var gh = mainPanel.transform.Find("COPY GITHUB");
            if (gh != null)
            {
                gh.GetComponentInChildren<Text>().text = "OPEN GITHUB";
                var b = gh.GetComponent<Button>();
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => Application.OpenURL(githubUrl));
            }
        }

        // full-screen black for fading between clips and into the game (on top of everything)
        fade = Img(root, "Fade", null, Color.clear); Stretch(fade.rectTransform);
    }

    void DifficultyCard(string label, Vector2 pos, Color colour, string tag, string description, UnityEngine.Events.UnityAction onClick)
    {
        var card = Img(difficultyPanel.transform, label + " Card", null, new Color(0.05f, 0.03f, 0.03f, 0.93f));
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(760, 660), new Vector2(0.5f, 0.5f));
        var edge = card.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(colour.r, colour.g, colour.b, 0.9f); edge.effectDistance = new Vector2(3, -3);
        var head = Txt(difficultyPanel.transform, label + " Title", 84, TextAnchor.MiddleCenter, colour);
        Place(head.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(0, 260), new Vector2(700, 100), new Vector2(0.5f, 0.5f));
        head.text = label;
        var d = Txt(difficultyPanel.transform, label + " Text", 33, TextAnchor.UpperCenter, new Color(0.9f, 0.88f, 0.85f));
        Place(d.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(0, 40), new Vector2(660, 300), new Vector2(0.5f, 0.5f));
        d.horizontalOverflow = HorizontalWrapMode.Wrap; d.fontStyle = FontStyle.Normal;
        d.text = description;
        // fully highlighted strip at the bottom of the card
        var strip = Img(difficultyPanel.transform, label + " Tag", null, colour);
        Place(strip.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(0, -160), new Vector2(760, 70), new Vector2(0.5f, 0.5f));
        var st = Txt(strip.transform, "Text", 40, TextAnchor.MiddleCenter, Color.white);
        Stretch(st.rectTransform);
        st.text = tag;
        // START button
        var b = MenuButton(difficultyPanel.transform, "START", pos + new Vector2(0, -255), onClick, true);
        b.name = label + " START";
        var bg = b.GetComponent<Image>(); bg.color = new Color(colour.r * 0.35f, colour.g * 0.35f, colour.b * 0.35f, 0.95f);
        ((RectTransform)b.transform).sizeDelta = new Vector2(420, 84);
        b.GetComponentInChildren<Text>().fontSize = 56;
    }

    Button MenuButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick, bool centred = false)
    {
        var go = new GameObject(label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hit = go.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0.001f);   // invisible click area
        var rt = (RectTransform)go.transform;
        Place(rt, centred ? new Vector2(0.5f, 0.5f) : new Vector2(0, 0.5f), pos, new Vector2(centred ? 700 : 620, 76), centred ? new Vector2(0.5f, 0.5f) : new Vector2(0, 0.5f));
        var t = Txt(go.transform, "Label", 50, centred ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, Color.white);
        Stretch(t.rectTransform);
        t.text = label;
        var b = go.AddComponent<Button>();
        b.targetGraphic = t;
        var cbk = b.colors;
        cbk.normalColor = new Color(0.86f, 0.84f, 0.8f); cbk.highlightedColor = new Color(1f, 0.2f, 0.15f);
        cbk.selectedColor = new Color(1f, 0.2f, 0.15f); cbk.pressedColor = new Color(0.6f, 0.05f, 0.05f);
        cbk.fadeDuration = 0.08f;
        b.colors = cbk;
        b.onClick.AddListener(() => Sfx.Play(Sfx.I != null ? Sfx.I.key : null, 0.35f));
        b.onClick.AddListener(onClick);
        go.AddComponent<HoverSelect>();
        return b;
    }

    GameObject Panel(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        return go;
    }

    Image Img(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    Text Txt(Transform parent, string name, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = anchor; t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.85f); sh.effectDistance = new Vector2(2, -2);
        return t;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Sprite GradientSprite()
    {
        var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 256; x++)
        {
            float a = Mathf.Clamp01(1f - Mathf.Pow(x / 255f, 1.6f));
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 256, 4), new Vector2(0.5f, 0.5f));
    }
}

// the mouse pointing at a button selects it, so mouse and keyboard highlight the same way
public class HoverSelect : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData e)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
    }
}
