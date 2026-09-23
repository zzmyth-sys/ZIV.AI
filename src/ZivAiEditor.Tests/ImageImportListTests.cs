using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.3 image-import list tests (pure logic, no UI / GPU).</summary>
public class ImageImportListTests
{
    [Fact]
    public void Starts_Empty()
    {
        var list = new ImageImportList();

        Assert.Equal(0, list.Count);
        Assert.Empty(list.Paths);
    }

    [Fact]
    public void AddRange_One_Reports_Empty_To_One_Transition()
    {
        var list = new ImageImportList();
        ImageImportChangedEventArgs? change = null;
        list.Changed += (_, e) => change = e;

        var added = list.AddRange(new[] { @"C:\img\a.png" });

        Assert.Equal(1, added);
        Assert.Equal(1, list.Count);
        Assert.NotNull(change);
        Assert.Equal(0, change!.CountBefore);
        Assert.Equal(1, change.CountAfter);
    }

    [Fact]
    public void AddRange_Batch_Raises_Changed_Once()
    {
        var list = new ImageImportList();
        var changes = 0;
        ImageImportChangedEventArgs? last = null;
        list.Changed += (_, e) => { changes++; last = e; };

        var added = list.AddRange(new[] { @"C:\img\a.png", @"C:\img\b.png", @"C:\img\c.png" });

        Assert.Equal(3, added);
        Assert.Equal(1, changes);
        Assert.Equal(0, last!.CountBefore);
        Assert.Equal(3, last.CountAfter);
    }

    [Fact]
    public void AddRange_Skips_Duplicates_Case_Insensitive()
    {
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\A.PNG" });

        var added = list.AddRange(new[] { @"c:\IMG\a.png", @"C:\img\b.png" });

        Assert.Equal(1, added);
        Assert.Equal(2, list.Count);
        Assert.True(list.Contains(@"C:\IMG\A.png"));
    }

    [Fact]
    public void AddRange_With_Only_Duplicates_Does_Not_Raise()
    {
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\a.png" });

        var changes = 0;
        list.Changed += (_, _) => changes++;
        var added = list.AddRange(new[] { @"C:\img\a.png" });

        Assert.Equal(0, added);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AddRange_Skips_Blanks()
    {
        var list = new ImageImportList();

        var added = list.AddRange(new[] { "", "   ", @"C:\img\a.png" });

        Assert.Equal(1, added);
        Assert.Single(list.Paths);
    }

    [Fact]
    public void RemoveAt_Removes_And_Reports_Counts()
    {
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\a.png", @"C:\img\b.png" });
        ImageImportChangedEventArgs? change = null;
        list.Changed += (_, e) => change = e;

        var removed = list.RemoveAt(0);

        Assert.True(removed);
        Assert.Equal(1, list.Count);
        Assert.Equal(@"C:\img\b.png", list.Paths[0]);
        Assert.Equal(2, change!.CountBefore);
        Assert.Equal(1, change.CountAfter);
    }

    [Fact]
    public void RemoveAt_Out_Of_Range_Returns_False_And_Does_Not_Raise()
    {
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\a.png" });
        var changes = 0;
        list.Changed += (_, _) => changes++;

        Assert.False(list.RemoveAt(5));
        Assert.False(list.RemoveAt(-1));
        Assert.Equal(1, list.Count);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Clear_Removes_All_And_Raises_Once()
    {
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\a.png", @"C:\img\b.png" });
        var changes = 0;
        ImageImportChangedEventArgs? change = null;
        list.Changed += (_, e) => { changes++; change = e; };

        list.Clear();

        Assert.Equal(0, list.Count);
        Assert.Equal(1, changes);
        Assert.Equal(2, change!.CountBefore);
        Assert.Equal(0, change.CountAfter);
    }

    [Fact]
    public void Clear_On_Empty_Is_NoOp()
    {
        var list = new ImageImportList();
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Clear();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Two_To_One_Removal_Does_Not_Look_Like_A_Promotion()
    {
        // The App layer promotes to main image only when CountBefore==0 && CountAfter==1.
        var list = new ImageImportList();
        list.AddRange(new[] { @"C:\img\a.png", @"C:\img\b.png" });
        ImageImportChangedEventArgs? change = null;
        list.Changed += (_, e) => change = e;

        list.RemoveAt(1);

        Assert.NotNull(change);
        Assert.Equal(2, change!.CountBefore);
        Assert.Equal(1, change.CountAfter);
        Assert.False(change.CountBefore == 0 && change.CountAfter == 1);
    }
}