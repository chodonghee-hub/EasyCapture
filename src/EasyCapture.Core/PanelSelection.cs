namespace EasyCapture.Core;

/// <summary>
/// 클립보드 패널의 다중 선택 모델. 목록(최신순 id)과 선택 집합, 커서(키보드 위치), 기준점(Shift 범위의 시작)을 관리한다.
/// UI와 무관하며 시간·키 입력에 의존하지 않는다. 선택 결과(<see cref="SelectedIds"/>)는 항상 목록 순서다.
/// </summary>
public sealed class PanelSelection
{
    private readonly HashSet<Guid> _selected = [];
    private List<Guid> _order = [];
    private Dictionary<Guid, int> _index = [];
    private int _cursor = -1;
    private int _anchor = -1;

    /// <summary>목록에 있는 항목 수.</summary>
    public int ItemCount => _order.Count;

    /// <summary>선택된 항목 수.</summary>
    public int Count => _selected.Count;

    /// <summary>커서가 가리키는 항목 id. 목록이 비었거나 아직 이동한 적이 없으면 null.</summary>
    public Guid? Cursor => _cursor >= 0 ? _order[_cursor] : null;

    /// <summary>커서의 목록 내 위치. 없으면 -1.</summary>
    public int CursorIndex => _cursor;

    /// <summary>선택된 id를 목록 순서(앞쪽이 최신)로 돌려준다. 이미지 복사 같은 "첫 번째 선택"은 이 목록의 첫 항목이다.</summary>
    public IReadOnlyList<Guid> SelectedIds => _order.Where(_selected.Contains).ToArray();

    /// <summary>해당 id가 선택돼 있는지.</summary>
    public bool IsSelected(Guid id) => _selected.Contains(id);

    /// <summary>
    /// 목록을 새로 읽은 뒤 호출한다. 목록에 없어진 id는 선택·커서·기준점에서 정리한다.
    /// 커서 항목이 사라지면 같은 위치(끝을 넘으면 마지막) 항목으로 옮기고, 선택이 있었는데 모두 사라졌으면 그 항목을 선택한다.
    /// 목록이 비면 모두 비운다. 이전에 선택이 없었으면 새로 선택하지 않는다. 중복 id는 첫 번째만 남긴다.
    /// </summary>
    public void SetItems(IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var oldCursorId = Cursor;
        var oldCursorIndex = _cursor;
        var oldAnchorId = _anchor >= 0 ? _order[_anchor] : (Guid?)null;
        var hadSelection = _selected.Count > 0;

        _order = ids.Distinct().ToList();
        _index = new Dictionary<Guid, int>(_order.Count);
        for (var i = 0; i < _order.Count; i++)
        {
            _index[_order[i]] = i;
        }

        _selected.RemoveWhere(id => !_index.ContainsKey(id));

        if (_order.Count == 0)
        {
            _cursor = -1;
            _anchor = -1;
            return;
        }

        if (oldCursorId is { } cursorId && _index.TryGetValue(cursorId, out var cursorIndex))
        {
            _cursor = cursorIndex;
        }
        else if (oldCursorIndex >= 0)
        {
            _cursor = Math.Min(oldCursorIndex, _order.Count - 1);
        }
        else
        {
            _cursor = -1;
        }

        if (oldAnchorId is { } anchorId && _index.TryGetValue(anchorId, out var anchorIndex))
        {
            _anchor = anchorIndex;
        }
        else
        {
            _anchor = _cursor;
        }

        if (_selected.Count == 0 && hadSelection && _cursor >= 0)
        {
            _selected.Add(_order[_cursor]);
        }
    }

    /// <summary>좌우 방향키. 끝에서 멈춘다(순환 없음). 커서가 없으면 첫 항목으로 간다. extend면 기준점에서 커서까지 범위를 선택한다.</summary>
    public void MoveBy(int delta, bool extend)
    {
        if (_order.Count == 0)
        {
            return;
        }

        // int.MaxValue 같은 큰 delta가 오버플로해 반대쪽 끝으로 가지 않도록 long으로 더한 뒤 끝으로 보정한다.
        var target = _cursor < 0 ? 0L : (long)_cursor + delta;
        MoveTo((int)Math.Clamp(target, 0L, _order.Count - 1), extend);
    }

