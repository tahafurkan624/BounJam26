#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level07, the final level. Layout at runtime (world, floor tops at y 0 unless noted; there is NO water in this
    // level, the camera clears to a flat light blue, so anything that falls off a floor drops into the void):
    //   Start platform x -3..3, z -4.69..1.31 (blob spawn (0,0.3,-3.19), Pocket Magnet (0,0.15,-0.69), Button Magnet On
    //   (0,0,0.81)); void gap; seed bridge x -2.5..2.5, z 4.5..5.5 (Seed (2,0.12,5)); void; magnet box x -1..1,
    //   z 6.5..9 fenced by four grates, Magnet (-0.01,0.7,8.23) facing -Z (range 7, so the crystal must be at z >= 1.24);
    //   lower tropical floor x -9.63..-5.63, z 4.5..11.5 with the SoilBed at (-7.51,0.01,5.77); upper tile (y 2.24)
    //   x -12..-8.5, z 4.5..11.5; ship tiles x -1..1, z 15.5..16.5 (PaperShip parked at (0.5,0.02,16), bow +X) and
    //   x 1..3, z 15..17; pier east from x ~3 to the harbour pad, Dock pivot (9.36,0,15.99), flag at (11.23,0,12.47).
    // NOTE: Temp/Trailer/maps/Level07_*.png also show the MainMenu scene (it was open when they were rendered): the big
    // checker floor x -5..5 below z 8.5, the long yellow block at x 0, the second harbour at (11,4.5) and the sea are
    // MainMenu, not Level07.
    public static partial class TrailerShots
    {
        static void ShotsLevel07(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L07_magnet",
                scene = "Level07",
                note = "the jelly rolls over a pocket magnet (orange glow), freezes around it (blue), slides onto the switch; " +
                       "the electromagnet lights up and yanks the crystal across the gap along its beam",
                setup = d => L07MagnetSetup(d),
                action = d => L07MagnetAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L07_bloom",
                scene = "Level07",
                note = "the blob swallows a seed on the soil bed, freezes and melts; the seed drops in, a flower lift sprouts " +
                       "and carries the blob 3 m up while its petals unfold toward the upper tile (crane up)",
                setup = d => L07BloomSetup(d, false),
                action = d => L07BloomAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L07_bloom_v",
                scene = "Level07",
                portrait = true,
                note = "portrait: seed into the soil, the flower lift sprouts and rides the blob straight up the frame",
                setup = d => L07BloomSetup(d, true),
                action = d => L07BloomAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L07_pickup",
                scene = "Level07",
                note = "the blob dashes under the parked paper ship, which rises onto the jelly in a bow spray (slow motion), " +
                       "then sets off for the harbour",
                setup = d => L07PickupSetup(d),
                action = d => L07PickupAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L07_finale_play",
                scene = "Level07",
                hud = true,
                settle = 3.6f,
                note = "gameplay camera with HUD: the blob sails the ship down the pier onto the harbour pad, it turns green " +
                       "and the 'All ships delivered! Thanks for playing' card pours in",
                setup = d => L07FinaleSetup(d),
                action = d => L07FinaleAction(d),
                tail = 1.0f,
            });
            shots.Add(new Shot
            {
                id = "L07_hero_harbor",
                scene = "Level07",
                note = "hero: high reveal of the whole final level sweeping down to the harbour as the blob glides the ship " +
                       "onto the pad; the pad turns green and the flag is hoisted",
                setup = d => L07HeroSetup(d),
                action = d => L07HeroAction(d),
                tail = 0.8f,
            });
        }

        // ---------------------------------------------------------------- Shared

        static readonly Vector3 L07Iron = new Vector3(0f, 0.15f, -0.69f);
        static readonly Vector3 L07Bed = new Vector3(-7.51f, 0.01f, 5.77f);
        static readonly Vector3 L07Pad = new Vector3(9.36f, 0.3f, 15.99f);

        static float L07Ease(float k) => k * k * (3f - 2f * k);

        /// <summary>
        /// Eases the camera from where it is now to a new framing over <paramref name="glide"/> s, then drifts on by
        /// <paramref name="drift"/> over the next <paramref name="driftTime"/> s: a change of framing mid-action, no cut.
        /// </summary>
        static void L07Glide(TrailerDirector d, Vector3 toPos, System.Func<Vector3> toLook, float toFov, float glide,
                             Vector3 drift, float driftTime)
        {
            Vector3 fromPos = d.Cam.Camera.transform.position;
            Vector3 fromLook = d.Cam.Look;
            float fromFov = d.Cam.Camera.fieldOfView;
            d.Cam.Custom(t =>
            {
                float k = L07Ease(Mathf.Clamp01(t / glide));
                float m = L07Ease(Mathf.Clamp01((t - glide) / driftTime));
                Vector3 position = Vector3.Lerp(fromPos, toPos, k) + drift * m;
                Vector3 look = Vector3.Lerp(fromLook, toLook(), k);
                return (position, look, Mathf.Lerp(fromFov, toFov, k));
            });
        }

        /// <summary>Turns the blob's eyes toward the lens (it has to stand still).</summary>
        static void L07FaceCamera(TrailerDirector d)
        {
            if (d.Cam != null && d.Cam.Camera != null) d.FaceBlob(d.Cam.Camera.transform.position - d.Blob.position);
        }

        /// <summary>
        /// Drives the blob (with the ship riding) straight to the harbour pad, gliding in slowly over the last ~1.8 m so
        /// the ship is under 1.5 m/s inside the zone, and waits for the delivery.
        /// </summary>
        static IEnumerator L07Sail(TrailerDirector d, ShipDock dock, float speed)
        {
            bool onPier = false, onPad = false;
            for (float time = 0f; time < 8f; time += Time.deltaTime)
            {
                Vector3 to = L07Pad - d.Blob.position;
                to.y = 0f;
                if (!onPier && d.Blob.position.x > 3.2f)
                {
                    onPier = true;
                    d.Mark("onto the pier");
                }
                if (!onPad && d.Blob.position.x > 6.4f)
                {
                    onPad = true;
                    d.Mark("onto the harbour");
                }
                if (to.magnitude < 0.3f || (dock != null && dock.IsDelivered)) break;
                float s = Mathf.Clamp(speed * Mathf.Clamp01(to.magnitude / 2.2f + 0.2f), 0.12f, 1f);
                d.Move(d.StickFor(to) * s);
                yield return null;
            }
            d.Halt();
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
        }

        // ---------------------------------------------------------------- L07_magnet

        static IEnumerator L07MagnetSetup(TrailerDirector d)
        {
            // 1.25 m short of the pocket magnet, so the take opens on the jelly rolling over it.
            yield return d.Teleport(new Vector3(0f, 0.3f, -1.95f), false);
            yield return d.Wait(1.0f);
            d.FaceBlob(Vector3.forward);
            Vector3 at = L07Iron + Vector3.up * 0.15f;
            // Low 3/4 close-up from in front and to the right (above the start floor), slowly pushing in.
            d.Cam.Dolly(at + new Vector3(1.9f, 0.55f, 1.5f), at + new Vector3(1.45f, 0.4f, 1.1f),
                        () => Vector3.Lerp(at, d.Blob.position, 0.45f) + Vector3.up * 0.08f, 3.6f, 36f, 33f);
            d.Cam.Sway(0.35f);
        }

        static IEnumerator L07MagnetAction(TrailerDirector d)
        {
            Collectible iron = d.Get<Collectible>("Pickups/Pocket Magnet");
            PressureButton button = d.Get<PressureButton>("Machines/Button Magnet On");
            Magnet magnet = d.Get<Magnet>("Machines/Magnet");
            BlobCargo cargo = d.Blob.GetComponent<BlobCargo>();
            BlobMagnetism pull = d.Blob.GetComponent<BlobMagnetism>();
            Vector3 spot = new Vector3(L07Iron.x, 0f, L07Iron.z);

            d.Mark("roll onto the pocket magnet");
            // A second try only if the first freeze missed the item.
            for (int attempt = 0; attempt == 0 || (attempt < 2 && cargo != null && !cargo.Has<IronBall>()); attempt++)
            {
                if (attempt > 0)
                {
                    d.Melt();
                    yield return d.Wait(0.5f);
                }
                yield return d.Steer(spot, 0.45f, attempt == 0 ? 0.12f : 0.05f, 3f);
                L07FaceCamera(d);
                yield return d.WaitUntil(() => iron == null || iron.IsEngulfed, 2.5f, "the pocket magnet inside the jelly");
                if (attempt == 0) d.Mark("orange glow");
                yield return d.Wait(0.75f);
                yield return d.Freeze();
                yield return new WaitForFixedUpdate();
            }
            // Blue glow in the crystal.
            yield return d.Wait(0.55f);

            // Crane out to a side view of the pull line: switch, gap, seed bridge, grate box and magnet.
            L07Glide(d, new Vector3(5.0f, 2.7f, 2.2f), () => Vector3.Lerp(new Vector3(0f, 0.55f, 4.3f), d.Blob.position, 0.25f),
                     42f, 1.9f, new Vector3(-0.25f, -0.1f, 0.7f), 4f);
            d.Mark("push");
            d.Move(new Vector2(0f, 1f));
            yield return d.WaitUntil(() => (button != null && button.IsPressed) || (magnet != null && magnet.IsActive), 2.5f,
                                     "the magnet switch");
            d.Mark("magnet on");
            yield return d.WaitUntil(() => pull == null || pull.Current != null, 3f, "the pull");
            d.Halt();
            d.Mark("yank");
            float yanked = Time.time;
            yield return d.WaitUntil(() => Time.time - yanked > 0.35f && d.Body.linearVelocity.magnitude < 0.4f, 3f,
                                     "the crystal to reach the magnet");
            d.Mark("held at the magnet");
            bool magnetOn = magnet != null && magnet.IsActive;
            for (float time = 0f; time < 1.7f; time += Time.deltaTime)
            {
                if (magnetOn && magnet != null && !magnet.IsActive)
                {
                    magnetOn = false;
                    d.Mark("magnet off, crystal drops");
                }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- L07_bloom, L07_bloom_v

        /// <summary>Height of the soil bed's surface at its centre (raycast), so the seed can be set right on it.</summary>
        static float L07SoilTop()
        {
            Vector3 from = new Vector3(L07Bed.x, 4f, L07Bed.z);
            int mask = LayerMask.GetMask("Default", "Environment");
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, mask, QueryTriggerInteraction.Ignore) &&
                hit.point.y > -0.5f && hit.point.y < 1f)
                return hit.point.y;
            return L07Bed.y + 0.08f;
        }

        static string L07Stage(FlowerLift flower)
        {
            if (flower == null) return "";
            object stage = TrailerDirector.GetMember(flower, "stage");
            return stage != null ? stage.ToString() : "";
        }

        static bool L07Lifting(FlowerLift flower)
        {
            string stage = L07Stage(flower);
            return stage == "Rising" || stage == "Top";
        }

        /// <summary>Last resort: starts the leaf rising without waiting for a rider (private FlowerLift.Enter).</summary>
        static void L07ForceRise(FlowerLift flower)
        {
            if (flower == null) return;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Type stageType = typeof(FlowerLift).GetNestedType("Stage", System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo enter = typeof(FlowerLift).GetMethod("Enter", flags);
            if (stageType == null || enter == null) return;
            enter.Invoke(flower, new object[] { System.Enum.Parse(stageType, "Rising") });
            Debug.LogWarning("[Trailer] L07: forced the flower lift to rise");
        }

        static IEnumerator L07BloomSetup(TrailerDirector d, bool portrait)
        {
            float top = L07SoilTop();
            // The seed waits on the middle of the bed; the blob comes in from 1.3 m east of it.
            Collectible seed = d.Get<Collectible>("Pickups/Seed");
            if (seed != null) TrailerDirector.Place(seed.transform, new Vector3(L07Bed.x, top + 0.12f, L07Bed.z));
            yield return d.Teleport(new Vector3(L07Bed.x + 1.3f, 0.3f, L07Bed.z), false);
            yield return d.Wait(1.0f);
            d.FaceBlob(Vector3.left);
            L07BloomCamera(d, top, portrait);
        }

        /// <summary>
        /// A crane driven by the blob's height: low and close on the bed while the seed goes in, rising with the leaf to a
        /// high three-quarter view of the open flower and the upper tile behind it. The camera sits east of the bed, in
        /// open air, so wall avoidance is off: a growing bud between the lens and the aim point must not yank it.
        /// </summary>
        static void L07BloomCamera(TrailerDirector d, float top, bool portrait)
        {
            Vector3 bed = new Vector3(L07Bed.x, top, L07Bed.z);
            // The swollen blob is ~1.4 m wide and the open petals ~3 m across: keep the lens 3-5 m off, and end high
            // enough (~30 deg down) to see the blob sitting inside the flower rather than behind its front petals.
            Vector3 lowPos = bed + (portrait ? new Vector3(3.0f, 1.35f, -2.35f) : new Vector3(3.2f, 0.95f, -2.5f));
            Vector3 highPos = bed + (portrait ? new Vector3(3.3f, 5.5f, -2.7f) : new Vector3(3.1f, 5.3f, -2.7f));
            float lowFov = portrait ? 56f : 38f;
            float highFov = portrait ? 58f : 46f;
            Vector3 push = portrait ? new Vector3(-0.025f, 0f, 0.02f) : new Vector3(-0.04f, 0f, 0.03f);
            float lift = 0f;
            Vector3 seen = d.Blob.position + Vector3.up * 0.2f;
            d.Cam.avoidWalls = false;
            d.Cam.Custom(t =>
            {
                float dt = Time.deltaTime;
                float rise = Mathf.Clamp01((d.Blob.position.y - (top + 0.5f)) / 2.4f);
                lift = Mathf.Lerp(lift, rise, 1f - Mathf.Exp(-3f * dt));
                float k = L07Ease(lift);
                Vector3 aim = d.Blob.position + Vector3.up * Mathf.Lerp(0.2f, 0.1f, k);
                seen = Vector3.Lerp(seen, aim, 1f - Mathf.Exp(-6f * dt));
                Vector3 position = Vector3.Lerp(lowPos, highPos, k) + push * Mathf.Min(t, 14f);
                return (position, seen, Mathf.Lerp(lowFov, highFov, k));
            });
            d.Cam.Sway(0.3f);
        }

        static bool L07OnLeaf(TrailerDirector d, Vector3 center, float top)
        {
            Vector3 off = d.Body.position - center;
            off.y = 0f;
            return off.magnitude < 0.45f && d.Body.position.y > top;
        }

        /// <summary>Gets the blob onto the bud's leaf if the sprout left it beside it: flow on, or hop on.</summary>
        static IEnumerator L07Board(TrailerDirector d)
        {
            Collider leaf = d.Get<Collider>("Leaf Body");
            if (leaf == null) yield break;
            Vector3 center = leaf.bounds.center;
            float top = leaf.bounds.max.y;
            if (L07OnLeaf(d, center, top)) yield break;
            yield return d.Steer(center, 0.35f, 0.12f, 2f);
            if (L07OnLeaf(d, center, leaf.bounds.max.y)) yield break;
            d.Move(d.StickFor(center - d.Blob.position) * 0.3f);
            d.Jump();
            d.Mark("hop onto the leaf");
            yield return d.Wait(0.45f);
            yield return d.Steer(center, 0.3f, 0.1f, 1.5f);
        }

        static IEnumerator L07BloomAction(TrailerDirector d)
        {
            Collectible seed = d.Get<Collectible>("Pickups/Seed");
            Soil soil = d.Get<Soil>("Pickups/SoilBed");
            FlowerLift flower = d.Get<FlowerLift>("Pickups/SoilBed");
            BlobCargo cargo = d.Blob.GetComponent<BlobCargo>();
            Vector3 bed = new Vector3(L07Bed.x, 0f, L07Bed.z);

            d.Mark("onto the soil");
            yield return d.Steer(bed, 0.4f, 0.1f, 3f);
            L07FaceCamera(d);
            yield return d.WaitUntil(() => seed == null || seed.IsEngulfed, 2.5f, "the seed inside the jelly");
            d.Mark("seed glows");
            yield return d.Wait(0.6f);
            yield return d.Freeze();
            yield return d.WaitUntil(() => cargo == null || cargo.Has<Seed>(), 0.4f, "the seed caught");
            yield return d.Wait(0.7f);
            d.Melt();
            L07FaceCamera(d);
            yield return d.WaitUntil(() => soil == null || soil.IsPlanted, 1.5f, "the seed planted");
            // If the seed missed (not caught, or landed off the soil), plant by hand and hide it.
            if (soil != null && !soil.IsPlanted && soil.TryPlant() && seed != null) seed.gameObject.SetActive(false);
            d.Mark("sprout");
            yield return d.WaitUntil(() => L07Stage(flower) != "Sprouting", 6f, "the bud");
            d.Mark("bud ready");
            yield return L07Board(d);
            L07FaceCamera(d);
            // The leaf rises after 0.6 s with a rider on it.
            yield return d.WaitUntil(() => L07Lifting(flower), 2.5f, "the leaf to rise");
            if (!L07Lifting(flower)) L07ForceRise(flower);
            d.Mark("lift off");
            yield return d.WaitUntil(() => L07Stage(flower) == "Top", 8f, "the top");
            d.Mark("petals open");
            L07FaceCamera(d);
            yield return d.Wait(1.6f);
        }

        // ---------------------------------------------------------------- L07_pickup

        static IEnumerator L07PickupSetup(TrailerDirector d)
        {
            // The ship stays parked where the level puts it, (0.5,0.02,16) bow +X; the blob waits 2.2 m east of it.
            yield return d.Teleport(new Vector3(2.7f, 0.3f, 16f), false);
            yield return d.Wait(1.0f);
            d.FaceBlob(Vector3.left);
            L07PickupCamera(d);
        }

        /// <summary>
        /// Side-on, near floor level, from the open gap south of the ship tiles (z 13.5): the blob enters from screen
        /// right; the camera then slides along x with the ship so the set-off stays in frame.
        /// </summary>
        static void L07PickupCamera(TrailerDirector d)
        {
            float shipX = d.Ship.transform.position.x;
            float shipY = d.Ship.transform.position.y;
            d.Cam.Custom(t =>
            {
                float follow = 1f - Mathf.Exp(-4f * Time.deltaTime);
                shipX = Mathf.Lerp(shipX, d.Ship.transform.position.x, follow);
                shipY = Mathf.Lerp(shipY, d.Ship.transform.position.y, follow);
                var position = new Vector3(1.0f + 0.6f * shipX, 0.62f, 13.45f + 0.04f * Mathf.Min(t, 6f));
                var look = new Vector3(0.8f + 0.6f * shipX, 0.3f + 0.35f * shipY, 16f);
                return (position, look, 40f);
            });
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L07PickupAction(TrailerDirector d)
        {
            d.Mark("dash");
            d.Move(new Vector2(-0.75f, 0f));
            yield return d.WaitUntil(() => d.Blob.position.x < 1.75f, 1.5f, "the approach");
            yield return d.SlowTo(0.35f, 0.1f);
            d.Mark("scoop");
            yield return d.WaitUntil(() => d.Blob.position.x < 1.0f, 1.2f, "under the hull");
            d.Halt();
            yield return d.WaitUntil(() => d.Ship.IsFloating, 0.8f, "the ship afloat");
            d.Mark("ship aboard");
            yield return d.WaitClip(0.9f);
            yield return d.SlowTo(1f, 0.3f);
            yield return d.Wait(0.5f);
            d.Mark("set off");
            yield return d.MoveFor(new Vector2(0.4f, 0f), 0.9f);
            yield return d.Wait(0.5f);
        }

        // ---------------------------------------------------------------- L07_finale_play

        static IEnumerator L07FinaleSetup(TrailerDirector d)
        {
            d.Cam.Gameplay();
            yield return d.Teleport(new Vector3(2.0f, 0.3f, 16f), true, Vector3.right);
            // Ship settles on the jelly; the follow cam catches up after the jump.
            yield return d.Wait(2.0f);
            d.FaceBlob(Vector3.right);
        }

        static IEnumerator L07FinaleAction(TrailerDirector d)
        {
            ShipDock dock = Object.FindAnyObjectByType<ShipDock>();
            d.Mark("set sail");
            yield return L07Sail(d, dock, 0.45f);
            d.Mark("delivered");
            // Pad turns green, success sting, the glass card pours in.
            yield return d.Wait(4.2f);
        }

        // ---------------------------------------------------------------- L07_hero_harbor

        static IEnumerator L07HeroSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(1.7f, 0.3f, 16f), true, Vector3.right);
            yield return d.Wait(1.4f);
            d.FaceBlob(Vector3.right);
            L07HeroCamera(d);
        }

        /// <summary>
        /// From high over the south-west corner (the whole level in frame) an arcing swoop down to a low south-west
        /// three-quarter view of the harbour: pier on the left, pad in the middle, flag pole on the right (front-lit).
        /// </summary>
        static void L07HeroCamera(TrailerDirector d)
        {
            // Opening frame: the route from the start platform (bottom) to the harbour and lighthouse (top), aim points in
            // open air so wall avoidance never trips.
            Vector3 highPos = new Vector3(-7.5f, 15.5f, -5.5f), highLook = new Vector3(2f, 0.6f, 8f);
            Vector3 lowPos = new Vector3(4.6f, 3f, 7.4f), lowLook = new Vector3(9.6f, 1.4f, 15f);
            Vector3 bulge = new Vector3(2.2f, 0f, -2.2f);
            const float swoop = 5.4f;
            d.Cam.Custom(t =>
            {
                float k = L07Ease(Mathf.Clamp01(t / swoop));
                float h = L07Ease(Mathf.Clamp01((t - swoop) / 5f));
                Vector3 position = Vector3.Lerp(highPos, lowPos, k) + bulge * Mathf.Sin(k * Mathf.PI)
                                   + new Vector3(0.35f, -0.2f, 0.5f) * h;
                return (position, Vector3.Lerp(highLook, lowLook, k), Mathf.Lerp(50f, 44f, k));
            });
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L07HeroAction(TrailerDirector d)
        {
            ShipDock dock = Object.FindAnyObjectByType<ShipDock>();
            d.Mark("level reveal");
            yield return d.Wait(0.7f);
            d.Mark("set sail");
            yield return L07Sail(d, dock, 0.45f);
            d.Mark("delivered");
            // Flag hoist and wave.
            yield return d.Wait(3.2f);
        }
    }
}
#endif
