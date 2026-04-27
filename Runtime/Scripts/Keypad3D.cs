using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 3D キーパッド (Search 用)。子の KeypadKey から呼ばれて文字を _targetField に append する。
    ///
    /// _targetField の text は API URL プレフィックス (_prefix) で始まる前提。Backspace 等で
    /// プレフィックス領域を消さないよう範囲制限する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Keypad3D : UdonSharpBehaviour
    {
        [Header("Target")]
        [Tooltip("キー入力で書き換える VRCUrlInputField (Search 用)")]
        [SerializeField] private VRCUrlInputField _targetField;

        [Tooltip("ロックされたプレフィックス (= API URL の固定部分)")]
        [SerializeField] private string _prefix = "https://playlist.vrc-hub.com/api/vrc/playlists/search?q=";

        [Header("Submit")]
        [Tooltip("Enter キーで呼ばれるコントローラー")]
        [SerializeField] private PlaylistViewerController _controller;

        [Tooltip("入力可能な最大クエリ長 (プレフィックスを含まない)")]
        [SerializeField] private int _maxQueryLength = 64;

        void Start()
        {
            EnsurePrefix();
        }

        public void EnsurePrefix()
        {
            if (_targetField == null) return;
            if (_targetField.text == null || !_targetField.text.StartsWith(_prefix))
            {
                _targetField.text = _prefix;
            }
        }

        // ----- 子 KeypadKey から呼ばれる -----

        public void AppendChar(string c)
        {
            if (_targetField == null || c == null || c.Length == 0) return;
            EnsurePrefix();

            string current = _targetField.text;
            int queryLen = current.Length - _prefix.Length;
            if (queryLen < 0) queryLen = 0;
            if (queryLen + c.Length > _maxQueryLength) return;

            _targetField.text = current + c;
        }

        public void Backspace()
        {
            if (_targetField == null) return;
            EnsurePrefix();

            string current = _targetField.text;
            if (current.Length <= _prefix.Length) return;
            _targetField.text = current.Substring(0, current.Length - 1);
        }

        public void Clear()
        {
            if (_targetField == null) return;
            _targetField.text = _prefix;
        }

        public void Submit()
        {
            if (_targetField == null) return;
            EnsurePrefix();
            string current = _targetField.text;
            if (current.Length <= _prefix.Length) return; // 空クエリは送らない
            if (_controller != null) _controller.OnTabSearch();
        }
    }
}
