using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using EasyCapture.Core;

namespace EasyCapture.App;

/// <summary>패널 그리드의 타일 하나(뷰 모델). 항목 정보와 썸네일, 선택·커서 표시 상태를 XAML 바인딩에 노출한다.</summary>
public sealed class PanelTile : INotifyPropertyChanged
{
    private CaptureItem _item;
    private ImageSource? _thumbnail;
    private string _placeholderText = "불러오는 중…";
    private bool _isSelected;
    private bool _isCursor;

    public PanelTile(CaptureItem item)
    {
        _item = item;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CaptureItem Item => _item;

    public Guid Id => _item.Id;

    public string Name => _item.Name;

    public string Caption => $"{_item.Width}×{_item.Height} · {_item.CreatedAt.LocalDateTime:MM-dd HH:mm}";

    public string ToolTipText => $"{_item.Name}\n{_item.Width}×{_item.Height} · {_item.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}";

    public Visibility PinVisibility => _item.Pinned ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Thumbnail => _thumbnail;

    /// <summary>썸네일이 없을 때 보여줄 문구(불러오는 중 / 미리보기 없음 / 파일 없음).</summary>
    public string PlaceholderText => _placeholderText;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>키보드 커서가 있는 타일.</summary>
    public bool IsCursor
    {
        get => _isCursor;
        set => Set(ref _isCursor, value);
    }

    /// <summary>보관소에서 다시 읽은 최신 항목 정보로 바꾼다(이름 변경·고정 반영).</summary>
    public void Update(CaptureItem item)
    {
        _item = item;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(PinVisibility));
    }

    /// <summary>썸네일을 지정한다. null이면 읽지 못한 것이므로 자리표시 문구를 바꾼다.</summary>
    public void SetThumbnail(ImageSource? image, string missingText = "미리보기 없음")
    {
        _thumbnail = image;
        _placeholderText = image is null ? missingText : string.Empty;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(PlaceholderText));
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field != value)
        {
            field = value;
            OnPropertyChanged(name);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
