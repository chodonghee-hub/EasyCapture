using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PanelCommandsTests
{
    // ---- Evaluate ----

    [Theory]
    [InlineData(0, 0, false, false, true)]
    [InlineData(1, 0, true, true, true)]
    [InlineData(1, 1, true, true, false)]
    [InlineData(2, 0, true, false, true)]
    [InlineData(2, 1, true, false, true)]
    [InlineData(2, 2, true, false, false)]
    public void Evaluate_SelectionComposition_ReturnsCommandState(
        int count, int pinned, bool any, bool single, bool pinTarget)
    {
        var selected = Enumerable.Range(0, count)
            .Select(i => new CaptureItem { Pinned = i < pinned }).ToArray();

        var state = PanelCommands.Evaluate(selected);

        Assert.Equal(new PanelCommandState(any, any, any, single, single, single, pinTarget), state);
    }

    [Fact]
    public void Evaluate_NullSelection_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PanelCommands.Evaluate(null!));
    }

    // ---- BuildDeleteMessage ----

    [Fact]
    public void BuildDeleteMessage_EmptySelection_ReturnsEmptyString()
    {
        Assert.Equal("", PanelCommands.BuildDeleteMessage([]));
    }

    [Theory]
    [InlineData(false, "'회의 자료'을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다.")]
    [InlineData(true, "'회의 자료'을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다. 고정된 항목입니다.")]
    public void BuildDeleteMessage_SingleItem_IncludesNameAndPinNote(bool pinned, string expected)
    {
        Assert.Equal(expected, PanelCommands.BuildDeleteMessage(
            [new CaptureItem { Name = "회의 자료", Pinned = pinned }]));
    }

    [Theory]
    [InlineData(0, "선택한 2개을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다.")]
    [InlineData(1, "선택한 2개을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다. 고정 1개 포함.")]
    [InlineData(2, "선택한 2개을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다. 고정 2개 포함.")]
    public void BuildDeleteMessage_MultipleItems_IncludesCounts(int pinned, string expected)
    {
        var selected = Enumerable.Range(0, 2)
            .Select(i => new CaptureItem { Name = "항목", Pinned = i < pinned }).ToArray();

        Assert.Equal(expected, PanelCommands.BuildDeleteMessage(selected));
    }

    [Fact]
    public void BuildDeleteMessage_NullSelection_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PanelCommands.BuildDeleteMessage(null!));
    }
}
