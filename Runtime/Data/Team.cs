namespace TakoBoyStudios.TopDown2D
{
    [System.Flags]
    public enum Team
    {
        Player = 1 << 1,
        Enemy = 1 << 2,
        Any = Player | Enemy,
    }
}
