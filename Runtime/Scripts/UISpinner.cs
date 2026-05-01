using UdonSharp;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// Z 軸を毎フレーム回転させるだけの極小コンポーネント。
    /// Loading overlay 内の spinner GameObject にアタッチすると、overlay が SetActive(false) のとき
    /// Update が呼ばれないため起動制御は不要 (#23 Phase A)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UISpinner : UdonSharpBehaviour
    {
        [Tooltip("Z 軸回転速度 (°/s)。負値で時計回り (Material Design indeterminate と同方向)")]
        [SerializeField] private float _degreesPerSecond = -240f;

        void Update()
        {
            transform.Rotate(0f, 0f, _degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
