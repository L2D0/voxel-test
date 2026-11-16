using System;

public static class Settings
{
    public static event Action<int> DrawDistanceChanged;

    private static int _drawDistance = 4;

    public static int DrawDistance
    {
        get => _drawDistance;
        set
        {
            if (_drawDistance == value) return;
            _drawDistance = value;
            DrawDistanceChanged?.Invoke(value);
        }
    }
}