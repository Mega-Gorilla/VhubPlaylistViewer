using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Image;
using VRC.Udon.Common.Interfaces;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// yt-thumb-direct pool (i.ytimg.com 直接 baked URL) からサムネ画像を取得する。
    /// (server-api-spec.md v4 / vhub-playlist#92: 旧 /vrcurl/default-thumb/{i} → 302 → i.ytimg.com の
    ///  redirect chain は VRCImageDownloader が follow せず NG。i.ytimg.com URL を Editor 時に直接 baked する。
    ///  Ytimg は VRChat の trusted image host なので Allow Untrusted URLs OFF プレイヤーでも表示可能)
    /// 同じ ytThumbIndex のリクエストはキャッシュ命中。
    ///
    /// 制約:
    /// - VRCImageDownloader の rate limit (5s/件、scene 全体共有) が VRChat 側にあるため、
    ///   現状は単純な FIFO キューで 1 件ずつ処理する v1 実装。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ThumbnailLoader : UdonSharpBehaviour
    {
        [Header("yt-thumb-direct pool (i.ytimg.com 直接 baked、vhub-playlist#92 v4)")]
        [Tooltip("PoolGenerator が代入。VRCImageDownloader で trusted host (Ytimg) から redirect なしで取得")]
        [SerializeField] private VRCUrl[] _ytThumbPool = new VRCUrl[0];

        [Header("Fallback")]
        [SerializeField] private Texture _dummyTexture;

        [Header("Cache size")]
        [Tooltip("最近使ったテクスチャの保持数")]
        [SerializeField] private int _cacheCapacity = 256;

        public VRCUrl[] YtThumbPool => _ytThumbPool;

        // ----- キャッシュ (parallel arrays) -----
        private int[] _cachedIndices;     // thumb pool index
        private Texture2D[] _cachedTextures;
        private int _cacheCount;

        // ----- 進行中ロード -----
        private VRCImageDownloader _downloader;
        private int _loadingIndex = -1;
        private RawImage _loadingTarget;

        // ----- キュー (待機中) -----
        private int[] _queueIndices;
        private RawImage[] _queueTargets;
        private int _queueLen;

        void Start()
        {
            _downloader = new VRCImageDownloader();
            _cachedIndices = new int[_cacheCapacity];
            _cachedTextures = new Texture2D[_cacheCapacity];
            _cacheCount = 0;

            // キューはとりあえず固定長 64 (一画面分の表示数として十分)
            _queueIndices = new int[64];
            _queueTargets = new RawImage[64];
            _queueLen = 0;
        }

        void OnDestroy()
        {
            if (_downloader != null) _downloader.Dispose();
        }

        /// <summary>
        /// yt-thumb-direct pool (i.ytimg.com 直接 URL) からサムネ取得。
        /// VRCImageDownloader が trusted host (Ytimg) で redirect なし取得 → Allow Untrusted URLs 設定不要。
        /// vhub-playlist#92 v4 仕様。
        /// </summary>
        public void LoadYtThumbnail(int ytThumbIndex, RawImage target)
        {
            if (target == null) return;
            if (_ytThumbPool == null || _ytThumbPool.Length == 0)
            {
                ApplyDummy(target);
                return;
            }
            if (ytThumbIndex < 0 || ytThumbIndex >= _ytThumbPool.Length)
            {
                ApplyDummy(target);
                return;
            }

            // キャッシュヒット?
            int cacheIdx = LookupCache(ytThumbIndex);
            if (cacheIdx >= 0)
            {
                target.texture = _cachedTextures[cacheIdx];
                return;
            }

            // 進行中?
            if (_loadingIndex == ytThumbIndex)
            {
                _loadingTarget = target;
                return;
            }

            // キューへ追加
            ApplyDummy(target);
            EnqueueLoad(ytThumbIndex, target);
            ProcessQueue();
        }

        private void EnqueueLoad(int thumbIndex, RawImage target)
        {
            if (_queueLen >= _queueIndices.Length) return; // キュー満杯、新規 enqueue は捨てる
            _queueIndices[_queueLen] = thumbIndex;
            _queueTargets[_queueLen] = target;
            _queueLen++;
        }

        private void ProcessQueue()
        {
            if (_loadingIndex >= 0) return; // 進行中なら待つ
            if (_queueLen == 0) return;

            int idx = _queueIndices[0];
            RawImage target = _queueTargets[0];

            // shift
            for (int i = 1; i < _queueLen; i++)
            {
                _queueIndices[i - 1] = _queueIndices[i];
                _queueTargets[i - 1] = _queueTargets[i];
            }
            _queueLen--;

            VRCUrl url = _ytThumbPool[idx];
            if (!Utilities.IsValid(url) || url.Get().Length == 0)
            {
                ApplyDummy(target);
                ProcessQueue();
                return;
            }

            _loadingIndex = idx;
            _loadingTarget = target;
            TextureInfo info = new TextureInfo();
            info.GenerateMipMaps = false;
            info.WrapModeU = TextureWrapMode.Clamp;
            info.WrapModeV = TextureWrapMode.Clamp;
            _downloader.DownloadImage(url, null, (IUdonEventReceiver)this, info);
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result)
        {
            int idx = _loadingIndex;
            RawImage target = _loadingTarget;
            _loadingIndex = -1;
            _loadingTarget = null;

            if (idx < 0) { ProcessQueue(); return; }

            Texture2D tex = result.Result;
            CacheStore(idx, tex);

            if (target != null)
            {
                target.texture = tex;
            }

            ProcessQueue();
        }

        public override void OnImageLoadError(IVRCImageDownload result)
        {
            RawImage target = _loadingTarget;
            _loadingIndex = -1;
            _loadingTarget = null;
            if (target != null) ApplyDummy(target);
            ProcessQueue();
        }

        // ----- キャッシュ (LRU 風: 最古を上書き) -----

        private int LookupCache(int thumbIndex)
        {
            for (int i = 0; i < _cacheCount; i++)
            {
                if (_cachedIndices[i] == thumbIndex) return i;
            }
            return -1;
        }

        private void CacheStore(int thumbIndex, Texture2D tex)
        {
            if (_cacheCount < _cacheCapacity)
            {
                _cachedIndices[_cacheCount] = thumbIndex;
                _cachedTextures[_cacheCount] = tex;
                _cacheCount++;
            }
            else
            {
                // Shift everything left, drop oldest
                for (int i = 1; i < _cacheCount; i++)
                {
                    _cachedIndices[i - 1] = _cachedIndices[i];
                    _cachedTextures[i - 1] = _cachedTextures[i];
                }
                _cachedIndices[_cacheCount - 1] = thumbIndex;
                _cachedTextures[_cacheCount - 1] = tex;
            }
        }

        private void ApplyDummy(RawImage target)
        {
            if (target == null) return;
            target.texture = _dummyTexture;
        }
    }
}
