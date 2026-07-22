using System.Reflection;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsD3D11ComSignatureTests
{
    [Fact]
    public void TextureCopyProbePreservesNativeVoidCopyResourceSignature()
    {
        var contextType = typeof(SystemPlaybackWindowsOutputTextureCopyProbeCalls)
            .GetNestedType("ID3D11DeviceContext", BindingFlags.NonPublic);
        Assert.NotNull(contextType);

        var method = contextType.GetMethod(
            "CopyResource",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(
            MethodImplAttributes.PreserveSig,
            method.MethodImplementationFlags & MethodImplAttributes.PreserveSig);

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType));
    }
}
