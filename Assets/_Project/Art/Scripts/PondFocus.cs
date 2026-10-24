using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Tells the Foam Pond water shader where the blob is: the centre and radius of each piece (up to four, so both
    /// halves after a piercer pipe), so the foam clears around it and a soft ring marks it while it swims. Installs
    /// itself when a scene loads; the shader ignores it where there is no foam pond.
    /// </summary>
    public class PondFocus : MonoBehaviour
    {
        const int MaxPieces = 4;
        const float BaseRadius = 0.5f;

        static readonly int FocusId = Shader.PropertyToID("_PondFocus");
        static readonly int CountId = Shader.PropertyToID("_PondFocusCount");

        readonly Vector4[] focus = new Vector4[MaxPieces];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<PondFocus>() != null) return;
            var go = new GameObject(nameof(PondFocus));
            DontDestroyOnLoad(go);
            go.AddComponent<PondFocus>();
        }

        void LateUpdate()
        {
            var pieces = BlobSize.All;
            int count = Mathf.Min(pieces.Count, MaxPieces);
            for (int i = 0; i < count; i++)
            {
                Vector3 centre = pieces[i].transform.position;
                // Lengths follow the cube root of the volume, as in BlobSize.
                focus[i] = new Vector4(centre.x, centre.y, centre.z, BaseRadius * Mathf.Pow(pieces[i].Volume, 1f / 3f));
            }
            Shader.SetGlobalVectorArray(FocusId, focus);
            Shader.SetGlobalFloat(CountId, count);
        }

        void OnDestroy() => Shader.SetGlobalFloat(CountId, 0f);
    }
}
