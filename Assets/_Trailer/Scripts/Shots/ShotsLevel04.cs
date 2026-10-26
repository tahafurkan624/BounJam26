#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level04, a two-floor level (Level root at y +7.5). World layout, from the scene's LevelPieces and the height map:
    //   Start platform   x -2.9..3.1, z -3..3, floor 8.01: blob spawn (0.1,8.31,-1.5), Pocket Magnet (0.11,8.32,0),
    //                    'Button Magnet On' (1.98,8.17,1.75) switches on 'Magnet' (5.6,8.2,6.77, face -Z).
    //   Upper deck       x 4.69..8.69, z 5.56..9.56, floor 7.5 ('Button Magnet Off' (5.69,7.5,6.06) under the magnet's
    //                    hold point; magnet housing x 4.75..6.25 z ~7 up to 9.1). Grate floors x 8.5..10.5 east of it.
    //   Lower floor      x 6..14, z 3..13, floor 0: 'Lift' deck 2x2 at (7.67,0.1,4.45) (top 0.2, rises 7.4 m to 7.6).
    //                    Pool x 14..17 z 5.5..10.5 (surface 0, flush); walled alcove x 14.71..16.21 z 10.5..13 north of it
    //                    with 'Crate' (15.5,0.5,11.1) and 'Button' (15.5,0,12.5) (lift + upper Gate + Magnet (1)).
    //                    Dry platform x 14..17 z 3..5.5 south of the pool.
    //   Goal area        x -3..3, z 9..18, floor 7.5, walls 1.5 m (top 9.0); ship parked at (-1.21,7.46,15.21); open to
    //                    the west at z 9.75..11.75 onto the Dock pier (deck 7.5) -> harbour pad (-8.87,7.5,10.77).
    // There is no sea in Level04: everything floats over the camera's light-blue background, so any camera outside the
    // level footprint is in open air.
    public static partial class TrailerShots
    {
        static void ShotsLevel04(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L04_lift",
                scene = "Level04",
                note = "hero crane: the jelly carrying the paper ship rides the 7.4 m lift from the lower floor to the upper deck; " +
                       "the camera rises with it from below and swings round to reveal the upper level, then the blob flows off",
                setup = d => L04LiftSetup(d, false),
                action = d => L04LiftAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L04_lift_v",
                scene = "Level04",
                portrait = true,
                note = "portrait: the blob with the ship rides the tall lift up a whole floor, camera craning with it",
                setup = d => L04LiftSetup(d, true),
                action = d => L04LiftAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L04_magnet",
                scene = "Level04",
                note = "the jelly swallows the iron ball, freezes, rolls onto the magnet button; the magnet lights and yanks the " +
                       "crystal across the void (slow motion), it hits the off button and drops onto the upper deck",
                setup = d => L04MagnetSetup(d),
                action = d => L04MagnetAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L04_press",
                scene = "Level04",
                note = "cause and effect: the crystal paddles across the pool and shoves the crate onto the button, the camera " +
                       "pulls back and the 7.4 m lift across the room sets off up to the next floor",
                setup = d => L04PressSetup(d),
                action = d => L04PressAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L04_play",
                scene = "Level04",
                note = "gameplay camera: from the spawn the blob covers the iron ball, freezes, rolls onto the button and is pulled " +
                       "by the magnet across the gap to the upper deck",
                setup = d => L04PlaySetup(d),
                action = d => L04PlayAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L04_deliver",
                scene = "Level04",
                note = "the blob carries the paper ship out of the walled goal area, along the pier and into the harbour; the pad " +
                       "turns green and the camera rises to the flag being hoisted",
                setup = d => L04DeliverSetup(d),
                action = d => L04DeliverAction(d),
                tail = 0.6f,
            });
        }

        // ------------------------------------------------------------------ places (world space)

        static readonly Vector3 L04LiftRest = new Vector3(7.67f, 0.1f, 4.45f);       // lift box centre at rest, deck top 0.2
        const float L04LiftTravel = 7.4f;
        static readonly Vector3 L04LiftButtonPos = new Vector3(15.5f, 0f, 12.5f);
        static readonly Vector3 L04CratePos = new Vector3(15.5f, 0.5f, 11.1f);
        static readonly Vector3 L04UpperLanding = new Vector3(7.6f, 7.8f, 6.8f);     // upper deck, clear of magnet and grates
        static readonly Vector3 L04LiftShipFacing = new Vector3(1f, 0f, 1f);         // side-on to the south-east crane
        static readonly Vector3 L04IronPos = new Vector3(0.11f, 8.32f, 0f);
        static readonly Vector3 L04OnButtonPos = new Vector3(1.98f, 8.17f, 1.75f);
        static readonly Vector3 L04MagnetPos = new Vector3(5.6f, 8.2f, 6.77f);
        const float L04StartFloor = 8.01f;
        static readonly Vector3 L04PoolShore = new Vector3(15.5f, 0.3f, 3.9f);       // dry platform south of the pool
        static readonly Vector3 L04PoolStart = new Vector3(15.5f, 0.35f, 8.7f);      // crystal dropped in the pool, 1.3 m from the crate
        static readonly Vector3 L04DockPos = new Vector3(-8.87f, 7.5f, 10.77f);
        static readonly Vector3 L04GoalStart = new Vector3(-1.5f, 7.79f, 10.77f);    // goal area floor 7.49, in line with the pier

        // ------------------------------------------------------------------ helpers

        /// <summary>The component on the named object, checked against where it should be; else the nearest of its type.</summary>
        static T L04Find<T>(TrailerDirector d, string name, Vector3 near) where T : Component
        {
            T named = d.Get<T>(name);
            if (named != null && (named.transform.position - near).sqrMagnitude < 9f) return named;
            T best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (T candidate in UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                float distance = (candidate.transform.position - near).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            if (best == null) best = named;
            if (best == null) Debug.LogWarning($"[Trailer] {d.Shot.id}: no {typeof(T).Name} '{name}' found");
            return best;
        }

        /// <summary>Freezes like d.Freeze() but without marking a beat (for staging during setup).</summary>
        static IEnumerator L04FreezeQuiet(TrailerDirector d)
        {
            BlobForm form = d.Form;
            for (int i = 0; i < 20 && !form.IsSolid; i++)
            {
                form.Freeze();
                if (form.IsSolid) break;
                yield return new WaitForFixedUpdate();
            }
            if (!form.IsSolid) Debug.LogWarning($"[Trailer] {d.Shot.id}: the blob wouldn't freeze");
        }

        /// <summary>Moves the frozen crystal (its look is in local space, so it moves cleanly) with its floor memory cleared.</summary>
        static void L04PlaceCrystal(TrailerDirector d, Vector3 position)
        {
            GroundGuard guard = d.Blob.GetComponent<GroundGuard>();
            if (guard != null) guard.enabled = false;
            TrailerDirector.Place(d.Blob, position);
            if (guard != null) guard.enabled = true;
        }

        /// <summary>Eases the camera from wherever it is now to a new pose, aiming at a (possibly moving) point.</summary>
        static void L04Blend(TrailerDirector d, Vector3 toPosition, System.Func<Vector3> toLook, float duration, float toFov)
        {
            Vector3 fromPosition = d.Cam.transform.position;
            Vector3 fromLook = d.Cam.Look;
            float fromFov = d.Cam.Camera.fieldOfView;
            d.Cam.Custom(t =>
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                return (Vector3.Lerp(fromPosition, toPosition, k), Vector3.Lerp(fromLook, toLook(), k), Mathf.Lerp(fromFov, toFov, k));
            });
        }

        /// <summary>Puts the crate on the lower-floor button (it is walled in and off camera); reflection press as a fallback.</summary>
        static void L04PressLiftButton(TrailerDirector d, PressureButton button, Pushable crate)
        {
            if (crate != null)
            {
                Vector3 onPlate = (button != null ? button.transform.position : L04LiftButtonPos) + Vector3.up * 0.5f;
                TrailerDirector.Place(crate.transform, onPlate);
            }
            else if (button != null) TrailerDirector.SetMember(button, "requiredMass", 0f);
        }

        // ------------------------------------------------------------------ L04_lift / L04_lift_v (hero, signature)

        static IEnumerator L04LiftSetup(TrailerDirector d, bool portrait)
        {
            d.Halt();
            if (d.Level != null) d.Level.HoldOnComplete = true;
            ButtonMover lift = L04Find<ButtonMover>(d, "Lift", L04LiftRest);
            Vector3 deck = (lift != null ? lift.transform.position : L04LiftRest) + Vector3.up * 0.1f;
            // The standing jelly (~1.5 m) fits the 2x2 deck; the ship drops onto it side-on to the crane.
            yield return d.Teleport(new Vector3(deck.x, deck.y + 0.3f, deck.z), true, L04LiftShipFacing);
            yield return d.Wait(1.2f);
            yield return d.WaitUntil(() => d.Ship != null && d.Ship.IsFloating, 1.5f, "the ship to float on the jelly");
            yield return d.Wait(0.3f);
            float startAngle = portrait ? 18f : 24f;
            d.FaceBlob(new Vector3(Mathf.Sin(startAngle * Mathf.Deg2Rad), 0f, -Mathf.Cos(startAngle * Mathf.Deg2Rad)));
            L04LiftCamera(d, lift, portrait);
            d.Cam.Sway(portrait ? 0.3f : 0.35f);
        }

        /// <summary>
        /// Crane bound to the lift: the camera stays south-east of the shaft (open air south of z 3), its height follows the
        /// highest the lift has been (so it never sinks when the lift goes back down), starting a touch below the jelly and
        /// ending 2 m above it while swinging from 24 to 54 degrees round the shaft. It aims at the blob, smoothed.
        /// </summary>
        static void L04LiftCamera(TrailerDirector d, ButtonMover lift, bool portrait)
        {
            float startY = lift != null ? lift.transform.position.y : L04LiftRest.y;
            float topY = startY;
            float a0 = portrait ? 18f : 24f, a1 = portrait ? 42f : 54f;
            float r0 = portrait ? 3.4f : 4.3f, r1 = portrait ? 4.3f : 5.2f;
            float h0 = portrait ? -0.35f : -0.2f, h1 = portrait ? 1.7f : 2.3f;
            float fov0 = portrait ? 54f : 38f, fov1 = portrait ? 58f : 42f;
            Vector3 aim = d.Blob.position + Vector3.up * 0.45f;
            float lastT = 0f;
            d.Cam.Custom(t =>
            {
                float dt = Mathf.Max(0f, t - lastT);
                lastT = t;
                float liftY = lift != null ? lift.transform.position.y : startY;
                topY = Mathf.Max(topY, liftY);
                float k = Mathf.Clamp01((topY - startY) / L04LiftTravel);
                float e = Mathf.SmoothStep(0f, 1f, k);
                float angle = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, e);
                float radius = Mathf.Lerp(r0, r1, k);
                float height = Mathf.Lerp(h0, h1, e);
                Vector3 pivot = new Vector3(L04LiftRest.x, topY + 0.8f, L04LiftRest.z);
                Vector3 position = pivot + new Vector3(Mathf.Sin(angle) * radius, height, -Mathf.Cos(angle) * radius);
                Vector3 wanted = d.Blob.position + Vector3.up * 0.45f;
                if (dt > 0f) aim = Vector3.Lerp(aim, wanted, 1f - Mathf.Exp(-10f * dt));
                return (position, aim, Mathf.Lerp(fov0, fov1, k));
            });
        }

        static IEnumerator L04LiftAction(TrailerDirector d)
        {
            ButtonMover lift = L04Find<ButtonMover>(d, "Lift", L04LiftRest);
            PressureButton button = L04Find<PressureButton>(d, "Button", L04LiftButtonPos);
            Pushable crate = L04Find<Pushable>(d, "Crate", L04CratePos);
            d.Mark("on the lift");
            yield return d.Wait(0.3f);
            L04PressLiftButton(d, button, crate);
            yield return d.WaitUntil(() => button == null || button.IsPressed, 0.5f, "the crate on the lift button");
            if (button != null && !button.IsPressed)
            {
                TrailerDirector.SetMember(button, "requiredMass", 0f);
                yield return d.WaitUntil(() => button.IsPressed, 0.3f, "the lift button (reflection)");
            }
            yield return d.WaitUntil(() => lift == null || lift.transform.position.y > L04LiftRest.y + 0.05f, 1.5f, "the lift to set off");
            d.Mark("lift sets off");
            yield return d.WaitUntil(() => lift == null || lift.Progress > 0.5f, 3f, "half way up");
            d.Mark("half way");
            yield return d.WaitUntil(() => lift == null || lift.Progress > 0.995f, 3f, "the top");
            d.Mark("top");
            // The lift stops dead: let the ship settle back into the jelly, then flow off before it heads down (1.5 s).
            yield return d.Wait(0.4f);
            d.Mark("step off");
            yield return d.Steer(L04UpperLanding, 0.45f, 0.35f, 2.5f);
            d.Mark("on the upper deck");
            yield return d.Wait(0.9f);
        }

        // ------------------------------------------------------------------ magnet flight (L04_magnet, L04_play)

        static IEnumerator L04MagnetSetup(TrailerDirector d)
        {
            d.Halt();
            Collectible iron = L04Find<Collectible>(d, "Pocket Magnet", L04IronPos);
            Vector3 at = iron != null ? iron.transform.position : L04IronPos;
            // The ship stays parked upstairs in the goal area.
            yield return d.Teleport(new Vector3(at.x, L04StartFloor + 0.3f, at.z), false);
            yield return d.Wait(1.0f);
            yield return d.WaitUntil(() => iron == null || iron.IsEngulfed, 2f, "the iron ball inside the jelly");
            d.FaceBlob(new Vector3(0.85f, 0f, -0.5f));
            // Side-on from the south-east, over the void: the iron spot and button on the left, the gap in the middle,
            // the magnet and upper deck on the right, the goal area and harbour far behind.
            d.Cam.Dolly(new Vector3(9.1f, 9.8f, -1.7f), new Vector3(8.7f, 9.65f, -1.2f), new Vector3(3.0f, 8.35f, 3.3f), 7f, 42f, 40f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L04MagnetAction(TrailerDirector d)
        {
            d.Mark("iron glows");
            yield return d.Wait(0.5f);
            yield return L04MagnetRun(d, true);
        }

        static IEnumerator L04PlaySetup(TrailerDirector d)
        {
            d.Halt();
            d.Cam.Gameplay();
            yield return d.Wait(0.4f);
        }

        static IEnumerator L04PlayAction(TrailerDirector d)
        {
            Collectible iron = L04Find<Collectible>(d, "Pocket Magnet", L04IronPos);
            Vector3 at = iron != null ? iron.transform.position : L04IronPos;
            d.Mark("go");
            yield return d.Steer(new Vector3(at.x, d.Blob.position.y, at.z), 0.45f, 0.12f, 3f);
            d.Mark("over the iron");
            yield return d.WaitUntil(() => iron == null || iron.IsEngulfed, 2.5f, "the iron ball inside the jelly");
            d.Mark("iron glows");
            yield return d.Wait(0.3f);
            yield return L04MagnetRun(d, false);
        }

        /// <summary>
        /// Freeze around the iron, roll onto 'Button Magnet On' until the magnet takes the crystal, then ride the pull to
        /// the hold point, where the crystal presses 'Button Magnet Off', the magnet lets go and it drops onto the deck.
        /// </summary>
        static IEnumerator L04MagnetRun(TrailerDirector d, bool slowMotion)
        {
            PressureButton on = L04Find<PressureButton>(d, "Button Magnet On", L04OnButtonPos);
            Magnet magnet = L04Find<Magnet>(d, "Magnet", L04MagnetPos);
            BlobMagnetism pull = d.Blob.GetComponent<BlobMagnetism>();
            BlobCargo cargo = d.Blob.GetComponent<BlobCargo>();

            yield return d.Freeze();
            if (cargo != null && cargo.Has<IronBall>()) d.Mark("iron caught");
            else Debug.LogWarning($"[Trailer] {d.Shot.id}: the crystal holds no iron; the magnet won't pull");
            yield return d.Wait(0.45f);

            d.Mark("roll");
            Vector3 target = on != null ? on.transform.position : L04OnButtonPos;
            bool pressed = false;
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                if (!pressed && on != null && on.IsPressed)
                {
                    pressed = true;
                    d.Mark("button pressed");
                }
                if (pull != null && pull.Current != null) break;
                Vector3 to = target - d.Blob.position;
                to.y = 0f;
                // Head for the plate centre and stop there: the magnet reaches the crystal (7 m) before it gets there.
                if (to.magnitude < 0.2f) d.Halt();
                else d.Move(d.StickFor(to) * Mathf.Clamp(Mathf.Clamp01(to.magnitude + 0.3f), 0.35f, 0.9f));
                yield return null;
            }
            d.Halt();
            if (!pressed && on != null && on.IsPressed) d.Mark("button pressed");
            if (pull == null || pull.Current == null)
            {
                Debug.LogWarning($"[Trailer] {d.Shot.id}: the magnet never took the crystal");
                yield return d.Wait(1f);
                yield break;
            }

            d.Mark("magnet pull");
            if (slowMotion) yield return d.SlowTo(0.45f, 0.12f);
            yield return d.WaitUntil(() => pull.Current == null || d.Blob.position.z > 4.2f, 2f, "the crystal over the gap");
            d.Mark("over the gap");
            yield return d.WaitUntil(() => pull.Current == null || (magnet != null && !magnet.IsActive), 3f, "the off button under the magnet");
            d.Mark("released");
            if (slowMotion) yield return d.SlowTo(1f, 0.25f);
            yield return d.WaitUntil(() => d.Motor.IsGrounded, 1.5f, "the crystal landing on the upper deck");
            d.Mark("land");
            yield return d.Wait(1.0f);
        }

        // ------------------------------------------------------------------ L04_press (cause and effect)

        static IEnumerator L04PressSetup(TrailerDirector d)
        {
            d.Halt();
            // Liquid on the dry platform south of the pool (1.6 m from the water), frozen before it has fully swelled so
            // the crystal fits the 1.5 m alcove, then set into the pool, where it floats.
            yield return d.Teleport(L04PoolShore, false);
            yield return d.Wait(0.3f);
            yield return L04FreezeQuiet(d);
            yield return d.Wait(0.3f);
            L04PlaceCrystal(d, L04PoolStart);
            yield return d.Wait(1.0f);
            yield return d.WaitUntil(() => d.Motor.IsSwimming, 1.5f, "the crystal to float in the pool");
            yield return d.Wait(0.5f);
            d.Halt();
            // Just east of the pool, over the void, looking up the pool into the open end of the alcove.
            d.Cam.Dolly(new Vector3(17.6f, 2.3f, 6.0f), new Vector3(17.3f, 2.0f, 6.6f), new Vector3(15.5f, 0.45f, 10.4f), 3f, 40f, 38f);
            d.Cam.Sway(0.35f);
        }

        static IEnumerator L04PressAction(TrailerDirector d)
        {
            PressureButton button = L04Find<PressureButton>(d, "Button", L04LiftButtonPos);
            ButtonMover lift = L04Find<ButtonMover>(d, "Lift", L04LiftRest);
            Pushable crate = L04Find<Pushable>(d, "Crate", L04CratePos);
            d.Mark("paddle");
            bool shoved = false;
            for (float t = 0f; t < 4.5f && !(button != null && button.IsPressed); t += Time.deltaTime)
            {
                // Straight up the pool, kept on the alcove's centre line (x 15.5).
                float dx = Mathf.Clamp((L04PoolStart.x - d.Blob.position.x) * 1.5f, -0.35f, 0.35f);
                d.Move(new Vector2(dx, 1f).normalized);
                if (!shoved && crate != null && crate.IsFree)
                {
                    shoved = true;
                    d.Mark("shove");
                }
                yield return null;
            }
            if (button != null && !button.IsPressed)
            {
                Debug.LogWarning("[Trailer] L04_press: the crate didn't reach the button; pressing it by reflection");
                TrailerDirector.SetMember(button, "requiredMass", 0f);
                yield return d.WaitUntil(() => button.IsPressed, 0.3f, "the lift button (reflection)");
            }
            d.Mark("pressed");
            yield return d.Wait(0.15f);
            d.Halt();
            // Pull back and up to the south-east corner: the alcove on the right, the lift on the left rising to the
            // upper floor, all in frame.
            L04Blend(d, new Vector3(18.4f, 5.4f, 0.4f), () => new Vector3(11.0f, 3.6f, 7.4f), 1.8f, 46f);
            yield return d.WaitUntil(() => lift == null || lift.Progress > 0.02f, 1f, "the lift to set off");
            d.Mark("lift rises");
            yield return d.WaitUntil(() => lift == null || lift.Progress > 0.995f, 4.5f, "the lift at the top");
            d.Mark("lift top");
            yield return d.Wait(0.5f);
        }

        // ------------------------------------------------------------------ L04_deliver (hero ending)

        static IEnumerator L04DeliverSetup(TrailerDirector d)
        {
            d.Halt();
            if (d.Level != null) d.Level.HoldOnComplete = true;
            yield return d.Teleport(L04GoalStart, true, Vector3.left);
            yield return d.Wait(1.2f);
            yield return d.WaitUntil(() => d.Ship != null && d.Ship.IsFloating, 1.5f, "the ship to float on the jelly");
            d.FaceBlob(Vector3.left);
            // The framing below only ever aims at points west of the goal area's south wall (x < -3), so nothing solid
            // is between the camera and its aim; keep a pier rail from yanking the camera forward.
            d.Cam.avoidWalls = false;
            L04DeliverCamera(d);
            d.Cam.Sway(0.3f);
        }

        /// <summary>
        /// Tracks along the south side of the pier, over the void: aims at the pier mouth until the ship comes out from
        /// behind the wall, then follows it (smoothed) to the pad while trucking west.
        /// </summary>
        static void L04DeliverCamera(TrailerDirector d)
        {
            const float mouthX = -3.4f, padX = -8.9f;
            float ax = mouthX, av = 0f, lastT = 0f;
            d.Cam.Custom(t =>
            {
                float dt = t - lastT;
                lastT = t;
                Transform subject = d.Ship != null ? d.Ship.transform : d.Blob;
                float wanted = Mathf.Clamp(subject.position.x, padX, mouthX);
                if (dt > 0f) ax = Mathf.SmoothDamp(ax, wanted, ref av, 0.45f, Mathf.Infinity, dt);
                float s = Mathf.InverseLerp(mouthX, padX, ax);
                Vector3 position = new Vector3(Mathf.Lerp(-4.3f, -6.7f, s), Mathf.Lerp(9.4f, 9.5f, s), Mathf.Lerp(5.0f, 4.7f, s));
                Vector3 look = new Vector3(ax, Mathf.Lerp(8.35f, 8.3f, s), L04DockPos.z);
                return (position, look, Mathf.Lerp(36f, 34f, s));
            });
        }

        static IEnumerator L04DeliverAction(TrailerDirector d)
        {
            ShipDock dock = L04Find<ShipDock>(d, "Dock", L04DockPos);
            Vector3 pad = dock != null ? dock.transform.position : L04DockPos;
            Vector3 target = new Vector3(pad.x + 0.2f, d.Blob.position.y, pad.z);
            d.Mark("set off");
            bool pier = false, arch = false, harbour = false;
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                Vector3 to = target - d.Blob.position;
                to.y = 0f;
                if (!pier && d.Blob.position.x < -3.1f)
                {
                    pier = true;
                    d.Mark("onto the pier");
                }
                // The harbour's entrance arch stands at x -5.5 with a 4.4 m tower on each side of the pier; from this
                // south-side track the near tower wipes across the ship for about half a second.
                if (!arch && d.Blob.position.x < -4.9f)
                {
                    arch = true;
                    d.Mark("under the arch (tower wipes across)");
                }
                if (!harbour && d.Blob.position.x < -6.5f)
                {
                    harbour = true;
                    d.Mark("into the harbour");
                }
                if (to.magnitude < 0.3f) break;
                // 3 m/s along the pier (ship-safe), easing off over the last metre or so onto the pad.
                d.Move(d.StickFor(to) * Mathf.Clamp(0.6f * Mathf.Clamp01(to.magnitude / 1.6f + 0.2f), 0.15f, 0.6f));
                yield return null;
            }
            d.Halt();
            d.Mark("stop on the pad");
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered");
            yield return d.Wait(0.4f);
            // Rise and tilt up to the flag pole behind the pad while it is hoisted.
            L04Blend(d, new Vector3(-6.9f, 9.9f, 5.3f), () => new Vector3(-9.7f, 9.3f, 12.6f), 2.6f, 36f);
            yield return d.Wait(3.2f);
            d.Mark("flag up");
            yield return d.Wait(0.6f);
        }
    }
}
#endif
