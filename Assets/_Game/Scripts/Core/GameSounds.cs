using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The FMOD events of the level's machines and items. They are built into the scenes by the level kits rather than
    /// placed from prefabs, so instead of an event field on every one they look the event up here by its path in the
    /// banks (renaming an event in FMOD Studio means changing its path here too). The blob and the ship carry their
    /// events as fields on their prefabs instead (see <see cref="BlobSounds"/>, <see cref="ShipSounds"/>).
    /// </summary>
    public static class GameSounds
    {
        public static readonly Sound Tree = new Sound("event:/Tree");
        public static readonly Sound Doors = new Sound("event:/Doors");
        public static readonly Sound Buttons = new Sound("event:/Buttons");
        public static readonly Sound ItemCollect = new Sound("event:/Item_Collect");
        public static readonly Sound Magnet = new Sound("event:/Magnet");
        public static readonly Sound Elevator = new Sound("event:/Elevator");
        public static readonly Sound ConveyorBelt = new Sound("event:/Conveyor_Belt");
        public static readonly Sound Level = new Sound("event:/Level");

        // Item_Collect's "Parameter 1" labels, one per kind of item.
        public const string ItemParameter = "Parameter 1";
        public const string ItemMagnet = "Magnet";
        public const string ItemWater = "Water";
        public const string ItemSeed = "Seed";

        // Magnet's parameter that switches from its loop (0) to its ending (1).
        public const string MagnetEndParameter = "Parameter 2";

        // Level's "Level" labels.
        public const string LevelParameter = "Level";
        public const string LevelSuccess = "Success";
        public const string LevelFail = "Fail";

        /// <summary>An event by path, looked up in the loaded banks the first time it's used.</summary>
        public class Sound
        {
            readonly string path;
            EventReference reference;
            bool looked;

            public Sound(string path) => this.path = path;

            /// <summary>The event; null when it isn't in the banks, with a warning the first time.</summary>
            public EventReference Reference
            {
                get
                {
                    if (!looked)
                    {
                        looked = true;
                        reference = RuntimeManager.PathToEventReference(path);
                        if (reference.IsNull) Debug.LogWarning($"[BounJam] FMOD event {path} not found in the banks.");
                    }
                    return reference;
                }
            }

            /// <summary>Plays it once at a point.</summary>
            public void PlayAt(Vector3 position)
            {
                if (!Reference.IsNull) RuntimeManager.PlayOneShot(Reference, position);
            }

            /// <summary>Plays it once at a point with one parameter set to a label.</summary>
            public void PlayAt(Vector3 position, string parameter, string label)
            {
                if (Reference.IsNull) return;
                var instance = RuntimeManager.CreateInstance(Reference);
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
                instance.setParameterByNameWithLabel(parameter, label);
                instance.start();
                instance.release();
            }
        }
    }
}
