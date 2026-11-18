using System.Collections.Generic;
using UnityEngine;

// NIGHT (the default) or MORNING, picked in the main menu and remembered. The night look is whatever the scene
// was built with; morning swaps in a bright sky, a warm high sun, lighter ambient light and fog, and dims the lamps.
public static class Theme
{
    const string Pref = "EvilResident_Morning", UnlockPref = "EvilResident_DayUnlocked";
    // DAY is locked until the game has been beaten at NIGHT
    public static bool DayUnlocked
    {
        get { return PlayerPrefs.GetInt(UnlockPref, 0) == 1; }
        set { PlayerPrefs.SetInt(UnlockPref, value ? 1 : 0); PlayerPrefs.Save(); }
    }
    public static bool Morning
    {
        get { return DayUnlocked && PlayerPrefs.GetInt(Pref, 0) == 1; }
        set { PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // the scene's own night settings, read once per loaded scene
    static Light sun;
    static Material nightSky, morningSky;
    static Color nightSunColour, ambSky, ambEq, ambGround, fogColour;
    static float nightSunIntensity, fogDensity;
    static Quaternion nightSunRot;
    static readonly Dictionary<Light, float> lamps = new Dictionary<Light, float>();

    static void Capture()
    {
        if (sun != null && sun == RenderSettings.sun) return;
        sun = RenderSettings.sun;
        nightSky = RenderSettings.skybox;
        if (sun != null) { nightSunColour = sun.color; nightSunIntensity = sun.intensity; nightSunRot = sun.transform.rotation; }
        ambSky = RenderSettings.ambientSkyColor; ambEq = RenderSettings.ambientEquatorColor; ambGround = RenderSettings.ambientGroundColor;
        fogColour = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
        lamps.Clear();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Point) lamps[l] = l.intensity;

        morningSky = nightSky != null ? new Material(nightSky) : null;
        if (morningSky != null)
        {
            morningSky.SetColor("_Top", new Color(0.22f, 0.45f, 0.85f));
            morningSky.SetColor("_Mid", new Color(0.55f, 0.72f, 0.95f));
            morningSky.SetColor("_Horizon", new Color(1f, 0.8f, 0.58f));
            morningSky.SetColor("_HorizonDim", new Color(0.78f, 0.84f, 0.93f));
            morningSky.SetColor("_Ground", new Color(0.32f, 0.35f, 0.38f));
            morningSky.SetColor("_MoonColor", new Color(1f, 0.96f, 0.82f));
            morningSky.SetFloat("_MoonSize", 0.06f);
            morningSky.SetFloat("_Stars", 0f);
        }
    }

    public static void Apply(bool morning)
    {
        Capture();
        if (morning)
        {
            var rot = Quaternion.Euler(42f, 150f, 0f);
            if (sun != null) { sun.color = new Color(1f, 0.93f, 0.8f); sun.intensity = 2.1f; sun.transform.rotation = rot; }
            if (morningSky != null) { morningSky.SetVector("_MoonDir", -(rot * Vector3.forward)); RenderSettings.skybox = morningSky; }
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.68f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.54f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.21f, 0.17f);
            RenderSettings.fogColor = new Color(0.7f, 0.77f, 0.86f);
            RenderSettings.fogDensity = fogDensity * 0.5f;
            foreach (var kv in lamps) if (kv.Key != null) kv.Key.intensity = kv.Value * 0.3f;
        }
        else
        {
            if (sun != null) { sun.color = nightSunColour; sun.intensity = nightSunIntensity; sun.transform.rotation = nightSunRot; }
            if (nightSky != null) RenderSettings.skybox = nightSky;
            RenderSettings.ambientSkyColor = ambSky; RenderSettings.ambientEquatorColor = ambEq; RenderSettings.ambientGroundColor = ambGround;
            RenderSettings.fogColor = fogColour; RenderSettings.fogDensity = fogDensity;
            foreach (var kv in lamps) if (kv.Key != null) kv.Key.intensity = kv.Value;
        }
        DynamicGI.UpdateEnvironment();
    }
}
