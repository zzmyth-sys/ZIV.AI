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

    [Fact]
    public void ILlmClient_IS_ASSIGNABLE_To_IDisposable()
    {
        // FROZEN Step 5.1: ILlmClient : IDisposable with CompleteAsync(system, user, ct).
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(ILlmClient)));
        Assert.NotNull(typeof(ILlmClient).GetMethod(
            nameof(ILlmClient.CompleteAsync),
            new[] { typeof(string), typeof(string), typeof(CancellationToken) }));
    }
}
