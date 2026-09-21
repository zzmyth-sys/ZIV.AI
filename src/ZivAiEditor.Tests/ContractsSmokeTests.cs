using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
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

    [Fact]
    public void InpaintRequest_Default_Steps_Is25()
    {
        var request = new InpaintRequest();
        Assert.Equal(25, request.Steps);
    }
}
