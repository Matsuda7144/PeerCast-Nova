using System.Reflection;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsWasapiComSignatureTests
{
    private static readonly string[] AudioClientThroughGetService =
    [
        "Initialize",
        "GetBufferSize",
        "GetStreamLatency",
        "GetCurrentPadding",
        "IsFormatSupported",
        "GetMixFormat",
        "GetDevicePeriod",
        "Start",
        "Stop",
        "Reset",
        "SetEventHandle",
        "GetService",
    ];

    [Fact]
    public void ActivationAudioClientResetPreservesWindowsSdkVtableAndSignatures()
    {
        var type = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeActivation),
            "IAudioClientReset");
        AssertMethodOrder(type, AudioClientThroughGetService);
        AssertAudioClientSignatures(type, includeReset: true, includeGetService: true);
    }

    [Fact]
    public void StreamInitializationAudioClientPreservesOrderThroughSetEventHandle()
    {
        var type = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeStreamInitializer),
            "IAudioClient");
        AssertMethodOrder(type, AudioClientThroughGetService[..^1]);
        AssertAudioClientSignatures(type, includeReset: true, includeGetService: false);
    }

    [Fact]
    public void NativeBufferAudioClientPreservesOrderThroughStop()
    {
        var type = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeBufferCalls),
            "IAudioClient");
        AssertMethodOrder(type, AudioClientThroughGetService[..9]);
        AssertAudioClientSignatures(type, includeReset: false, includeGetService: false);
    }

    [Fact]
    public void RenderAndDeviceInterfacesPreserveWindowsSdkOrderAndSignatures()
    {
        var render = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeBufferCalls),
            "IAudioRenderClient");
        AssertMethodOrder(render, ["GetBuffer", "ReleaseBuffer"]);
        AssertSignature(
            GetMethod(render, "GetBuffer"),
            (typeof(uint), false),
            (typeof(nint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(render, "ReleaseBuffer"),
            (typeof(uint), false),
            (typeof(uint), false));

        var enumerator = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeActivation),
            "IMMDeviceEnumerator");
        AssertMethodOrder(enumerator, ["EnumAudioEndpoints", "GetDefaultAudioEndpoint"]);
        AssertSignature(
            GetMethod(enumerator, "EnumAudioEndpoints"),
            (typeof(uint), false),
            (typeof(uint), false),
            (typeof(nint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(enumerator, "GetDefaultAudioEndpoint"),
            (typeof(uint), false),
            (typeof(uint), false),
            (typeof(nint).MakeByRefType(), true));

        var device = GetNested(
            typeof(SystemPlaybackWindowsOutputNativeActivation),
            "IMMDevice");
        AssertMethodOrder(device, ["Activate"]);
        AssertSignature(
            GetMethod(device, "Activate"),
            (typeof(Guid).MakeByRefType(), false),
            (typeof(uint), false),
            (typeof(nint), false),
            (typeof(nint).MakeByRefType(), true));
    }

    private static void AssertAudioClientSignatures(
        Type type,
        bool includeReset,
        bool includeGetService)
    {
        AssertSignature(
            GetMethod(type, "Initialize"),
            (typeof(uint), false),
            (typeof(uint), false),
            (typeof(long), false),
            (typeof(long), false),
            (typeof(nint), false),
            (typeof(nint), false));
        AssertSignature(
            GetMethod(type, "GetBufferSize"),
            (typeof(uint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(type, "GetStreamLatency"),
            (typeof(long).MakeByRefType(), true));
        AssertSignature(
            GetMethod(type, "GetCurrentPadding"),
            (typeof(uint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(type, "IsFormatSupported"),
            (typeof(uint), false),
            (typeof(nint), false),
            (typeof(nint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(type, "GetMixFormat"),
            (typeof(nint).MakeByRefType(), true));
        AssertSignature(
            GetMethod(type, "GetDevicePeriod"),
            (typeof(long).MakeByRefType(), true),
            (typeof(long).MakeByRefType(), true));
        AssertSignature(GetMethod(type, "Start"));
        AssertSignature(GetMethod(type, "Stop"));
        if (includeReset)
        {
            AssertSignature(GetMethod(type, "Reset"));
            AssertSignature(
                GetMethod(type, "SetEventHandle"),
                (typeof(nint), false));
        }

        if (includeGetService)
        {
            AssertSignature(
                GetMethod(type, "GetService"),
                (typeof(Guid).MakeByRefType(), false),
                (typeof(nint).MakeByRefType(), true));
        }
    }

    private static void AssertSignature(
        MethodInfo method,
        params (Type Type, bool IsOut)[] parameters)
    {
        Assert.Equal(typeof(int), method.ReturnType);
        Assert.Equal(
            MethodImplAttributes.PreserveSig,
            method.MethodImplementationFlags & MethodImplAttributes.PreserveSig);
        Assert.Collection(
            method.GetParameters(),
            parameters.Select<(Type Type, bool IsOut), Action<ParameterInfo>>(
                expected =>
                    parameter =>
                    {
                        Assert.Equal(expected.Type, parameter.ParameterType);
                        Assert.Equal(expected.IsOut, parameter.IsOut);
                    }).ToArray());
    }

    private static void AssertMethodOrder(Type type, string[] expected) =>
        Assert.Equal(
            expected,
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(method => method.MetadataToken)
                .Select(method => method.Name));

    private static Type GetNested(Type owner, string name)
    {
        var type = owner.GetNestedType(name, BindingFlags.NonPublic);
        Assert.NotNull(type);
        return type;
    }

    private static MethodInfo GetMethod(Type type, string name)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }
}
