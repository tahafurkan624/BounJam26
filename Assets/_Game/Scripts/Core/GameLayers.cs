namespace BounJam.Core
{
    /// <summary>
    /// Physics layers used by gameplay code. The editor's ProjectSetup writes these names into
    /// Tags &amp; Layers, so add or rename layers here only.
    /// </summary>
    public static class GameLayers
    {
        public const int Default = 0;
        public const int Ground = 6;        // static level geometry you can stand on
        public const int Player = 7;
        public const int Enemy = 8;
        public const int Pushable = 9;      // loose rigidbodies that liquid can shove
        public const int Tree = 10;         // standing and fallen trees; they block liquid
        public const int Interactable = 11; // buttons, fires and other things liquid acts on
        public const int Trigger = 12;      // pickups, kill zones and other trigger volumes

        public static readonly (int index, string name)[] Custom =
        {
            (Ground, nameof(Ground)),
            (Player, nameof(Player)),
            (Enemy, nameof(Enemy)),
            (Pushable, nameof(Pushable)),
            (Tree, nameof(Tree)),
            (Interactable, nameof(Interactable)),
            (Trigger, nameof(Trigger)),
        };

        /// <summary>Everything physical except the player and trigger volumes: what aim rays and liquid hit.</summary>
        public static readonly int Solid = MaskOf(Default, Ground, Enemy, Pushable, Tree, Interactable);

        public static int MaskOf(params int[] layers)
        {
            int mask = 0;
            foreach (int layer in layers)
                mask |= 1 << layer;
            return mask;
        }
    }
}
