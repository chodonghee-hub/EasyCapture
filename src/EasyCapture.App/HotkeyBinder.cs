using EasyCapture.Core;

namespace EasyCapture.App;

/// <summary>단축키가 눌렸을 때 실행할 동작. 이후 임무가 실제 동작으로 교체한다.</summary>
public sealed class HotkeyHandlers
{
    public Action StartCapture { get; set; } = () => { };

    public Action OpenPanel { get; set; } = () => { };

    public Action CopyLast { get; set; } = () => { };

    public Action<CapturePreset> CaptureWithPreset { get; set; } = _ => { };
}

/// <summary>설정의 단축키(동작 3개 + 프리셋 전용)를 HotkeyService에 등록한다. 빈 값(비활성)은 등록·충돌 검사에서 제외된다.</summary>
public sealed class HotkeyBinder
{
    // 풍선 알림 본문 한도(255자)를 넘기지 않도록 줄인다.
    private const int MaxNoticeLength = 250;

    private readonly HotkeyService _service;
    private readonly HotkeyHandlers _handlers;
    private readonly Action<string> _notify;

    public HotkeyBinder(HotkeyService service, HotkeyHandlers handlers, Action<string> notify)
    {
        _service = service;
        _handlers = handlers;
        _notify = notify;
        _service.CallbackFailed = ex => Notify($"단축키 동작 중 오류가 발생했습니다: {ex.Message}");
    }

    /// <summary>모두 해제한 뒤 설정대로 다시 등록한다. 충돌·등록 실패 안내문을 돌려주고 알림으로도 띄운다.</summary>
    public IReadOnlyList<string> Rebind(AppSettings settings)
    {
        _service.UnregisterAll();

        var problems = new List<string>();
        var entries = HotkeyConflicts.CollectEntries(settings);

        var conflicts = HotkeyConflicts.Find(entries);
        if (conflicts.Count > 0)
        {
            var detail = string.Join(", ", conflicts.Select(c => $"{c.Hotkey} ({string.Join(", ", c.Names)})"));
            problems.Add($"같은 단축키가 여러 동작에 지정되어 있습니다: {detail}");
        }

        // 같은 조합이 겹치면 첫 항목만 등록한다.
        var registered = new HashSet<Hotkey>();
        foreach (var entry in entries)
        {
            if (!registered.Add(entry.Hotkey))
            {
                continue;
            }

            var result = _service.Register(entry.Hotkey, CreateCallback(entry));
            if (result.Succeeded)
            {
                continue;
            }

            var reason = result.Status == HotkeyRegisterStatus.InUse
                ? "다른 프로그램이 사용 중입니다"
                : result.Message ?? "알 수 없는 오류";
            problems.Add($"단축키 {entry.Hotkey} 등록 실패 — {reason}");
        }

        if (problems.Count > 0)
        {
            Notify(string.Join("\n", problems));
        }

        return problems;
    }

    private void Notify(string message) =>
        _notify(message.Length > MaxNoticeLength ? message[..(MaxNoticeLength - 1)] + "…" : message);

    private Action CreateCallback(HotkeyEntry entry)
    {
        if (entry.Preset is { } preset)
        {
            return () => _handlers.CaptureWithPreset(preset);
        }

        return entry.Action switch
        {
            HotkeyAction.StartCapture => () => _handlers.StartCapture(),
            HotkeyAction.OpenPanel => () => _handlers.OpenPanel(),
            _ => () => _handlers.CopyLast(),
        };
    }
}
