namespace EasyCapture.Core;

/// <summary>
/// 오버레이에서 키를 누른 채 유지할 때 생기는 자동 반복 입력이 동작으로 이어지지 않게 하되, 새로 누른 키까지 막지는 않는다.
/// 두 경우만 무시한다.
/// 1) 오버레이가 뜰 때 이미 눌려 있던 키(예: Ctrl+Shift+Enter 단축키를 누르는 중): 단축키가 첫 누름을 가져가므로
///    오버레이는 이 키의 반복을 "반복 아님"으로 받을 수 있다. 그래서 뗄 때까지 모든 누름을 무시한다.
/// 2) 오버레이가 누름(반복 아님)을 직접 받았고 아직 뗌을 받지 못한 키의 반복.
/// Enter로 닫힌 직전 오버레이처럼 키 뗌이 다른 창으로 가면 시스템은 다음 첫 누름을 반복으로 표시하는데,
/// 그 키는 위 두 경우에 없으므로 정상 입력으로 처리된다.
/// </summary>
public sealed class KeyRepeatGuard
{
    private readonly HashSet<int> _armed = [];
    private readonly HashSet<int> _pressed = [];

    /// <summary>오버레이가 뜰 때 실제로 눌려 있는 키(가상 키 코드)를 기록한다.</summary>
    public void Arm(IEnumerable<int> heldKeys)
    {
        _armed.Clear();
        foreach (var key in heldKeys)
        {
            _armed.Add(key);
        }
    }

    /// <summary>키 누름을 받았을 때. 동작으로 이어가야 하면 true, 눌린 채 유지되는 중의 입력이면 false.</summary>
    public bool ShouldAct(int key, bool isRepeat)
    {
        if (_armed.Contains(key) || (isRepeat && _pressed.Contains(key)))
        {
            return false;
        }

        _pressed.Add(key);
        return true;
    }

    /// <summary>키 뗌을 받았을 때.</summary>
    public void Release(int key)
    {
        _armed.Remove(key);
        _pressed.Remove(key);
    }

    /// <summary>
    /// 뗌이 오버레이에 오지 않은 경우(포커스를 얻기 전에 뗀 경우)를 위해, 더는 눌려 있지 않은 기록을 지운다.
    /// </summary>
    public void ReleaseIfUp(Func<int, bool> isDown)
    {
        _armed.RemoveWhere(key => !isDown(key));
    }
}