    /// <summary>
    /// 상하 방향키. rows행만큼 이동한다(한 행 = columns개). 아래쪽 행이 있지만 같은 칸이 없으면(마지막 행이 덜 찬 경우) 마지막 항목으로 가고,
    /// 이동할 행이 아예 없으면 제자리다. 커서가 없으면 첫 항목으로 간다. columns는 1 미만이면 1로 본다.
    /// </summary>
    public void MoveRows(int rows, int columns, bool extend)
    {
        if (_order.Count == 0)
        {
            return;
        }

        if (_cursor < 0)
        {
            MoveTo(0, extend);
            return;
        }

        columns = Math.Max(1, columns);
        // rows * columns가 int를 넘어도 부호가 뒤집히지 않도록 long으로 계산한다.
        var requested = _cursor + ((long)rows * columns);
        int target;
        if (requested < 0)
        {
            // 첫 행이면 제자리, 아니면(여러 행 이동 요청 등) 첫 항목까지 간다.
            target = _cursor / columns > 0 ? 0 : _cursor;
        }
        else if (requested > _order.Count - 1)
        {
            var lastRow = (_order.Count - 1) / columns;
            target = _cursor / columns < lastRow ? _order.Count - 1 : _cursor;
        }
        else
        {
            target = (int)requested;
        }

        MoveTo(target, extend);
    }

    /// <summary>
    /// 커서를 지정 위치로 옮긴다(범위를 벗어나면 끝으로 보정, Home/End/PageUp/PageDown용).
    /// extend가 아니면 그 항목만 선택하고 기준점을 옮기며, extend면 기준점에서 커서까지 범위로 선택을 바꾼다.
    /// </summary>
    public void MoveTo(int index, bool extend)
    {
        if (_order.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _order.Count - 1);
        if (extend)
        {
            if (_anchor < 0)
            {
                _anchor = _cursor >= 0 ? _cursor : index;
            }

            _cursor = index;
            SelectRange(_anchor, _cursor, keepExisting: false);
        }
        else
        {
            _cursor = index;
            _anchor = index;
            _selected.Clear();
            _selected.Add(_order[index]);
        }
    }

    /// <summary>
    /// 마우스 클릭. 수식어 없으면 그 항목만 선택, ctrl이면 선택을 토글, shift면 기준점에서 그 항목까지 범위로 바꾼다(ctrl+shift면 기존 선택에 더한다).
    /// 목록에 없는 id는 무시한다.
    /// </summary>
    public void Click(Guid id, bool ctrl, bool shift)
    {
        if (!_index.TryGetValue(id, out var index))
        {
            return;
        }

        if (shift)
        {
            if (_anchor < 0)
            {
                _anchor = index;
            }

            _cursor = index;
            SelectRange(_anchor, index, keepExisting: ctrl);
        }
        else if (ctrl)
        {
            if (!_selected.Remove(id))
            {
                _selected.Add(id);
            }

            _cursor = index;
            _anchor = index;
        }
        else
        {
            SelectOnly(id);
        }
    }

    /// <summary>그 항목만 선택하고 커서·기준점을 옮긴다. 목록에 없는 id는 무시한다.</summary>
    public void SelectOnly(Guid id)
    {
        if (!_index.TryGetValue(id, out var index))
        {
            return;
        }

        _cursor = index;
        _anchor = index;
        _selected.Clear();
        _selected.Add(id);
    }

    /// <summary>모든 항목을 선택한다(Ctrl+A). 커서가 없으면 첫 항목에 둔다.</summary>
    public void SelectAll()
    {
        if (_order.Count == 0)
        {
            return;
        }

        if (_cursor < 0)
        {
            _cursor = 0;
        }

        _anchor = _cursor;
        foreach (var id in _order)
        {
            _selected.Add(id);
        }
    }

    /// <summary>선택만 비운다. 커서·기준점은 그대로다.</summary>
    public void Clear() => _selected.Clear();

    private void SelectRange(int from, int to, bool keepExisting)
    {
        if (!keepExisting)
        {
            _selected.Clear();
        }

        var (start, end) = from <= to ? (from, to) : (to, from);
        for (var i = start; i <= end; i++)
        {
            _selected.Add(_order[i]);
        }
    }
}
