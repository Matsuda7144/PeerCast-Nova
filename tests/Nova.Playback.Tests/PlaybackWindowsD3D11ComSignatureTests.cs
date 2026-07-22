using System.Reflection;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsD3D11ComSignatureTests
{
    private static readonly string[] CriticalContextMethodOrder =
    [
        "OMSetRenderTargets",
        "OMSetRenderTargetsAndUnorderedAccessViews",
        "OMSetBlendState",
        "OMSetDepthStencilState",
        "SOSetTargets",
        "DrawAuto",
        "DrawIndexedInstancedIndirect",
        "DrawInstancedIndirect",
        "Dispatch",
        "DispatchIndirect",
        "RSSetState",
        "RSSetViewports",
        "RSSetScissorRects",
        "CopySubresourceRegion",
        "CopyResource",
    ];

    [Fact]
    public void TextureCopyProbePreservesOfficialContextOrderAndCopySignature()
    {
        var contextType = GetContextType(typeof(SystemPlaybackWindowsOutputTextureCopyProbeCalls));
        AssertCriticalOrder(contextType, includeUpdateSubresource: false);

        var method = GetMethod(contextType, "CopyResource");
        Assert.Equal(typeof(void), method.ReturnType);
        AssertPreserveSig(method);
        Assert.Collection(
            method.GetParameters(),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType));
    }

    [Fact]
    public void ProductionOutputPreservesOfficialContextOrderAndUpdateSignature()
    {
        var contextType = GetContextType(typeof(SystemPlaybackWindowsOutputNativeBufferCalls));
        AssertCriticalOrder(contextType, includeUpdateSubresource: true);

        var update = GetMethod(contextType, "UpdateSubresource");
        Assert.Equal(typeof(void), update.ReturnType);
        AssertPreserveSig(update);
        Assert.Collection(
            update.GetParameters(),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(uint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(nint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(uint), parameter.ParameterType),
            parameter => Assert.Equal(typeof(uint), parameter.ParameterType));

        var renderTargets = GetMethod(contextType, "OMSetRenderTargets");
        Assert.Equal(typeof(void), renderTargets.ReturnType);
        AssertPreserveSig(renderTargets);
    }

    private static Type GetContextType(Type owner)
    {
        var contextType = owner.GetNestedType("ID3D11DeviceContext", BindingFlags.NonPublic);
        Assert.NotNull(contextType);
        return contextType;
    }

    private static MethodInfo GetMethod(Type contextType, string name)
    {
        var method = contextType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    private static void AssertCriticalOrder(Type contextType, bool includeUpdateSubresource)
    {
        var methods = contextType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(method => method.MetadataToken)
            .Select(method => method.Name)
            .ToArray();
        var start = Array.IndexOf(methods, CriticalContextMethodOrder[0]);
        Assert.True(start >= 0);

        var expected = includeUpdateSubresource
            ? [.. CriticalContextMethodOrder, "UpdateSubresource"]
            : CriticalContextMethodOrder;
        Assert.Equal(expected, methods.Skip(start).Take(expected.Length));
    }

    private static void AssertPreserveSig(MethodInfo method) =>
        Assert.Equal(
            MethodImplAttributes.PreserveSig,
            method.MethodImplementationFlags & MethodImplAttributes.PreserveSig);
}
