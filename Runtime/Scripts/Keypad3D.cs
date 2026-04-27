using UdonSharp;
using TMPro;
using UnityEngine;
using VRC.SDKBase;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 3D キーパッド。子の KeypadKey から呼ばれて文字を _targetField に append する汎用ユーティリティ。
    ///
    /// **v1 では Search との連携には使われていない**: Udon は VRCUrlInputField.text 設定を
    /// 公開していないため、3D キーパッドから検索 URL を組み立てることができない。Search 入力は
    /// VRCUrlInputField + VRChat 内蔵キーボード方式 (Copy/Paste 含む) で実現する。
    /// 本クラスは将来の汎用 TMP_InputField への入力手段として残す。
    ///
    /// _targetField の text は固定プレフィックス (_prefix) で始まる前提。Backspace 等で
    /// プレフィックス領域を消さないよう範囲制限する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Keypad3D : UdonSharpBehaviour
    {
        [Header("Target")]
        [Tooltip("キー入力で書き換える TMP_InputField")]
        [SerializeField] private TMP_InputField _targetField;

        [Tooltip("ロックされたプレフィックス (Backspace で消えない固定部分)。空文字なら制約なし")]
        [SerializeField] private string _prefix = "";

        [Header("Submit (任意)")]
        [Tooltip("Enter キーで呼ばれるコントローラー (v1 では未連携)")]
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
            // v1 では未連携。将来 _controller 経由のイベント発火に使う想定。
            if (_targetField == null) return;
            EnsurePrefix();
        }
    }
}
