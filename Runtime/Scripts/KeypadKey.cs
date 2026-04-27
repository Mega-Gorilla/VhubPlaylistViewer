using UdonSharp;
using UnityEngine;

namespace MegaGorilla.KawaPlayer.PlaylistViewer
{
    /// <summary>
    /// 3D キーパッドの個別キー。Interact (タップ) で親 Keypad3D に文字を伝える。
    ///
    /// Mode:
    ///  - Char: _value 文字列を AppendChar
    ///  - Backspace: 1 文字削除
    ///  - Clear: 全削除 (プレフィックスは残す)
    ///  - Submit: 検索送信
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KeypadKey : UdonSharpBehaviour
    {
        public const int MODE_CHAR = 0;
        public const int MODE_BACKSPACE = 1;
        public const int MODE_CLEAR = 2;
        public const int MODE_SUBMIT = 3;

        [Header("Parent")]
        [SerializeField] private Keypad3D _keypad;

        [Header("Behavior")]
        [SerializeField] private int _mode = MODE_CHAR;
        [SerializeField] private string _value = "a";

        public override void Interact()
        {
            Press();
        }

        // 別経路 (UI Button onClick など) からも呼べるように public
        public void Press()
        {
            if (_keypad == null) return;
            switch (_mode)
            {
                case MODE_CHAR:
                    _keypad.AppendChar(_value);
                    break;
                case MODE_BACKSPACE:
                    _keypad.Backspace();
                    break;
                case MODE_CLEAR:
                    _keypad.Clear();
                    break;
                case MODE_SUBMIT:
                    _keypad.Submit();
                    break;
            }
        }
    }
}
