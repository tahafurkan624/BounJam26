#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // MainMenu: the island diorama and its blob stunt. MenuBlobStunt (on "Stunt") loops: the jelly waits at Start
    // (0, 0.16, -8), sprints up the lane along +Z, up the ramp and splats into the wall on the upper deck, hops off it,
    // freezes into a crystal in mid-air, tumbles back down the ramp and melts once it rests. No LevelManager, no GlassUI,
    // no Cinemachine: MenuCameraOrbit moves the Main Camera directly, and the menu (MenuUI, MainMenu) is a Screen Space
    // Camera canvas on it, so every shot hides or keeps it itself.
    // Geometry (LevelPiece sizes from the scene): island floor top y 0, x -5..5, z -11..3; ramp x -1.25..1.25,
    // z -2.5..2.5 rising y 0 -> 2; upper deck top y 2, x -5..5, z 2.5..8.5; wall x -2..2, y 2..3.5, z 2.75..3.25;
    // invisible lane rails (layer Ignore Raycast, so they never block cameras) at x +-1.55 and z -10.6; the harbour dock
    // east at (11, 0, 4); the paper ship floats in the sea at (6, -0.9, -9) and is left alone (no LevelManager: no fail).
    // The sun comes from the south-east (+X, -Z), so cameras sit on that side.
    public static partial class TrailerShots
    {
        static void ShotsMainMenu(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "MM_hero",
                scene = "MainMenu",
                note = "hero: high crane-orbit down over the island diorama and harbour while the blob sprints up the ramp, " +
                       "splats into the wall, freezes in mid-air, tumbles down as a crystal and melts (menu hidden)",
                setup = d => MMHeroSetup(d),
                action = d => MMLoopAction(d, "establishing"),
            });
            shots.Add(new Shot
            {
                id = "MM_freeze_side",
                scene = "MainMenu",
                note = "signature: low side view of the ramp top: the blob splats into the wall, hops and freezes into a " +
                       "crystal in mid-air (slow motion), then tumbles down the ramp and melts back into jelly",
                setup = d => MMSideSetup(d),
                action = d => MMSideAction(d),
            });
            shots.Add(new Shot
            {
                id = "MM_face",
                scene = "MainMenu",
                note = "close-up: the blob's face at the foot of the lane, breathing and looking into the lens, then it " +
                       "dashes off up the ramp and splats into the wall and freezes in the distance (follow zoom)",
                setup = d => MMFaceSetup(d),
                action = d => MMFaceAction(d),
                tail = 0.4f,
            });
            shots.Add(new Shot
            {
                id = "MM_title",
                scene = "MainMenu",
                note = "title card: the real menu's liquid-glass ShapeLock title and studio credit (Play/Options/Exit hidden) " +
                       "over the diorama, slow push-in while the blob splats into the wall and freezes below",
                setup = d => MMTitleSetup(d),
                action = d => MMTitleAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "MM_menu",
                scene = "MainMenu",
                note = "gameplay: the real main menu exactly as the player sees it (menu camera, liquid Play/Options/Exit " +
                       "buttons) with one full pass of the blob stunt",
                setup = d => MMMenuSetup(d),
                action = d => MMLoopAction(d, "menu"),
            });
            shots.Add(new Shot
            {
                id = "MM_freeze_v",
                scene = "MainMenu",
                portrait = true,
                note = "portrait: three-quarter view up the ramp: the blob shoots into frame, splats into the wall, freezes " +
                       "in mid-air (slow motion) and tumbles back down past the lens",
                setup = d => MMPortraitSetup(d),
                action = d => MMPortraitAction(d),
            });
        }

        // ---------------------------------------------------------------- Places

        // Where the stunt waits (its Start transform), a hair above the floor.
        static readonly Vector3 MMStart = new Vector3(0f, 0.18f, -8f);
        // Closer to the ramp foot (z -2.5): still full speed (5 m/s after ~1 m) when it reaches the ramp.
        static readonly Vector3 MMRunUp = new Vector3(0f, 0.18f, -4.5f);
        // MenuCameraOrbit's pivot ("Stunt/Camera Pivot"); the menu camera looks 2.6 m above it from 15 m, 6.5 m up.
        static readonly Vector3 MMMenuPivot = new Vector3(0.5f, 1.2f, -1.5f);
        // The middle of the stunt lane, a little east so the harbour stays in the hero frame.
        static readonly Vector3 MMHeroCenter = new Vector3(1f, 0.5f, -3.6f);

        // Side camera: over the sea just east of the island edge (x 5), level with the deck, south of the wall.
        static readonly Vector3 MMSideFrom = new Vector3(5.6f, 1.6f, 1.0f);
        static readonly Vector3 MMSideTo = new Vector3(5.8f, 1.45f, -0.4f);
        static readonly Vector3 MMSideAnchor = new Vector3(0f, 2.0f, 1.0f);

        // Face camera: on the island floor south of Start (the floor ends at z -11, the back rail is invisible).
        static readonly Vector3 MMFaceCamFrom = new Vector3(0.7f, 0.55f, -10.4f);
        static readonly Vector3 MMFaceCamTo = new Vector3(0.5f, 0.5f, -10.0f);
        static readonly Vector3 MMFaceAim = new Vector3(0f, 0.34f, 0f);

        // Portrait camera: beside the lane (outside the rail at x 1.55), south-east of the ramp, above the floor.
        static readonly Vector3 MMTallFrom = new Vector3(2.3f, 1.9f, -2.6f);
        static readonly Vector3 MMTallTo = new Vector3(2.0f, 1.75f, -1.9f);
        static readonly Vector3 MMTallAnchor = new Vector3(0f, 2.2f, 1.6f);

        // ---------------------------------------------------------------- Setups

        static IEnumerator MMHeroSetup(TrailerDirector d)
        {
            MMHideMenu(d);
            MMFreeCamera(d);
            yield return MMStageBlob(d, MMStart, 0.9f);
            d.FaceBlob(Vector3.back);
            // From high and wide south-west of the island, down and round to the south-east, closing in.
            MMCrane(d, MMHeroCenter, 0f, -28f, 18f, 16f, 12.5f, 9f, 5.5f, 11f, 42f, 40f);
            d.Cam.Sway(0.25f);
            MMStartStunt(d, 1.0f);
        }

        static IEnumerator MMSideSetup(TrailerDirector d)
        {
            MMHideMenu(d);
            MMFreeCamera(d);
            yield return MMStageBlob(d, MMRunUp, 0.8f);
            d.FaceBlob(MMSideFrom - MMRunUp);
            MMTrack(d, MMSideFrom, MMSideTo, 7f, MMSideAnchor, 0.6f, -100f, 0.25f, 38f, 35f);
            d.Cam.Sway(0.35f);
            MMStartStunt(d, 0.7f);
        }

        static IEnumerator MMFaceSetup(TrailerDirector d)
        {
            MMHideMenu(d);
            MMFreeCamera(d);
            yield return MMStageBlob(d, MMStart, 1.1f);
            // Eyes into the lens; they hold that way until the blob moves.
            d.FaceBlob(MMFaceCamTo - d.Blob.position);
            MMFaceRig(d);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator MMTitleSetup(TrailerDirector d)
        {
            MMFreeCamera(d);
            MMHideButtons(d);
            MMNoPointer(d);
            yield return MMStageBlob(d, MMStart, 0.9f);
            d.FaceBlob(Vector3.back);
            // The menu camera's own framing (pivot, 2.6 m look height), held steady and pushing in slowly.
            MMCrane(d, MMMenuPivot, 2.6f, -6f, 5f, 15.8f, 14.6f, 6.5f, 6.3f, 7.5f, 40f, 40f);
            MMStartStunt(d, 0.6f);
        }

        static IEnumerator MMMenuSetup(TrailerDirector d)
        {
            MMNoPointer(d);
            MenuCameraOrbit orbit = MMOrbitOf(d);
            if (orbit != null) orbit.enabled = true;
            // In this scene the game's camera is MenuCameraOrbit (there is no Cinemachine brain).
            d.Cam.Gameplay();
            yield return MMStageBlob(d, MMStart, 0.9f);
            d.FaceBlob(Vector3.back);
            MMStartStunt(d, 0.8f);
        }

        static IEnumerator MMPortraitSetup(TrailerDirector d)
        {
            MMHideMenu(d);
            MMFreeCamera(d);
            // Starts behind the camera's plane, out of frame; it shoots in from the bottom as it runs up the ramp.
            yield return MMStageBlob(d, MMRunUp, 0.8f);
            MMTrack(d, MMTallFrom, MMTallTo, 6f, MMTallAnchor, 0.35f, -1.0f, 0.3f, 54f, 50f);
            d.Cam.Sway(0.35f);
            MMStartStunt(d, 0.7f);
        }

        // ---------------------------------------------------------------- Actions

        // One whole pass of the stunt in real time: run, splat, hop, freeze, tumble, melt.
        static IEnumerator MMLoopAction(TrailerDirector d, string opening)
        {
            var beats = new MMBeats(d);
            d.Mark(opening);
            yield return MMWaitSplat(d, 8f, "run");
            yield return d.WaitUntil(() => beats.Frozen, 2f, "the mid-air freeze");
            yield return MMWaitTumble(d, beats);
            yield return d.WaitUntil(() => beats.Melted, 8f, "the melt");
            yield return d.Wait(0.8f);
            beats.Stop();
        }

        static IEnumerator MMSideAction(TrailerDirector d)
        {
            var beats = new MMBeats(d);
            yield return MMWaitSplat(d, 6f, "run");
            yield return MMSlowFreeze(d, beats, 0.3f);
            yield return MMWaitTumble(d, beats);
            yield return d.WaitUntil(() => beats.Melted, 6f, "the melt");
            yield return d.Wait(0.7f);
            beats.Stop();
        }

        static IEnumerator MMFaceAction(TrailerDirector d)
        {
            var beats = new MMBeats(d);
            d.Mark("face");
            // The stunt waits 1.7 s, then the blob dashes away from the lens toward the wall.
            MMStartStunt(d, 1.7f);
            yield return MMWaitSplat(d, 7f, "dash");
            yield return d.WaitUntil(() => beats.Frozen, 1.5f, "the mid-air freeze");
            // Ends as the crystal starts back down the ramp, before it rolls near the lens.
            yield return d.Wait(0.9f);
            beats.Stop();
        }

        static IEnumerator MMTitleAction(TrailerDirector d)
        {
            var beats = new MMBeats(d);
            d.Mark("title");
            yield return MMWaitSplat(d, 7f, "run");
            yield return d.WaitUntil(() => beats.Frozen, 2f, "the mid-air freeze");
            yield return d.Wait(2.4f);
            beats.Stop();
        }

        static IEnumerator MMPortraitAction(TrailerDirector d)
        {
            var beats = new MMBeats(d);
            yield return MMWaitSplat(d, 6f, "run");
            yield return MMSlowFreeze(d, beats, 0.3f);
            yield return MMWaitTumble(d, beats);
            yield return d.Wait(0.5f);
            beats.Stop();
        }

        // ---------------------------------------------------------------- Stunt beats

        /// <summary>
        /// Marks the stunt's hop (BlobMotor.Jumped) and its freeze and melt (BlobForm.FormChanged) the moment they
        /// happen, and keeps flags to wait on. Subscribe at the start of the action, Stop at its end.
        /// </summary>
        sealed class MMBeats
        {
            public bool Hopped, Frozen, Melted;
            readonly TrailerDirector director;
            readonly BlobMotor motor;
            readonly BlobForm form;

            public MMBeats(TrailerDirector d)
            {
                director = d;
                motor = d.Motor;
                form = d.Form;
                if (motor != null) motor.Jumped += OnJumped;
                if (form != null) form.FormChanged += OnFormChanged;
            }

            void OnJumped(float lift)
            {
                if (Hopped) return;
                Hopped = true;
                director.Mark("hop");
            }

            void OnFormChanged(bool solid)
            {
                if (solid && !Frozen)
                {
                    Frozen = true;
                    director.Mark("freeze");
                }
                else if (!solid && Frozen && !Melted)
                {
                    Melted = true;
                    director.Mark("melt");
                }
            }

            public void Stop()
            {
                if (motor != null) motor.Jumped -= OnJumped;
                if (form != null) form.FormChanged -= OnFormChanged;
            }
        }

        /// <summary>
        /// Waits for the run and the splat into the wall, with the stunt's own test (MenuBlobStunt.Run): over 3 m/s up
        /// the lane (+Z), then below 35% of the fastest.
        /// </summary>
        static IEnumerator MMWaitSplat(TrailerDirector d, float timeout, string runLabel)
        {
            Rigidbody body = d.Body;
            float fastest = 0f;
            bool running = false;
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                float speed = Vector3.Dot(body.linearVelocity, Vector3.forward);
                if (!running && speed > 1f)
                {
                    running = true;
                    d.Mark(runLabel);
                }
                fastest = Mathf.Max(fastest, speed);
                if (fastest > 3f && speed < fastest * 0.35f)
                {
                    d.Mark("splat");
                    yield break;
                }
                yield return null;
            }
            Debug.LogWarning($"[Trailer] {d.Shot.id}: timed out waiting for the splat into the wall");
        }

        /// <summary>Slow motion from the splat through the hop and the mid-air freeze, then back to full speed.</summary>
        static IEnumerator MMSlowFreeze(TrailerDirector d, MMBeats beats, float slow)
        {
            // The hop comes 0.1 s after the splat and the freeze 0.2 s after the hop (game time).
            yield return d.SlowTo(slow, 0.15f);
            yield return d.WaitUntil(() => beats.Frozen, 1.5f, "the mid-air freeze");
            yield return d.WaitClip(0.9f);
            yield return d.SlowTo(1f, 0.4f);
        }

        /// <summary>The crystal on its way down the ramp, and off its foot (z -2.5).</summary>
        static IEnumerator MMWaitTumble(TrailerDirector d, MMBeats beats)
        {
            yield return d.WaitUntil(() => beats.Melted || d.Body.position.z < 1.2f, 3f, "the tumble down the ramp");
            if (!beats.Melted) d.Mark("tumble");
            yield return d.WaitUntil(() => beats.Melted || d.Body.position.z < -2.6f, 3f, "the foot of the ramp");
            if (!beats.Melted) d.Mark("off the ramp");
        }

        // ---------------------------------------------------------------- Staging

        static MenuBlobStunt MMStuntOf(TrailerDirector d)
        {
            MenuBlobStunt stunt = d.Get<MenuBlobStunt>("Stunt");
            return stunt != null ? stunt : UnityEngine.Object.FindAnyObjectByType<MenuBlobStunt>();
        }

        static MenuCameraOrbit MMOrbitOf(TrailerDirector d)
        {
            MenuCameraOrbit orbit = d.Cam != null && d.Cam.Camera != null ? d.Cam.Camera.GetComponent<MenuCameraOrbit>() : null;
            return orbit != null ? orbit : UnityEngine.Object.FindAnyObjectByType<MenuCameraOrbit>();
        }

        static MainMenu MMMenuOf(TrailerDirector d)
        {
            MainMenu menu = d.Get<MainMenu>("MenuUI");
            return menu != null ? menu : UnityEngine.Object.FindAnyObjectByType<MainMenu>();
        }

        /// <summary>
        /// Stops the stunt (its coroutine keeps running when it is only disabled), stills the blob and puts it at
        /// <paramref name="at"/> the safe way, then lets the jelly swell and settle.
        /// </summary>
        static IEnumerator MMStageBlob(TrailerDirector d, Vector3 at, float rest)
        {
            MenuBlobStunt stunt = MMStuntOf(d);
            if (stunt != null)
            {
                stunt.StopAllCoroutines();
                stunt.enabled = false;
            }
            d.Halt();
            yield return d.Teleport(at, false);
            d.Halt();
            yield return d.Wait(rest);
        }

        /// <summary>Starts the stunt loop over from wherever the blob is: it waits <paramref name="idle"/> s, then runs.</summary>
        static void MMStartStunt(TrailerDirector d, float idle)
        {
            MenuBlobStunt stunt = MMStuntOf(d);
            if (stunt == null)
            {
                Debug.LogWarning($"[Trailer] {d.Shot.id}: no MenuBlobStunt in the scene");
                return;
            }
            TrailerDirector.SetMember(stunt, "idleTime", idle);
            stunt.StopAllCoroutines();
            stunt.enabled = false;
            stunt.enabled = true;
        }

        /// <summary>Takes the Main Camera from MenuCameraOrbit, which would otherwise fight d.Cam in LateUpdate.</summary>
        static void MMFreeCamera(TrailerDirector d)
        {
            MenuCameraOrbit orbit = MMOrbitOf(d);
            if (orbit != null) orbit.enabled = false;
        }

        /// <summary>
        /// Hides the whole menu (title, studio credit, buttons): its main CanvasGroup goes to alpha 0 and its object is
        /// switched off, which also hides the liquid buttons' 3D bodies (mesh renderers the canvas doesn't draw).
        /// The director only hides GlassUI, which this scene doesn't have.
        /// </summary>
        static void MMHideMenu(TrailerDirector d)
        {
            MainMenu menu = MMMenuOf(d);
            CanvasGroup group = menu != null ? TrailerDirector.GetMember(menu, "mainGroup") as CanvasGroup : null;
            if (group != null) group.alpha = 0f;
            GameObject main = group != null ? group.gameObject : d.Find("MenuUI/Liquid3D/Main");
            if (main != null) main.SetActive(false);
            else Debug.LogWarning($"[Trailer] {d.Shot.id}: couldn't find the menu to hide");
        }

        /// <summary>Hides Play, Options and Exit so the menu reads as a logo card.</summary>
        static void MMHideButtons(TrailerDirector d)
        {
            MainMenu menu = MMMenuOf(d);
            string[] fields = { "playButton", "optionsButton", "exitButton" };
            string[] names = { "PlayButton", "OptionsButton", "ExitButton" };
            for (int i = 0; i < fields.Length; i++)
            {
                Component button = menu != null ? TrailerDirector.GetMember(menu, fields[i]) as Component : null;
                GameObject go = button != null ? button.gameObject : d.Find(names[i]);
                if (go != null) go.SetActive(false);
            }
        }

        /// <summary>A mouse left over the Game view must not hover (lift, tint) a liquid button during the take.</summary>
        static void MMNoPointer(TrailerDirector d)
        {
            MainMenu menu = MMMenuOf(d);
            if (menu == null) return;
            foreach (UnityEngine.UI.GraphicRaycaster raycaster in menu.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true))
                raycaster.enabled = false;
        }

        // ---------------------------------------------------------------- Camera rigs

        /// <summary>
        /// An orbit that also changes radius and height: round <paramref name="center"/> from one angle to another
        /// (d.Cam.Orbit's convention: 0 = on the -Z side, 90 = on the +X side), eased, looking at the centre raised by
        /// <paramref name="lookHeight"/>.
        /// </summary>
        static void MMCrane(TrailerDirector d, Vector3 center, float lookHeight, float degFrom, float degTo, float radiusFrom,
                            float radiusTo, float heightFrom, float heightTo, float seconds, float fovFrom, float fovTo)
        {
            Vector3 look = center + Vector3.up * lookHeight;
            d.Cam.Custom(t =>
            {
                float k = Mathf.Clamp01(t / seconds);
                k = k * k * (3f - 2f * k);
                float angle = Mathf.Deg2Rad * Mathf.Lerp(degFrom, degTo, k);
                float radius = Mathf.Lerp(radiusFrom, radiusTo, k);
                Vector3 position = center + new Vector3(Mathf.Sin(angle) * radius, Mathf.Lerp(heightFrom, heightTo, k),
                                                        -Mathf.Cos(angle) * radius);
                return (position, look, Mathf.Lerp(fovFrom, fovTo, k));
            });
        }

        /// <summary>
        /// A slow dolly from <paramref name="from"/> to <paramref name="to"/> that pans after the blob: it aims between a
        /// fixed <paramref name="anchor"/> and the blob (<paramref name="follow"/> = share of the blob), smoothed, never
        /// following it south of <paramref name="minZ"/>.
        /// </summary>
        static void MMTrack(TrailerDirector d, Vector3 from, Vector3 to, float seconds, Vector3 anchor, float follow, float minZ,
                            float smooth, float fovFrom, float fovTo)
        {
            System.Func<Vector3> aim = () =>
            {
                Vector3 blob = d.Blob.position + Vector3.up * 0.25f;
                blob.z = Mathf.Max(blob.z, minZ);
                return Vector3.Lerp(anchor, blob, follow);
            };
            Vector3 look = aim();
            Vector3 velocity = Vector3.zero;
            d.Cam.Custom(t =>
            {
                float k = Mathf.Clamp01(t / seconds);
                k = k * k * (3f - 2f * k);
                look = Vector3.SmoothDamp(look, aim(), ref velocity, smooth, Mathf.Infinity, Time.deltaTime);
                return (Vector3.Lerp(from, to, k), look, Mathf.Lerp(fovFrom, fovTo, k));
            });
        }

        /// <summary>
        /// Eye-level push-in on the blob's face, then, once it dashes away, a smoothed pan after it that narrows the lens
        /// as it gets further away (so it stays a readable size up at the wall, 12 m off).
        /// </summary>
        static void MMFaceRig(TrailerDirector d)
        {
            Vector3 look = d.Blob.position + MMFaceAim;
            Vector3 velocity = Vector3.zero;
            d.Cam.Custom(t =>
            {
                float k = Mathf.Clamp01(t / 2.6f);
                k = k * k * (3f - 2f * k);
                Vector3 position = Vector3.Lerp(MMFaceCamFrom, MMFaceCamTo, k);
                look = Vector3.SmoothDamp(look, d.Blob.position + MMFaceAim, ref velocity, 0.3f, Mathf.Infinity, Time.deltaTime);
                float away = Mathf.InverseLerp(3f, 11f, Vector3.Distance(position, look));
                return (position, look, Mathf.Lerp(Mathf.Lerp(36f, 32f, k), 22f, away));
            });
        }
    }
}
#endif
