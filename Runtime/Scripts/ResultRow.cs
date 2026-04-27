using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDK3.Data;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 結果カード 1 行分の状態と Click 受け口。Pre-allocated 20 行方式で prefab に
    /// 物理配置される (Hierarchy 上で _index = 0..19 をインスペクタでハードコード)。
    ///
    /// 役割:
    /// - 自身のインデックスを保持
    /// - Controller から SetData(name, owner, trackCount, thumbIndex) を受けて
    ///   #Name / #Owner / #TrackCount / #Thumbnail を更新
    /// - 子の #SelectButton.onClick → OnSelect → _controller.OnSelectResultByIndex(_index)
    ///
    /// 動的 Instantiate を避ける理由は docs/unity-architecture.md §5.3 を参照。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ResultRow : UdonSharpBehaviour
    {
        [Header("Identity (prefab で 0..19 をハードコード)")]
        [SerializeField] private int _index = -1;

        [Header("Refs")]
        [SerializeField] private PlaylistViewerController _controller;
        [SerializeField] private ThumbnailLoader _thumbnailLoader;

        [Header("Bound visual elements")]
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _ownerText;
        [SerializeField] private TextMeshProUGUI _trackCountText;
        [SerializeField] private RawImage _thumbnailImage;

        [Header("Display config")]
        [SerializeField] private Texture _placeholderThumbnail;
        [SerializeField] private string _trackCountSuffix = " tracks";

        public int Index => _index;

        /// <summary>
        /// Controller から呼ばれる。row が表示すべきデータをセットし、SetActive(true) で表示する。
        /// </summary>
        public void SetData(string name, string owner, int trackCount, int thumbIndex)
        {
            if (_nameText != null) _nameText.text = name != null ? name : "";
            if (_ownerText != null) _ownerText.text = owner != null ? owner : "";
            if (_trackCountText != null) _trackCountText.text = trackCount.ToString() + _trackCountSuffix;

            if (_thumbnailImage != null)
            {
                if (thumbIndex >= 0 && _thumbnailLoader != null)
                {
                    _thumbnailLoader.LoadThumbnail(thumbIndex, _thumbnailImage);
                }
                else
                {
                    _thumbnailImage.texture = _placeholderThumbnail;
                }
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// このスロットを使っていないときに呼ばれる。表示を畳む。
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Suffix を多言語化したい場合 (Controller から OnLanguageChanged 経由で呼ばれる想定)。
        /// </summary>
        public void SetTrackCountSuffix(string suffix)
        {
            _trackCountSuffix = suffix != null ? suffix : "";
        }

        /// <summary>
        /// #SelectButton.onClick から (prefab Inspector で) 呼ばれる。
        /// Button から index を引数で渡す手段がないので、代わりに自身の _index を controller に投げる。
        /// </summary>
        public void OnSelect()
        {
            if (_controller == null) return;
            if (_index < 0)
            {
                Debug.LogWarning("[ResultRow] _index not set on " + gameObject.name);
                return;
            }
            _controller.OnSelectResultByIndex(_index);
        }
    }
}
