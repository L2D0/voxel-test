// Global per-machine state. Every machine has exactly one local player,
// so "is my menu open" lives here safely even in multiplayer.
public static class GameState
{
    public static bool MenuOpen;
    public static bool InGame;   // true once this machine's own player has spawned
}