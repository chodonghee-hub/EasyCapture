using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class ClipboardCopyPlanTests
{
    // ---- Create ----

    [Theory]
    [InlineData(CopyMode.FileAndImage, true, true)]
    [InlineData(CopyMode.FileOnly, true, false)]
    [InlineData(CopyMode.ImageOnly, false, true)]
    [InlineData((CopyMode)(-1), true, true)]
    [InlineData((CopyMode)int.MaxValue, true, true)]
    public void Create_SelectedItems_UsesModeAndInputOrder(CopyMode mode, bool files, bool image)
    {
        Guid[] ids = [new(3, 0, 0, new byte[8]), new(1, 0, 0, new byte[8])];

        var plan = ClipboardCopyPlan.Create(mode, ids);

        Assert.Equal(files ? ids : Array.Empty<Guid>(), plan.FileIds);
        Assert.Equal(image ? (Guid?)ids[0] : null, plan.ImageId);
        Assert.False(plan.IsEmpty);
    }

    [Theory]
    [InlineData(CopyMode.FileAndImage)]
    [InlineData(CopyMode.FileOnly)]
    [InlineData(CopyMode.ImageOnly)]
    [InlineData((CopyMode)(-1))]
    public void Create_EmptySelection_ReturnsEmptyPlan(CopyMode mode)
    {
        var plan = ClipboardCopyPlan.Create(mode, []);

        Assert.Empty(plan.FileIds);
        Assert.Null(plan.ImageId);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Create_SingleItem_UsesSameIdForFileAndImage()
    {
        var plan = ClipboardCopyPlan.Create(CopyMode.FileAndImage, [Guid.Empty]);

        Assert.Equal(Guid.Empty, Assert.Single(plan.FileIds));
        Assert.Equal(Guid.Empty, plan.ImageId);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Create_DuplicateIds_PreservesInput()
    {
        var plan = ClipboardCopyPlan.Create(CopyMode.FileAndImage, [Guid.Empty, Guid.Empty]);

        Assert.Equal(new[] { Guid.Empty, Guid.Empty }, plan.FileIds);
        Assert.Equal(Guid.Empty, plan.ImageId);
    }

    [Fact]
    public void Create_NullSelection_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ClipboardCopyPlan.Create(CopyMode.FileAndImage, null!));
    }
}
