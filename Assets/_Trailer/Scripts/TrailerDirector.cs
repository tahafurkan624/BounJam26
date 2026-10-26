#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using FMODUnity;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.Trailer
{
    /// <summary>One filmed clip: the scene it plays in, what is set up before the take and what happens during it.</summary>
    public sealed class Shot
    {
        public string id;
        public string scene;
        public string note;
        /// <summary>Keep the game's HUD and cards on screen (off: clean frame).</summary>
        public bool hud;
        /// <summary>Keep the music in the clip's sound (off: sound effects only; the music is its own stem).</summary>
        public bool music;
        /// <summary>Seconds the scene runs after loading before setup, so the level title and any intro settle.</summary>
        public float settle = 1.2f;
        /// <summary>Seconds recorded before the action, as a handle for the edit.</summary>
        public float leadIn = 0.4f;
        /// <summary>Seconds recorded after the action.</summary>
        public float tail = 0.6f;
        /// <summary>Record portrait (1080x1920) instead of 1920x1080.</summary>
        public bool portrait;
        /// <summary>Record only the sound (the music stem).</summary>
        public bool audioOnly;
        public Func<TrailerDirector, IEnumerator> setup;
        public Func<TrailerDirector, IEnumerator> action;
    }

    /// <summary>
    /// Films trailer shots in Play mode (editor only). For each <see cref="Shot"/> it loads the scene, clears the frame
    /// (UI, music), runs the shot's setup, has the editor start recording (<see cref="StartRecording"/>, set by
    /// TrailerSession), plays the action and stops. Shots drive the blob through <see cref="PlayerInputReader.MoveOverride"/>
    /// and the camera through <see cref="TrailerCamera"/>, and mark moments (<see cref="Mark"/>) at their time in the clip.
    /// </summary>
    public class TrailerDirector : MonoBehaviour
    {
        /// <summary>Set by the editor: starts a take named after the shot.</summary>
        public static Action<Shot> StartRecording;
        /// <summary>Set by the editor: stops the take; gets the moments marked during it.</summary>
        public static Action<Shot, string> StopRecording;
        /// <summary>Set by the editor: the frame rate takes are recorded at.</summary>
        public static float FrameRate = 60f;

        public static TrailerDirector Current { get; private set; }
        public static bool Busy { get; private set; }
        public static string LastError { get; private set; }

        readonly StringBuilder marks = new StringBuilder();
        int recordStartFrame = -1;

        public TrailerCamera Cam { get; private set; }
        public Shot Shot { get; private set; }
        public LevelManager Level { get; private set; }
        public Transform Blob { get; private set; }
        public ShipBuoyancy Ship { get; private set; }
        public bool Recording => recordStartFrame >= 0;
        /// <summary>Seconds into the clip (frames recorded / frame rate).</summary>
        public float ClipTime => Recording ? (Time.frameCount - recordStartFrame) / FrameRate : 0f;

        public static TrailerDirector Run(IList<Shot> shots)
        {
            var go = new GameObject("TrailerDirector");
            DontDestroyOnLoad(go);
            Current = go.AddComponent<TrailerDirector>();
            Busy = true;
            LastError = null;
            Current.StartCoroutine(Current.RunAll(shots));
            return Current;
        }

        IEnumerator RunAll(IList<Shot> shots)
        {
            foreach (Shot shot in shots)
            {
                yield return Flatten(RunShot(shot), e =>
                {
                    LastError = $"{shot.id}: {e}";
                    Debug.LogError("[Trailer] " + LastError);
                    if (Recording) EndTake();
                });
                Restore();
            }
            Busy = false;
            Destroy(gameObject);
        }

        /// <summary>
        /// Steps a coroutine and the ones it yields itself, so an exception anywhere in a shot ends that shot (through
        /// <paramref name="fail"/>) instead of leaving the queue waiting on a dead coroutine.
        /// </summary>
        static IEnumerator Flatten(IEnumerator root, Action<Exception> fail)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception e)
                {
                    fail(e);
                    yield break;
                }
                if (!moved) stack.Pop();
                else if (top.Current is IEnumerator nested) stack.Push(nested);
                else yield return top.Current;
            }
        }

        IEnumerator RunShot(Shot shot)
        {
            Shot = shot;
            marks.Clear();
            Debug.Log($"[Trailer] Shot {shot.id} in {shot.scene}: {shot.note}");
            if (shot.scene == Silence) yield return LoadSilence();
            else SceneManager.LoadScene(shot.scene);
            yield return null;
            yield return null;
            Level = FindAnyObjectByType<LevelManager>();
            BlobMotor motor = FindAnyObjectByType<BlobMotor>();
            Blob = motor != null ? motor.transform : null;
            Ship = FindAnyObjectByType<ShipBuoyancy>();
            Cam = TrailerCamera.Attach();
            PrepareScene();
            if (Level != null)
                Level.Ended += (state, message) =>
                    Mark((state == LevelManager.State.Failed ? "! LEVEL FAILED: " : "level complete: ") + message.Replace("\n", " "));
            yield return Wait(shot.settle);

            ClearFrame(shot);
            if (shot.setup != null) yield return shot.setup(this);

            StartRecording?.Invoke(shot);
            // The recorder starts capturing on the frame after.
            yield return null;
            recordStartFrame = Time.frameCount;
            yield return Wait(shot.leadIn);
            if (shot.action != null) yield return shot.action(this);
            yield return Wait(shot.tail);
            EndTake();
        }

        /// <summary>A shot's scene name for an empty scene: only the music (which outlives scene loads) is heard there.</summary>
        public const string Silence = "(silence)";

        static IEnumerator LoadSilence()
        {
            Scene empty = SceneManager.CreateScene("TrailerSilence");
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.AddComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            SceneManager.MoveGameObjectToScene(cameraObject, empty);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        void EndTake()
        {
            string moments = marks.ToString();
            recordStartFrame = -1;
            StopRecording?.Invoke(Shot, moments);
        }

        /// <summary>Notes a moment of the clip (for cutting on it) at the current clip time.</summary>
        public void Mark(string what)
        {
            if (!Recording) what += " (before the take)";
            string at = Blob != null
                ? string.Format(CultureInfo.InvariantCulture, "  [blob ({0:0.00},{1:0.00},{2:0.00})]", Blob.position.x, Blob.position.y, Blob.position.z)
                : "";
            marks.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}{2}", ClipTime, what, at));
            Debug.Log($"[Trailer] {Shot?.id} @ {ClipTime:0.00}s: {what}");
        }

        // ---------------------------------------------------------------- Frame and sound

        Transform heading;

        /// <summary>
        /// Readies a freshly loaded level for filming: the blob steers in world space whatever the camera does, edges
        /// are antialiased, and a fail (ship soaked, blob fell) can't reload the scene in the middle of a take.
        /// </summary>
        void PrepareScene()
        {
            Time.timeScale = 1f;
            if (heading == null)
            {
                heading = new GameObject("TrailerHeading").transform;
                heading.SetParent(transform, false);
            }
            PinSteering();
            if (Cam != null && Cam.Camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data))
            {
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            }
            if (Level != null) SetMember(Level, "failDelay", 9999f);
        }

        /// <summary>Points every blob piece's steering frame at a fixed world-aligned heading (x = +X, y = +Z).</summary>
        public void PinSteering()
        {
            if (heading == null) return;
            foreach (BlobMotor motor in FindObjectsByType<BlobMotor>(FindObjectsSortMode.None)) SetMember(motor, "cameraTransform", heading);
        }

        void ClearFrame(Shot shot)
        {
            if (!shot.hud) SetUi(false);
            if (!shot.music) SetMusic(0f);
        }

        /// <summary>Shows or hides the glass UI (HUD, toasts, cards) by its root canvases.</summary>
        public static void SetUi(bool visible)
        {
            foreach (GlassUI ui in FindObjectsByType<GlassUI>(FindObjectsSortMode.None))
                foreach (Canvas canvas in ui.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isRootCanvas) canvas.enabled = visible;
        }

        /// <summary>
        /// Music gain for the take, without touching the player's saved volumes. Zero both mutes the music bus and
        /// silences the music event itself, in case it doesn't go through the bus.
        /// </summary>
        public static void SetMusic(float gain)
        {
            if (RuntimeManager.StudioSystem.getBus(GameAudio.MusicBus, out FMOD.Studio.Bus bus) == FMOD.RESULT.OK)
            {
                bus.setMute(gain <= 0f);
                if (gain > 0f) bus.setVolume(gain);
            }
            GameMusic.EventVolume = gain > 0f ? 1f : 0f;
        }

        /// <summary>Sound effects gain, where the banks have an effects bus.</summary>
        public static bool SetEffects(float gain) =>
            RuntimeManager.StudioSystem.getBus(GameAudio.SfxBus, out FMOD.Studio.Bus bus) == FMOD.RESULT.OK && bus.setVolume(gain) == FMOD.RESULT.OK;

        void Restore()
        {
            foreach (PlayerInputReader reader in FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None)) reader.MoveOverride = null;
            if (Cam != null) Cam.Release();
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            if (RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup master) == FMOD.RESULT.OK) master.setPitch(1f);
            if (RuntimeManager.StudioSystem.getBus(GameAudio.MusicBus, out FMOD.Studio.Bus bus) == FMOD.RESULT.OK) bus.setMute(false);
            GameMusic.EventVolume = 1f;
            GameAudio.Apply();
        }

        /// <summary>Slow motion over <paramref name="seconds"/> of clip time (eased), optionally pitching the sound down.</summary>
        public IEnumerator SlowTo(float timeScale, float seconds, float pitch = 1f)
        {
            float from = Time.timeScale;
            RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup master);
            master.getPitch(out float pitchFrom);
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * FrameRate));
            for (int i = 1; i <= frames; i++)
            {
                float k = Mathf.SmoothStep(0f, 1f, i / (float)frames);
                Time.timeScale = Mathf.Lerp(from, timeScale, k);
                master.setPitch(Mathf.Lerp(pitchFrom, pitch, k));
                yield return null;
            }
        }

        /// <summary>Waits <paramref name="seconds"/> of clip time (frames at the recording rate), whatever the time scale.</summary>
        public IEnumerator WaitClip(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds * FrameRate);
            for (int i = 0; i < frames; i++) yield return null;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Busy = false;
            foreach (FMODRuntimeManagerOnGUIHelper overlay in Resources.FindObjectsOfTypeAll<FMODRuntimeManagerOnGUIHelper>())
                overlay.enabled = true;
        }

        // FMOD's debug overlay (the platform settings turn it on) draws over the game view, which Recorder captures.
        // Its object is hidden, so only FindObjectsOfTypeAll sees it.
        void LateUpdate()
        {
            foreach (FMODRuntimeManagerOnGUIHelper overlay in Resources.FindObjectsOfTypeAll<FMODRuntimeManagerOnGUIHelper>())
                overlay.enabled = false;
        }

        // ---------------------------------------------------------------- Waiting

        public IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        public IEnumerator WaitUntil(Func<bool> done, float timeout, string what = null)
        {
            for (float t = 0f; t < timeout && !done(); t += Time.deltaTime) yield return null;
            if (!done() && what != null)
            {
                Debug.LogWarning($"[Trailer] {Shot.id}: timed out waiting for {what}");
                Mark("! TIMEOUT waiting for " + what);
            }
        }

        // ---------------------------------------------------------------- The blob

        IEnumerable<PlayerInputReader> Readers => FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None);

        /// <summary>Holds the stick at <paramref name="stick"/> (x right, y forward) on every piece of the blob.</summary>
        public void Move(Vector2 stick)
        {
            foreach (PlayerInputReader reader in Readers) reader.MoveOverride = stick;
        }

        public void Halt() => Move(Vector2.zero);

        public IEnumerator MoveFor(Vector2 stick, float seconds)
        {
            Move(stick);
            yield return Wait(seconds);
            Halt();
        }

        /// <summary>The stick for a world direction: steering is pinned to world axes (<see cref="PinSteering"/>).</summary>
        public Vector2 StickFor(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            worldDirection.Normalize();
            return new Vector2(worldDirection.x, worldDirection.z);
        }

        /// <summary>Steers the blob to a point on the ground, slowing as it gets close.</summary>
        public IEnumerator Steer(Vector3 target, float speed = 1f, float arrive = 0.35f, float timeout = 8f)
        {
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                Vector3 to = target - Blob.position;
                to.y = 0f;
                if (to.magnitude < arrive) break;
                Move(StickFor(to) * Mathf.Clamp(speed * Mathf.Clamp01(to.magnitude / 1.2f + 0.35f), 0.2f, 1f));
                yield return null;
            }
            Halt();
        }

        /// <summary>Steers through points in turn without stopping between them.</summary>
        public IEnumerator Path(float speed, params Vector3[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                bool last = i == points.Length - 1;
                for (float t = 0f; t < 10f; t += Time.deltaTime)
                {
                    Vector3 to = points[i] - Blob.position;
                    to.y = 0f;
                    if (to.magnitude < (last ? 0.35f : 0.6f)) break;
                    float s = last ? Mathf.Clamp(speed * Mathf.Clamp01(to.magnitude / 1.2f + 0.35f), 0.2f, 1f) : speed;
                    Move(StickFor(to) * s);
                    yield return null;
                }
            }
            Halt();
        }

        public BlobMotor Motor => Blob != null ? Blob.GetComponent<BlobMotor>() : null;
        public BlobForm Form => Blob != null ? Blob.GetComponent<BlobForm>() : null;
        public Rigidbody Body => Blob != null ? Blob.GetComponent<Rigidbody>() : null;

        /// <summary>Every blob piece (one, or two halves after a piercer pipe).</summary>
        public IReadOnlyList<BlobSplit> Pieces => BlobSplit.All;

        /// <summary>Jumps every piece (liquid, grounded).</summary>
        public void Jump()
        {
            if (BlobSplit.All.Count == 0) Motor?.RequestJump();
            foreach (BlobSplit piece in BlobSplit.All) piece.Motor.RequestJump();
        }

        /// <summary>Freezes the blob into a crystal, retrying until the skin allows it (as MenuBlobStunt does).</summary>
        public IEnumerator Freeze(BlobForm form = null)
        {
            form = form != null ? form : Form;
            for (int i = 0; i < 20 && !form.IsSolid; i++)
            {
                form.Freeze();
                if (form.IsSolid) break;
                yield return new WaitForFixedUpdate();
            }
            if (form.IsSolid) Mark("freeze");
            else
            {
                Debug.LogWarning($"[Trailer] {Shot.id}: the blob wouldn't freeze");
                Mark("! FREEZE FAILED");
            }
        }

        public void Melt(BlobForm form = null)
        {
            form = form != null ? form : Form;
            form.Melt();
            Mark("melt");
        }

        /// <summary>
        /// Sends a crystal rolling along <paramref name="direction"/> with no brake, like the menu stunt: the motor is
        /// off until <see cref="EndRoll"/>.
        /// </summary>
        public void StartRoll(Vector3 direction, float speed = 1.2f, float spin = 5f)
        {
            Motor.enabled = false;
            Rigidbody body = Body;
            body.angularDamping = 0.5f;
            direction.y = 0f;
            direction.Normalize();
            body.linearVelocity += direction * speed;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * spin;
        }

        public void EndRoll() => Motor.enabled = true;

        /// <summary>
        /// Moves the blob to <paramref name="position"/> the safe way (MenuBlobStunt.ResetToStart: hidden, jelly re-seeded,
        /// floor memory cleared), and the ship with it, placed above the jelly so it settles on top.
        /// </summary>
        public IEnumerator Teleport(Vector3 position, bool withShip = true, Vector3? shipFacing = null)
        {
            BlobSplit split = Blob.GetComponent<BlobSplit>();
            BlobSkin skin = Blob.GetComponent<BlobSkin>();
            GroundGuard guard = Blob.GetComponent<GroundGuard>();
            if (split != null) split.SetHidden(true);
            if (Form.IsSolid) Form.Melt();
            Motor.enabled = true;
            if (skin != null) skin.enabled = false;
            if (guard != null) guard.enabled = false;
            Place(Blob, position);
            if (skin != null) skin.enabled = true;
            if (guard != null) guard.enabled = true;
            if (withShip && Ship != null) PlaceShip(position + Vector3.up * 0.75f, shipFacing);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            if (split != null) split.SetHidden(false);
        }

        /// <summary>Puts the ship at <paramref name="position"/>, stopped, facing <paramref name="facing"/> (its bow is +Z).</summary>
        public void PlaceShip(Vector3 position, Vector3? facing = null)
        {
            if (Ship == null) return;
            GroundGuard guard = Ship.GetComponent<GroundGuard>();
            if (guard != null) guard.enabled = false;
            Quaternion? rotation = null;
            if (facing.HasValue)
            {
                Vector3 f = facing.Value;
                f.y = 0f;
                rotation = Quaternion.LookRotation(f.normalized);
            }
            Place(Ship.transform, position, rotation ?? Ship.transform.rotation);
            if (guard != null) guard.enabled = true;
        }

        /// <summary>Turns the ship riding on the blob to face <paramref name="facing"/>.</summary>
        public void FaceShip(Vector3 facing)
        {
            if (Ship == null) return;
            facing.y = 0f;
            Quaternion rotation = Quaternion.LookRotation(facing.normalized);
            Ship.Body.rotation = rotation;
            Ship.transform.rotation = rotation;
            Ship.Body.angularVelocity = Vector3.zero;
        }

        /// <summary>Turns the blob's face (eyes and shape) toward a world direction while it stands still.</summary>
        public void FaceBlob(Vector3 facing)
        {
            facing.y = 0f;
            BlobSkin skin = Blob.GetComponent<BlobSkin>();
            if (skin != null) SetMember(skin, "frameForward", facing.normalized);
        }

        public static void Place(Transform target, Vector3 position, Quaternion? rotation = null)
        {
            target.position = position;
            if (rotation.HasValue) target.rotation = rotation.Value;
            if (target.TryGetComponent(out Rigidbody body))
            {
                body.position = position;
                if (rotation.HasValue) body.rotation = rotation.Value;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            Physics.SyncTransforms();
        }

        public GameObject Find(string name)
        {
            GameObject found = GameObject.Find(name);
            if (found != null) return found;
            // Inactive objects too.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name || PathOf(t).EndsWith("/" + name)) return t.gameObject;
            return null;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        public T Get<T>(string name) where T : Component
        {
            GameObject go = Find(name);
            return go != null ? go.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>Sets a field or property by reflection (for staging: a latch already pressed, a timer...).</summary>
        public static void SetMember(object target, string member, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            FieldInfo field = type.GetField(member, flags);
            if (field != null) field.SetValue(target, value);
            else type.GetProperty(member, flags)?.SetValue(target, value);
        }

        public static object GetMember(object target, string member)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            FieldInfo field = type.GetField(member, flags);
            if (field != null) return field.GetValue(target);
            return type.GetProperty(member, flags)?.GetValue(target);
        }
    }
}
#endif
