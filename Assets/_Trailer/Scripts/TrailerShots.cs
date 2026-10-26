#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BounJam.Trailer
{
    /// <summary>The trailer's shot list. Ids start with the scene or the subject so queue patterns (L01*) pick groups.</summary>
    public static partial class TrailerShots
    {
        public static IEnumerable<Shot> All()
        {
            yield return new Shot
            {
                id = "test_L01_follow",
                scene = "Level01",
                note = "smoke test: the game's camera while the blob rolls forward",
                action = d => Test(d),
            };
            yield return new Shot
            {
                id = "stem_music",
                scene = TrailerDirector.Silence,
                note = "the music on its own, at full level, for the edit",
                music = true,
                audioOnly = true,
                leadIn = 0f,
                tail = 0f,
                settle = 0.5f,
                setup = d =>
                {
                    TrailerDirector.SetMusic(1f);
                    return d.Wait(0f);
                },
                action = d => RestartMusic(d),
            };
            foreach (Shot shot in Planned()) yield return shot;
        }

        // Game_Music is 144.4 s long.
        public static float MusicStemSeconds = 147f;

        /// <summary>Starts the music over from its first note (GameMusic otherwise plays on from the game's start).</summary>
        static IEnumerator RestartMusic(TrailerDirector d)
        {
            GameObject old = GameObject.Find("GameMusic");
            if (old != null) Object.Destroy(old);
            yield return null;
            typeof(GameMusic).GetMethod("Install", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                             ?.Invoke(null, null);
            TrailerDirector.SetMusic(1f);
            d.Mark("music start");
            yield return d.Wait(MusicStemSeconds);
        }

        static IEnumerator Test(TrailerDirector d)
        {
            d.Mark("go");
            yield return d.MoveFor(new Vector2(0f, 1f), 2.5f);
            d.Mark("stop");
        }

        /// <summary>
        /// The shots from every <c>static void Shots*(List&lt;Shot&gt;)</c> method of this partial class (one file per
        /// level under Shots/), in name order.
        /// </summary>
        static IEnumerable<Shot> Planned()
        {
            var shots = new List<Shot>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Public;
            foreach (System.Reflection.MethodInfo method in typeof(TrailerShots).GetMethods(flags).OrderBy(m => m.Name))
            {
                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                if (method.Name.StartsWith("Shots") && parameters.Length == 1 && parameters[0].ParameterType == typeof(List<Shot>))
                    method.Invoke(null, new object[] { shots });
            }
            return shots;
        }
    }
}
#endif
