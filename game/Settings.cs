using Godot;
using System;

// Player settings, kept in user://settings.cfg so they survive restarts. Static, so anything can read them;
// whatever needs to react to a change listens to Changed (or DrawDistanceChanged for the world).
public static class Settings
{
    private const string File = "user://settings.cfg";

    public static event Action Changed;
    public static event Action<int> DrawDistanceChanged;

    private static int _drawDistance = 4;
    public static int DrawDistance
    {
        get => _drawDistance;
        set
        {
            value = Math.Clamp(value, 2, 12);
            if (_drawDistance == value) return;
            _drawDistance = value;
            DrawDistanceChanged?.Invoke(value);
            Changed?.Invoke();
        }
    }

    public static float MouseSensitivity { get; private set; } = 1f;   // multiplies the camera's base sensitivity
    public static float Fov { get; private set; } = 75f;               // third person; first person adds 5
    public static bool CrouchToggle { get; private set; }              // false = hold C, true = press to toggle
    public static bool Shadows { get; private set; } = true;

    public static void SetMouseSensitivity(float value) { MouseSensitivity = Mathf.Clamp(value, 0.1f, 3f); Changed?.Invoke(); }
    public static void SetFov(float value) { Fov = Mathf.Clamp(value, 60f, 100f); Changed?.Invoke(); }
    public static void SetCrouchToggle(bool value) { CrouchToggle = value; Changed?.Invoke(); }
    public static void SetShadows(bool value) { Shadows = value; Changed?.Invoke(); }

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(File) != Error.Ok) return;   // first run: defaults

        _drawDistance = Math.Clamp((int)config.GetValue("video", "draw_distance", _drawDistance), 2, 12);
        Fov = (float)config.GetValue("video", "fov", Fov);
        Shadows = (bool)config.GetValue("video", "shadows", Shadows);
        MouseSensitivity = (float)config.GetValue("controls", "mouse_sensitivity", MouseSensitivity);
        CrouchToggle = (bool)config.GetValue("controls", "crouch_toggle", CrouchToggle);
        Changed?.Invoke();
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("video", "draw_distance", _drawDistance);
        config.SetValue("video", "fov", Fov);
        config.SetValue("video", "shadows", Shadows);
        config.SetValue("controls", "mouse_sensitivity", MouseSensitivity);
        config.SetValue("controls", "crouch_toggle", CrouchToggle);
        config.Save(File);
    }

    // Shadows live on the scene's lights; call after a change or when a scene loads
    public static void ApplyShadows(Node root)
    {
        if (root is DirectionalLight3D light) light.ShadowEnabled = Shadows;
        foreach (Node child in root.GetChildren()) ApplyShadows(child);
    }
}
