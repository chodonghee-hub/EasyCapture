using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PanelSelectionTests
{
    private static readonly Guid[] Ids = Enumerable.Range(1, 6)
        .Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

    private static PanelSelection Create(int count = 6)
    {
        var selection = new PanelSelection();
        selection.SetItems(Ids.Take(count).ToArray());
        return selection;
    }

    private static void AssertSelection(PanelSelection selection, params int[] indices)
    {
        Assert.Equal(indices.Select(i => Ids[i]), selection.SelectedIds);
        Assert.Equal(indices.Length, selection.Count);
        for (var i = 0; i < Ids.Length; i++)
        {
            Assert.Equal(indices.Contains(i), selection.IsSelected(Ids[i]));
        }
    }

    // ---- SetItems ----

    [Fact]
    public void SetItems_InitialList_DoesNotSelectOrMoveCursor()
    {
        var selection = Create();

        Assert.Equal(6, selection.ItemCount);
        Assert.Null(selection.Cursor);
        Assert.Equal(-1, selection.CursorIndex);
        AssertSelection(selection);
    }

    [Fact]
    public void SetItems_Duplicates_KeepsFirstOccurrence()
    {
        var selection = new PanelSelection();
        selection.SetItems([Ids[2], Ids[0], Ids[2], Ids[1], Ids[0]]);
        selection.SelectAll();

        Assert.Equal(3, selection.ItemCount);
        AssertSelection(selection, 2, 0, 1);
        selection.MoveTo(2, false);
        Assert.Equal(Ids[1], selection.Cursor);
    }

    [Fact]
    public void SetItems_ReorderedList_PreservesCursorAndAnchorById()
    {
        var selection = Create();
        selection.MoveTo(1, false);
        selection.MoveTo(3, true);

        selection.SetItems([Ids[3], Ids[2], Ids[0], Ids[1]]);

        AssertSelection(selection, 3, 2, 1);
        Assert.Equal(Ids[3], selection.Cursor);
        Assert.Equal(0, selection.CursorIndex);
        selection.MoveTo(2, true);
        AssertSelection(selection, 0, 1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    public void SetItems_RemovedCursor_SelectsSamePositionOrLast(int oldIndex, int expectedIndex)
    {
        var selection = Create();
        selection.MoveTo(oldIndex, false);

        selection.SetItems([Ids[0], Ids[2], Ids[3]]);

        Assert.Equal(expectedIndex, selection.CursorIndex);
        Assert.Equal(expectedIndex == 1 ? Ids[2] : Ids[3], Assert.Single(selection.SelectedIds));
        selection.MoveTo(0, true);
        AssertSelection(selection, expectedIndex == 1 ? new[] { 0, 2 } : new[] { 0, 2, 3 });
    }

    [Fact]
    public void SetItems_SomeSelectionSurvives_DoesNotSelectReplacementCursor()
    {
        var selection = Create();
        selection.Click(Ids[0], true, false);
        selection.Click(Ids[2], true, false);

        selection.SetItems([Ids[0], Ids[1], Ids[3]]);

        Assert.Equal(Ids[3], selection.Cursor);
        AssertSelection(selection, 0);
    }

    [Fact]
    public void SetItems_NoPreviousSelection_MovesCursorWithoutSelecting()
    {
        var selection = Create();
        selection.MoveTo(2, false);
        selection.Clear();

        selection.SetItems([Ids[0], Ids[1]]);

        Assert.Equal(Ids[1], selection.Cursor);
        AssertSelection(selection);
    }

    [Fact]
    public void SetItems_RemovedAnchor_UsesSurvivingCursorAsNewAnchor()
    {
        var selection = Create();
        selection.MoveTo(1, false);
        selection.MoveTo(3, true);

        selection.SetItems([Ids[0], Ids[2], Ids[3], Ids[4]]);
        selection.MoveTo(3, true);

        AssertSelection(selection, 3, 4);
    }

    [Fact]
    public void SetItems_EmptyList_ClearsSelectionCursorAndAnchor()
    {
        var selection = Create();
        selection.MoveTo(4, false);

        selection.SetItems([]);

        Assert.Equal(0, selection.ItemCount);
        Assert.Null(selection.Cursor);
        Assert.Equal(-1, selection.CursorIndex);
        AssertSelection(selection);
        selection.SetItems(Ids);
        selection.MoveTo(2, true);
        AssertSelection(selection, 2);
    }

    [Fact]
    public void SetItems_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Create().SetItems(null!));
    }

    // ---- MoveBy ----

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(5, 1, 5)]
    [InlineData(2, 2, 4)]
    [InlineData(3, -2, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(1, int.MaxValue, 5)]
    public void MoveBy_WithCursor_ClampsAtEnds(int start, int delta, int expected)
    {
        var selection = Create();
        selection.MoveTo(start, false);

        selection.MoveBy(delta, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveBy_NoCursor_SelectsFirst(bool extend)
    {
        var selection = Create();

        selection.MoveBy(3, extend);

        Assert.Equal(0, selection.CursorIndex);
        AssertSelection(selection, 0);
    }

    [Fact]
    public void MoveBy_Extend_SelectsFromOriginalAnchor()
    {
        var selection = Create();
        selection.MoveTo(2, false);

        selection.MoveBy(2, true);
        AssertSelection(selection, 2, 3, 4);
        selection.MoveBy(-3, true);
        AssertSelection(selection, 1, 2);
    }

    // ---- MoveRows ----

    [Theory]
    [InlineData(1, 1, 3, 4)]
    [InlineData(4, -1, 3, 1)]
    [InlineData(2, 1, 3, 4)]
    [InlineData(4, 1, 3, 4)]
    [InlineData(1, -1, 3, 1)]
    [InlineData(4, -3, 3, 0)]
    [InlineData(1, 1, 0, 2)]
    [InlineData(1, 1, -3, 2)]
    [InlineData(3, 0, 3, 3)]
    public void MoveRows_WithCursor_RespectsRowsAndPartialLastRow(
        int start, int rows, int columns, int expected)
    {
        var selection = Create(5);
        selection.MoveTo(start, false);

        selection.MoveRows(rows, columns, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveRows_NoCursor_SelectsFirst(bool extend)
    {
        var selection = Create();

        selection.MoveRows(2, 3, extend);

        Assert.Equal(0, selection.CursorIndex);
        AssertSelection(selection, 0);
    }

    [Fact]
    public void MoveRows_Extend_SelectsContinuousRange()
    {
        var selection = Create();
        selection.MoveTo(1, false);

        selection.MoveRows(1, 3, true);

        AssertSelection(selection, 1, 2, 3, 4);
    }

    // ---- MoveTo ----

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(6, 5)]
    [InlineData(int.MaxValue, 5)]
    public void MoveTo_OutOfRange_ClampsIndex(int index, int expected)
    {
        var selection = Create();
        selection.SelectAll();

        selection.MoveTo(index, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Fact]
    public void MoveTo_Extend_ReplacesSelectionAndRetainsAnchorAcrossDirectionChanges()
    {
        var selection = Create();
        selection.MoveTo(2, false);
        selection.MoveTo(5, true);
        AssertSelection(selection, 2, 3, 4, 5);

        selection.MoveTo(0, true);

        AssertSelection(selection, 0, 1, 2);
        selection.MoveTo(4, false);
        selection.MoveTo(3, true);
        AssertSelection(selection, 3, 4);
    }

    [Fact]
    public void MoveTo_ExtendWithoutCursor_UsesTargetAsAnchor()
    {
        var selection = Create();

        selection.MoveTo(3, true);
        selection.MoveTo(1, true);

        AssertSelection(selection, 1, 2, 3);
    }

    // ---- Click ----

    [Fact]
    public void Click_NoModifiers_ReplacesSelectionAndMovesAnchor()
    {
        var selection = Create();
        selection.SelectAll();

        selection.Click(Ids[3], false, false);

        Assert.Equal(Ids[3], selection.Cursor);
        AssertSelection(selection, 3);
        selection.MoveTo(4, true);
        AssertSelection(selection, 3, 4);
    }

    [Fact]
    public void Click_Control_TogglesAndMovesCursorAndAnchor()
    {
        var selection = Create();
        selection.Click(Ids[4], true, false);
        selection.Click(Ids[1], true, false);
        AssertSelection(selection, 1, 4);

        selection.Click(Ids[1], true, false);

        Assert.Equal(Ids[1], selection.Cursor);
        AssertSelection(selection, 4);
        selection.MoveTo(2, true);
        AssertSelection(selection, 1, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Click_Shift_ReplacesOrAddsRange(bool ctrl)
    {
        var selection = Create();
        selection.Click(Ids[5], true, false);
        selection.Click(Ids[1], true, false);

        selection.Click(Ids[3], ctrl, true);

        Assert.Equal(Ids[3], selection.Cursor);
        AssertSelection(selection, ctrl ? new[] { 1, 2, 3, 5 } : new[] { 1, 2, 3 });
        selection.Click(Ids[0], false, true);
        AssertSelection(selection, 0, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Click_ShiftWithoutAnchor_SelectsClickedItem(bool ctrl)
    {
        var selection = Create();

        selection.Click(Ids[3], ctrl, true);

        AssertSelection(selection, 3);
        Assert.Equal(Ids[3], selection.Cursor);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Click_UnknownId_PreservesSelectionCursorAndAnchor(bool ctrl, bool shift)
    {
        var selection = Create();
        selection.MoveTo(1, false);

        selection.Click(Guid.Empty, ctrl, shift);

        AssertSelection(selection, 1);
        Assert.Equal(Ids[1], selection.Cursor);
        selection.MoveTo(2, true);
        AssertSelection(selection, 1, 2);
    }

    // ---- SelectOnly / SelectAll / Clear ----

    [Fact]
    public void SelectOnly_KnownId_ReplacesSelectionAndAnchor()
    {
        var selection = Create();
        selection.SelectAll();

        selection.SelectOnly(Ids[3]);

        AssertSelection(selection, 3);
        Assert.Equal(Ids[3], selection.Cursor);
        selection.MoveTo(4, true);
        AssertSelection(selection, 3, 4);
    }

    [Fact]
    public void SelectOnly_UnknownId_PreservesState()
    {
        var selection = Create();
        selection.MoveTo(2, false);

        selection.SelectOnly(Guid.Empty);

        AssertSelection(selection, 2);
        Assert.Equal(Ids[2], selection.Cursor);
        selection.MoveTo(3, true);
        AssertSelection(selection, 2, 3);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 3)]
    public void SelectAll_WithOrWithoutCursor_SelectsAllAndAnchorsAtCursor(int start, int expected)
    {
        var selection = Create();
        if (start >= 0)
        {
            selection.MoveTo(start, false);
        }

        selection.SelectAll();

        AssertSelection(selection, 0, 1, 2, 3, 4, 5);
        Assert.Equal(expected, selection.CursorIndex);
        selection.MoveTo(expected + 1, true);
        AssertSelection(selection, expected, expected + 1);
    }

    [Fact]
    public void Clear_ExistingRange_PreservesCursorAndAnchor()
    {
        var selection = Create();
        selection.MoveTo(1, false);
        selection.MoveTo(3, true);

        selection.Clear();

        AssertSelection(selection);
        Assert.Equal(Ids[3], selection.Cursor);
        selection.MoveTo(2, true);
        AssertSelection(selection, 1, 2);
    }

    [Fact]
    public void SelectionMethods_SingleItem_StayOnOnlyItem()
    {
        var selection = Create(1);
        selection.SelectOnly(Ids[0]);
        selection.MoveBy(1, false);
        selection.MoveRows(-1, 3, true);
        selection.MoveTo(int.MaxValue, true);
        selection.SelectAll();

        AssertSelection(selection, 0);
        Assert.Equal(Ids[0], selection.Cursor);
        selection.Click(Ids[0], true, false);
        AssertSelection(selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionMethods_EmptyList_AreNoOps(bool extend)
    {
        var selection = Create(0);
        selection.MoveBy(1, extend);
        selection.MoveRows(1, 3, extend);
        selection.MoveTo(int.MaxValue, extend);
        selection.Click(Ids[0], false, extend);
        selection.Click(Ids[0], true, extend);
        selection.SelectOnly(Ids[0]);
        selection.SelectAll();
        selection.Clear();

        AssertSelection(selection);
        Assert.Equal(0, selection.ItemCount);
        Assert.Null(selection.Cursor);
        Assert.Equal(-1, selection.CursorIndex);
    }

    // ---- MoveBy overflow boundaries ----

    [Theory]
    [InlineData(2, int.MinValue, 0)]
    [InlineData(5, int.MinValue, 0)]
    [InlineData(0, int.MaxValue, 5)]
    [InlineData(3, int.MaxValue, 5)]
    public void MoveBy_ExtremeDelta_ClampsAtEnds(int start, int delta, int expected)
    {
        var selection = Create();
        selection.MoveTo(start, false);

        selection.MoveBy(delta, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Theory]
    [InlineData(int.MinValue, 0, new[] { 0, 1, 2 })]
    [InlineData(int.MaxValue, 5, new[] { 2, 3, 4, 5 })]
    public void MoveBy_ExtremeDeltaWithExtend_SelectsFromOriginalAnchor(
        int delta, int expected, int[] selected)
    {
        var selection = Create();
        selection.MoveTo(2, false);
        selection.MoveTo(3, true);

        selection.MoveBy(delta, true);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, selected);
    }

    // ---- MoveRows overflow boundaries ----

    [Theory]
    [InlineData(1, int.MaxValue, 2, 5)]
    [InlineData(2, int.MaxValue, 3, 5)]
    [InlineData(4, int.MaxValue, 2, 4)]
    [InlineData(4, int.MaxValue, 3, 4)]
    [InlineData(4, int.MinValue, 2, 0)]
    [InlineData(4, int.MinValue, 3, 0)]
    [InlineData(1, int.MinValue, 2, 1)]
    [InlineData(2, int.MinValue, 3, 2)]
    [InlineData(3, int.MaxValue, int.MaxValue, 3)]
    [InlineData(3, int.MinValue, int.MaxValue, 3)]
    public void MoveRows_ExtremeRows_RespectsFirstAndLastRows(
        int start, int rows, int columns, int expected)
    {
        var selection = Create();
        selection.MoveTo(start, false);

        selection.MoveRows(rows, columns, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Theory]
    [InlineData(int.MaxValue, 0, 5)]
    [InlineData(int.MinValue, 0, 0)]
    [InlineData(int.MaxValue, -3, 5)]
    [InlineData(int.MinValue, -3, 0)]
    public void MoveRows_ExtremeRowsWithNonPositiveColumns_TreatsColumnsAsOne(
        int rows, int columns, int expected)
    {
        var selection = Create();
        selection.MoveTo(2, false);

        selection.MoveRows(rows, columns, false);

        Assert.Equal(expected, selection.CursorIndex);
        AssertSelection(selection, expected);
    }

    [Theory]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, true)]
    [InlineData(int.MinValue, true)]
    public void MoveRows_ExtremeRowsWithoutCursor_SelectsFirst(int rows, bool extend)
    {
        var selection = Create();

        selection.MoveRows(rows, int.MaxValue, extend);

        Assert.Equal(0, selection.CursorIndex);
        AssertSelection(selection, 0);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void MoveRows_ExtremeRowsWithSingleItem_StaysOnOnlyItem(int rows)
    {
        var selection = Create(1);
        selection.MoveTo(0, false);

        selection.MoveRows(rows, 3, false);

        Assert.Equal(0, selection.CursorIndex);
        AssertSelection(selection, 0);
    }

    [Theory]
    [InlineData(5, int.MaxValue, 6)]
    [InlineData(6, int.MaxValue, 6)]
    [InlineData(6, int.MinValue, 0)]
    [InlineData(2, int.MinValue, 2)]
    public void MoveRows_ExtremeRowsWithPartialLastRow_RespectsRowBoundaries(
        int start, int rows, int expected)
    {
        var ids = Enumerable.Range(1, 7)
            .Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        var selection = new PanelSelection();
        selection.SetItems(ids);
        selection.MoveTo(start, false);

        selection.MoveRows(rows, 3, false);

        Assert.Equal(expected, selection.CursorIndex);
        Assert.Equal(ids[expected], Assert.Single(selection.SelectedIds));
        Assert.Equal(1, selection.Count);
        for (var i = 0; i < ids.Length; i++)
        {
            Assert.Equal(i == expected, selection.IsSelected(ids[i]));
        }
    }
}
