using System.Windows;
using System.Windows.Input;
using EasyCapture.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace EasyCapture.App.Controls;

/// <summary>포커스 상태에서 누른 키 조합을 단축키로 입력받는 읽기 전용 텍스트 상자.</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyTextProperty = DependencyProperty.Register(
        nameof(HotkeyText),
        typeof(string),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((HotkeyBox)d).OnHotkeyTextChanged()));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        InputMethod.SetIsInputMethodEnabled(this, false);
        ShowValue();
    }

    /// <summary>"Ctrl+Shift+S" 형태. 지우면 null이며, null·빈 문자열은 모두 "비활성"으로 본다.</summary>
    public string? HotkeyText
    {
        get => (string?)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    /// <summary>HotkeyText 값이 바뀔 때(입력·지우기·코드 변경 모두) 발생한다.</summary>
    public event EventHandler? HotkeyChanged;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        var modifiers = CurrentModifiers();

        // 포커스 이동용 Tab은 가로채지 않는다.
        if (key == Key.Tab && (modifiers & ~HotkeyModifiers.Shift) == HotkeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        var result = HotkeyInput.Evaluate(modifiers, KeyInterop.VirtualKeyFromKey(key));
        switch (result.Kind)
        {
            case HotkeyInputKind.Incomplete:
                ShowProgress(modifiers);
                break;
            case HotkeyInputKind.Set:
                ToolTip = null;
                HotkeyText = result.Hotkey.ToString();
                break;
            case HotkeyInputKind.Clear:
                ToolTip = null;
                HotkeyText = null;
                break;
            default:
                Text = "Ctrl, Alt, Shift, Win 중 하나와 함께 누르세요";
                ToolTip = "수식어(Ctrl/Alt/Shift/Win)와 함께 누르세요. F1–F24, PrintScreen은 단독으로 가능합니다.";
                break;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        // 수식어를 뗄 때만 표시를 갱신한다(무효 조합 안내가 곧바로 지워지지 않게).
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyKeys.IsModifierKey(KeyInterop.VirtualKeyFromKey(key)))
        {
            ShowProgress(CurrentModifiers());
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ToolTip = null;
        ShowValue();
    }

    private void OnHotkeyTextChanged()
    {
        ShowValue();
        HotkeyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowProgress(HotkeyModifiers modifiers)
    {
        if (modifiers == HotkeyModifiers.None)
        {
            ShowValue();
        }
        else
        {
            Text = HotkeyInput.FormatProgress(modifiers);
        }
    }

    private void ShowValue()
    {
        Text = Hotkey.TryParse(HotkeyText, out var hotkey) ? hotkey.ToString() : string.Empty;
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var keys = Keyboard.Modifiers;
        var result = HotkeyModifiers.None;
        if ((keys & ModifierKeys.Control) != 0)
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if ((keys & ModifierKeys.Alt) != 0)
        {
            result |= HotkeyModifiers.Alt;
        }

        if ((keys & ModifierKeys.Shift) != 0)
        {
            result |= HotkeyModifiers.Shift;
        }

        if ((keys & ModifierKeys.Windows) != 0)
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }
}
