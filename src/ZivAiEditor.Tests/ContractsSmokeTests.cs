using ZivAiEditor.Contracts.Imaging;
using Xunit;

namespace ZivAiEditor.Tests;

public class ContractsSmokeTests
{
    [Fact]
    public void MaskSpec_Default_IsBinary()
    {
        var mask = new MaskSpec();
        Assert.True(mask.IsBinary);
    }
}
