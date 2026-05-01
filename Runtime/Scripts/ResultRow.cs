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

        [Header("Text length limits (TMP overflow=Ellipsis/Truncate が VRChat で頂点ゼロ bug を起こすため C# 側で truncate)")]
        [Tooltip("playlist 名の最大文字数。超過分は末尾を「…」で省略。#Name rect 648 + fontSize 36 で全角 18 + margin")]
        [SerializeField] private int _nameMaxChars = 20;
        [Tooltip("owner 名の最大文字数。超過分は末尾を「…」で省略。#Owner rect 280 + fontSize 20 で全角 14 + margin")]
        [SerializeField] private int _ownerMaxChars = 16;

        public int Index => _index;

        /// <summary>
        /// Controller から呼ばれる。row が表示すべきデータをセットし、SetActive(true) で表示する。
        /// ytThumbIndex は yt-thumb-direct pool (i.ytimg.com 直接 URL) の index (vhub-playlist#92 v4)。
        /// </summary>
        public void SetData(string name, string owner, int trackCount, int ytThumbIndex)
        {
            if (_nameText != null) _nameText.text = TruncateString(name, _nameMaxChars);
            if (_ownerText != null) _ownerText.text = TruncateString(owner, _ownerMaxChars);
            if (_trackCountText != null) _trackCountText.text = trackCount.ToString() + _trackCountSuffix;

            if (_thumbnailImage != null)
            {
                if (ytThumbIndex >= 0 && _thumbnailLoader != null)
                {
                    _thumbnailLoader.LoadYtThumbnail(ytThumbIndex, _thumbnailImage);
                }
                else
                {
                    _thumbnailImage.texture = _placeholderThumbnail;
                }
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// VRChat の TMP で overflow=Ellipsis / Truncate が頂点ゼロ bug を起こすため、
        /// C# 側で text を最大文字数で切り捨てて末尾に「…」(U+2026) を付加する。
        /// 詳細: docs/unity-architecture.md §13.1
        /// </summary>
        private string TruncateString(string s, int maxChars)
        {
            if (s == null) return "";
            if (maxChars <= 0 || s.Length <= maxChars) return s;
            return s.Substring(0, maxChars) + "…";
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
