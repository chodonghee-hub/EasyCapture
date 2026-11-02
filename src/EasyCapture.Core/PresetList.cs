namespace EasyCapture.Core;

public static class PresetList
{
    public const string AreaNamePrefix = "영역 ";

    /// <summary>현재 프리셋에서 step만큼 이동한 프리셋(목록 끝에서 처음으로 순환). 현재가 목록에 없으면 앞으로는 첫째, 뒤로는 마지막. 목록이 비면 null.</summary>
    public static CapturePreset? Cycle(IReadOnlyList<CapturePreset> presets, Guid? currentId, int step)
    {
        if (presets.Count == 0)
        {
            return null;
        }

        var index = -1;
        for (var i = 0; i < presets.Count; i++)
        {
            if (presets[i].Id == currentId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return step >= 0 ? presets[0] : presets[^1];
        }

        var count = presets.Count;
        return presets[(((index + step) % count) + count) % count];
    }

    /// <summary>"영역 1", "영역 2"… 중 기존 이름과 겹치지 않는 가장 작은 번호의 이름.</summary>
    public static string NextAreaName(IEnumerable<string> existingNames)
    {
        var used = new HashSet<string>(
            existingNames.Where(n => n is not null).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        for (var n = 1; ; n++)
        {
            var name = AreaNamePrefix + n;
            if (!used.Contains(name))
            {
                return name;
            }
        }
    }
}
