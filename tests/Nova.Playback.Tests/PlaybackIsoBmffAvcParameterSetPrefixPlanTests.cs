using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffAvcParameterSetPrefixPlanTests
{
    [Fact]
    public void Classify_ProjectsNalHeaderSemanticsWithoutCopying()
    {
        var frame = Frame(
            [2, 0x65, 0xAA, 1, 0x06, 1, 0x67],
            1,
            BaselineParameterSets());

        Assert.Equal(
            PlaybackIsoBmffAvcNalUnitClassificationResult.Classified,
            PlaybackIsoBmffAvcNalUnitClassifier.Classify(
                frame,
                out var classification));
        var parsed = Assert.IsType<PlaybackIsoBmffAvcNalUnitClassification>(
            classification);
        Assert.Same(frame, parsed.Frame);
        Assert.True(parsed.ContainsVcl);
        Assert.True(parsed.ContainsIdr);
        Assert.True(parsed.ContainsParameterSet);
        Assert.Collection(
            parsed.NalUnits,
            idr =>
            {
                Assert.Same(frame.NalUnits[0], idr.NalUnit);
                Assert.Equal((byte)3, idr.NalReferenceIdc);
                Assert.Equal((byte)5, idr.NalUnitType);
                Assert.True(idr.IsVcl);
                Assert.True(idr.IsIdr);
                Assert.False(idr.IsParameterSet);
            },
            sei =>
            {
                Assert.Equal((byte)0, sei.NalReferenceIdc);
                Assert.Equal((byte)6, sei.NalUnitType);
            },
            sps =>
            {
                Assert.Equal((byte)7, sps.NalUnitType);
                Assert.True(sps.IsParameterSet);
            });
    }

    [Fact]
    public void Classify_MissingFrameIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcNalUnitClassificationResult.MissingFrame,
            PlaybackIsoBmffAvcNalUnitClassifier.Classify(
                null,
                out var classification));
        Assert.Null(classification);
    }

    [Theory]
    [InlineData(0xE5)]
    [InlineData(0x60)]
    public void Classify_InvalidNalHeadersFailClosed(int header)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcNalUnitClassificationResult.InvalidNalUnit,
            PlaybackIsoBmffAvcNalUnitClassifier.Classify(
                Frame([(byte)1, (byte)header], 1, BaselineParameterSets()),
                out var classification));
        Assert.Null(classification);
    }

    [Fact]
    public void Build_ProjectsOrderedConfigurationAndSampleUnits()
    {
        var parameterSets = Array.AsReadOnly(
            new[]
            {
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Sequence,
                    [0x67, 0x42]),
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Picture,
                    [0x68]),
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.SequenceExtension,
                    [0x6D, 0x01]),
            });
        var classification = Classify(Frame([2, 0x65, 0xAA], 1, parameterSets));

        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.Planned,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                classification,
                out var plan));
        var parsed = Assert.IsType<PlaybackIsoBmffAvcParameterSetPrefixPlan>(
            plan);
        Assert.Same(classification, parsed.Classification);
        Assert.Equal((byte)1, parsed.NalUnitLengthBytes);
        Assert.Equal(3, parsed.ParameterSetCount);
        Assert.Equal(11, parsed.EncodedPayloadBytes);
        Assert.Collection(
            parsed.NalUnits,
            sps => Assert.Equal(
                PlaybackIsoBmffAvcPrefixedNalUnitSource.ConfigurationSequence,
                sps.Source),
            pps => Assert.Equal(
                PlaybackIsoBmffAvcPrefixedNalUnitSource.ConfigurationPicture,
                pps.Source),
            extension => Assert.Equal(
                PlaybackIsoBmffAvcPrefixedNalUnitSource
                    .ConfigurationSequenceExtension,
                extension.Source),
            sample =>
            {
                Assert.Equal(
                    PlaybackIsoBmffAvcPrefixedNalUnitSource.Sample,
                    sample.Source);
                Assert.Equal(new byte[] { 0x65, 0xAA },
                    sample.Payload.ToArray());
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_537)]
    public void Build_InvalidCountLimitRejectsBeforeInputAccess(int count)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.InvalidConfiguration,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                null,
                out var plan,
                new() { MaximumNalUnitCount = count }));
        Assert.Null(plan);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_194_305)]
    public void Build_InvalidPayloadLimitRejectsBeforeInputAccess(int bytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.InvalidConfiguration,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                null,
                out var plan,
                new() { MaximumPayloadBytes = bytes }));
        Assert.Null(plan);
    }

    [Fact]
    public void Build_MissingClassificationIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.MissingClassification,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                null,
                out var plan));
        Assert.Null(plan);
    }

    [Theory]
    [InlineData(PlaybackIsoBmffAvcParameterSetKind.Sequence, 0x68)]
    [InlineData(PlaybackIsoBmffAvcParameterSetKind.Picture, 0x67)]
    [InlineData(PlaybackIsoBmffAvcParameterSetKind.SequenceExtension, 0x6C)]
    [InlineData(PlaybackIsoBmffAvcParameterSetKind.Sequence, 0xE7)]
    public void Build_ParameterSetKindMustMatchNalHeader(
        PlaybackIsoBmffAvcParameterSetKind kind,
        int header)
    {
        var sets = new List<PlaybackIsoBmffAvcParameterSet>(
            BaselineParameterSets())
        {
            ParameterSet(kind, [(byte)header]),
        };

        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.InvalidParameterSet,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                Classify(Frame([1, 0x65], 1, sets.AsReadOnly())),
                out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Build_RequiresSequenceAndPictureParameterSets()
    {
        var onlySequence = Array.AsReadOnly(
            new[]
            {
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Sequence,
                    [0x67]),
            });

        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .MissingRequiredParameterSet,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                Classify(Frame([1, 0x65], 1, onlySequence)),
                out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Build_ParameterSetLengthMustFitConfiguredPrefixWidth()
    {
        var longSequence = new byte[256];
        longSequence[0] = 0x67;
        var sets = Array.AsReadOnly(
            new[]
            {
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Sequence,
                    longSequence),
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Picture,
                    [0x68]),
            });

        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult
                .ParameterSetLengthNotRepresentable,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                Classify(Frame([1, 0x65], 1, sets)),
                out var plan));
        Assert.Null(plan);
    }

    [Fact]
    public void Build_TotalNalUnitCountLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.NalUnitLimitExceeded,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                Classify(Frame(
                    [1, 0x65],
                    1,
                    BaselineParameterSets())),
                out var plan,
                new() { MaximumNalUnitCount = 2 }));
        Assert.Null(plan);
    }

    [Fact]
    public void Build_EncodedPayloadLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.PayloadLimitExceeded,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                Classify(Frame(
                    [1, 0x65],
                    1,
                    BaselineParameterSets())),
                out var plan,
                new() { MaximumPayloadBytes = 6 }));
        Assert.Null(plan);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MaterializeLengthPrefixed_PreservesConfiguredWidth(int width)
    {
        var plan = Plan((byte)width, [0x65, 0xAA]);

        Assert.Equal(
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .Materialized,
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer.Materialize(
                plan,
                out var payload));
        Assert.Same(plan, payload!.Plan);
        Assert.Equal((byte)width, payload.NalUnitLengthBytes);
        Assert.Equal(
            ExpectedLengthPrefixed(
                width,
                [0x67, 0x42],
                [0x68],
                [0x65, 0xAA]),
            payload.Payload.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_194_305)]
    public void MaterializeLengthPrefixed_InvalidLimitRejectsBeforeInputAccess(
        int bytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .InvalidConfiguration,
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer.Materialize(
                null,
                out var payload,
                new() { MaximumPayloadBytes = bytes }));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeLengthPrefixed_MissingPlanIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult.MissingPlan,
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer.Materialize(
                null,
                out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeLengthPrefixed_PayloadLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .PayloadLimitExceeded,
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer.Materialize(
                Plan(1, [0x65, 0xAA]),
                out var payload,
                new() { MaximumPayloadBytes = 7 }));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeAnnexB_UsesFourByteStartCodeForEveryUnit()
    {
        var plan = Plan(1, [0x65, 0xAA]);

        Assert.Equal(
            PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.Materialized,
            PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                plan,
                out var payload));
        Assert.Same(plan, payload!.Plan);
        Assert.Equal(4, payload.StartCodeLength);
        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 1, 0x67, 0x42,
                0, 0, 0, 1, 0x68,
                0, 0, 0, 1, 0x65, 0xAA,
            },
            payload.Payload.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_194_305)]
    public void MaterializeAnnexB_InvalidLimitRejectsBeforeInputAccess(int bytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                .InvalidConfiguration,
            PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                null,
                out var payload,
                new() { MaximumPayloadBytes = bytes }));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeAnnexB_MissingPlanIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.MissingPlan,
            PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                null,
                out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeAnnexB_PayloadLimitIsEnforced()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                .PayloadLimitExceeded,
            PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                Plan(1, [0x65, 0xAA]),
                out var payload,
                new() { MaximumPayloadBytes = 16 }));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeSampleAnnexB_UsesOnlyClassifiedSampleUnits()
    {
        var classification = Classify(Frame(
            [2, 0x65, 0xAA, 1, 0x06],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult.Materialized,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                classification,
                out var payload));
        Assert.Same(classification, payload!.Classification);
        Assert.Equal(4, payload.StartCodeLength);
        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 1, 0x65, 0xAA,
                0, 0, 0, 1, 0x06,
            },
            payload.Payload.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_194_305)]
    public void MaterializeSampleAnnexB_InvalidLimitRejectsBeforeInputAccess(
        int bytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .InvalidConfiguration,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                null,
                out var payload,
                new() { MaximumPayloadBytes = bytes }));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeSampleAnnexB_MissingClassificationIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .MissingClassification,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                null,
                out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void MaterializeSampleAnnexB_PayloadLimitIsEnforced()
    {
        var classification = Classify(Frame(
            [2, 0x65, 0xAA],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                .PayloadLimitExceeded,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                classification,
                out var payload,
                new() { MaximumPayloadBytes = 5 }));
        Assert.Null(payload);
    }

    [Fact]
    public void Select_ProjectsEachExplicitPayloadFormat()
    {
        var classification = Classify(Frame(
            [2, 0x65, 0xAA],
            1,
            BaselineParameterSets()));
        var plan = Plan(classification);
        Assert.Equal(
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                .Materialized,
            PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer.Materialize(
                plan,
                out var prefixedLength));
        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult.Materialized,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                classification,
                out var sampleAnnexB));
        Assert.Equal(
            PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult.Materialized,
            PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                plan,
                out var prefixedAnnexB));

        foreach (var format in Enum.GetValues<PlaybackIsoBmffAvcPayloadFormat>())
        {
            Assert.Equal(
                PlaybackIsoBmffAvcPayloadSelectionResult.Selected,
                PlaybackIsoBmffAvcPayloadSelector.Select(
                    classification,
                    format,
                    out var selection,
                    prefixedLength,
                    sampleAnnexB,
                    prefixedAnnexB));
            Assert.Same(classification, selection!.Classification);
            Assert.Equal(format, selection.Format);
            Assert.Equal(
                format is PlaybackIsoBmffAvcPayloadFormat
                    .ParameterSetPrefixedLengthPrefixed or
                    PlaybackIsoBmffAvcPayloadFormat
                        .ParameterSetPrefixedAnnexB,
                selection.IncludesConfigurationParameterSets);
            Assert.Equal(
                format is PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB or
                    PlaybackIsoBmffAvcPayloadFormat
                        .ParameterSetPrefixedAnnexB,
                selection.IsAnnexB);
            Assert.False(selection.Payload.IsEmpty);
        }
    }

    [Fact]
    public void Select_MissingClassificationIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcPayloadSelectionResult.MissingClassification,
            PlaybackIsoBmffAvcPayloadSelector.Select(
                null,
                PlaybackIsoBmffAvcPayloadFormat.OriginalLengthPrefixed,
                out var selection));
        Assert.Null(selection);
    }

    [Fact]
    public void Select_UnsupportedFormatIsDistinct()
    {
        var classification = Classify(Frame(
            [1, 0x65],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadSelectionResult.UnsupportedFormat,
            PlaybackIsoBmffAvcPayloadSelector.Select(
                classification,
                (PlaybackIsoBmffAvcPayloadFormat)99,
                out var selection));
        Assert.Null(selection);
    }

    [Theory]
    [InlineData(
        PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedLengthPrefixed)]
    [InlineData(PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB)]
    [InlineData(PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedAnnexB)]
    public void Select_RequiredMaterializedPayloadMustBePresent(
        PlaybackIsoBmffAvcPayloadFormat format)
    {
        var classification = Classify(Frame(
            [1, 0x65],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadSelectionResult.MissingPayload,
            PlaybackIsoBmffAvcPayloadSelector.Select(
                classification,
                format,
                out var selection));
        Assert.Null(selection);
    }

    [Fact]
    public void Select_RejectsPayloadFromDifferentClassification()
    {
        var expected = Classify(Frame(
            [1, 0x65],
            1,
            BaselineParameterSets()));
        var foreign = Classify(Frame(
            [1, 0x61],
            1,
            BaselineParameterSets()));
        Assert.Equal(
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult.Materialized,
            PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer.Materialize(
                foreign,
                out var foreignPayload));

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadSelectionResult.SourceMismatch,
            PlaybackIsoBmffAvcPayloadSelector.Select(
                expected,
                PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB,
                out var selection,
                sampleAnnexBPayload: foreignPayload));
        Assert.Null(selection);
    }

    [Theory]
    [InlineData(false, false,
        PlaybackIsoBmffAvcPayloadFormat.OriginalLengthPrefixed)]
    [InlineData(false, true,
        PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedLengthPrefixed)]
    [InlineData(true, false,
        PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB)]
    [InlineData(true, true,
        PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedAnnexB)]
    public void Resolve_MapsExplicitRequirementsToOneFormat(
        bool requireAnnexB,
        bool includeParameterSets,
        PlaybackIsoBmffAvcPayloadFormat expected)
    {
        var requirements = new PlaybackIsoBmffAvcPayloadRequirements
        {
            RequireAnnexB = requireAnnexB,
            IncludeConfigurationParameterSets = includeParameterSets,
        };

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadRequirementsResolveResult.Resolved,
            PlaybackIsoBmffAvcPayloadRequirementsResolver.Resolve(
                requirements,
                out var resolution));
        Assert.Same(requirements, resolution!.Requirements);
        Assert.Equal(expected, resolution.Format);
    }

    [Fact]
    public void Resolve_MissingRequirementsIsDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffAvcPayloadRequirementsResolveResult
                .MissingRequirements,
            PlaybackIsoBmffAvcPayloadRequirementsResolver.Resolve(
                null,
                out var resolution));
        Assert.Null(resolution);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Compose_BuildsOnlyArtifactsRequiredByResolution(
        bool requireAnnexB,
        bool includeParameterSets)
    {
        var classification = Classify(Frame(
            [2, 0x65, 0xAA],
            1,
            BaselineParameterSets()));
        var resolution = Resolve(requireAnnexB, includeParameterSets);

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.Composed,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                classification,
                resolution,
                out var composition));
        var composed = Assert.IsType<PlaybackIsoBmffAvcPayloadComposition>(
            composition);
        Assert.Same(resolution, composed.Resolution);
        Assert.Same(classification, composed.Selection.Classification);
        Assert.Equal(resolution.Format, composed.Selection.Format);
        Assert.Equal(includeParameterSets, composed.PrefixPlan is not null);
        Assert.Equal(
            !requireAnnexB && includeParameterSets,
            composed.PrefixedLengthPayload is not null);
        Assert.Equal(
            requireAnnexB && !includeParameterSets,
            composed.SampleAnnexBPayload is not null);
        Assert.Equal(
            requireAnnexB && includeParameterSets,
            composed.PrefixedAnnexBPayload is not null);
    }

    [Theory]
    [InlineData(0, 4_194_304)]
    [InlineData(65_537, 4_194_304)]
    [InlineData(4_096, 0)]
    [InlineData(4_096, 4_194_305)]
    public void Compose_InvalidLimitsRejectBeforeInputAccess(
        int count,
        int bytes)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.InvalidConfiguration,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                null,
                null,
                out var composition,
                new()
                {
                    MaximumNalUnitCount = count,
                    MaximumPayloadBytes = bytes,
                }));
        Assert.Null(composition);
    }

    [Fact]
    public void Compose_MissingInputsAreDistinct()
    {
        var classification = Classify(Frame(
            [1, 0x65],
            1,
            BaselineParameterSets()));
        var resolution = Resolve(false, false);

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.MissingClassification,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                null,
                resolution,
                out var missingClassification));
        Assert.Null(missingClassification);
        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.MissingResolution,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                classification,
                null,
                out var missingResolution));
        Assert.Null(missingResolution);
    }

    [Fact]
    public void Compose_PrefixPlanFailureIsDistinct()
    {
        var classification = Classify(Frame(
            [1, 0x65],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.PrefixPlanFailed,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                classification,
                Resolve(false, true),
                out var composition,
                new() { MaximumNalUnitCount = 2 }));
        Assert.Null(composition);
    }

    [Fact]
    public void Compose_OriginalPayloadLimitIsEnforced()
    {
        var classification = Classify(Frame(
            [2, 0x65, 0xAA],
            1,
            BaselineParameterSets()));

        Assert.Equal(
            PlaybackIsoBmffAvcPayloadComposeResult.PayloadLimitExceeded,
            PlaybackIsoBmffAvcPayloadComposer.Compose(
                classification,
                Resolve(false, false),
                out var composition,
                new() { MaximumPayloadBytes = 2 }));
        Assert.Null(composition);
    }

    private static PlaybackIsoBmffAvcPayloadRequirementsResolution Resolve(
        bool requireAnnexB,
        bool includeParameterSets)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcPayloadRequirementsResolveResult.Resolved,
            PlaybackIsoBmffAvcPayloadRequirementsResolver.Resolve(
                new()
                {
                    RequireAnnexB = requireAnnexB,
                    IncludeConfigurationParameterSets = includeParameterSets,
                },
                out var resolution));
        return Assert.IsType<PlaybackIsoBmffAvcPayloadRequirementsResolution>(
            resolution);
    }

    private static PlaybackIsoBmffAvcParameterSetPrefixPlan Plan(
        byte lengthBytes,
        byte[] samplePayload)
    {
        var encodedSample = ExpectedLengthPrefixed(lengthBytes, samplePayload);
        var classification = Classify(Frame(
            encodedSample,
            lengthBytes,
            BaselineParameterSets()));
        return Plan(classification);
    }

    private static PlaybackIsoBmffAvcParameterSetPrefixPlan Plan(
        PlaybackIsoBmffAvcNalUnitClassification classification)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcParameterSetPrefixPlanResult.Planned,
            PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                classification,
                out var plan));
        return Assert.IsType<PlaybackIsoBmffAvcParameterSetPrefixPlan>(plan);
    }

    private static byte[] ExpectedLengthPrefixed(
        int lengthBytes,
        params byte[][] nalUnits)
    {
        var result = new List<byte>();
        foreach (var unit in nalUnits)
        {
            var length = checked((uint)unit.Length);
            for (var index = lengthBytes - 1; index >= 0; index--)
            {
                result.Add((byte)(length >> (index * 8)));
            }

            result.AddRange(unit);
        }

        return [.. result];
    }

    private static PlaybackIsoBmffAvcNalUnitClassification Classify(
        PlaybackIsoBmffAvcSampleFrame frame)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcNalUnitClassificationResult.Classified,
            PlaybackIsoBmffAvcNalUnitClassifier.Classify(
                frame,
                out var classification));
        return Assert.IsType<PlaybackIsoBmffAvcNalUnitClassification>(
            classification);
    }

    private static PlaybackIsoBmffAvcSampleFrame Frame(
        byte[] payload,
        byte lengthBytes,
        IReadOnlyList<PlaybackIsoBmffAvcParameterSet> parameterSets)
    {
        Assert.Equal(
            PlaybackIsoBmffAvcSampleFramingResult.Framed,
            PlaybackIsoBmffAvcSampleFraming.Frame(
                Candidate(payload),
                Configuration(lengthBytes, parameterSets),
                out var frame));
        return Assert.IsType<PlaybackIsoBmffAvcSampleFrame>(frame);
    }

    private static ReadOnlyCollection<PlaybackIsoBmffAvcParameterSet>
        BaselineParameterSets() => Array.AsReadOnly(
            new[]
            {
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Sequence,
                    [0x67, 0x42]),
                ParameterSet(
                    PlaybackIsoBmffAvcParameterSetKind.Picture,
                    [0x68]),
            });

    private static PlaybackIsoBmffAvcParameterSet ParameterSet(
        PlaybackIsoBmffAvcParameterSetKind kind,
        byte[] payload) => new(kind, payload);

    private static PlaybackIsoBmffEditedSamplePacketCandidate Candidate(
        byte[] payload)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(
            10,
            2,
            12,
            0,
            false);
        var appearances = Array.AsReadOnly(
            new[]
            {
                new PlaybackIsoBmffSampleEditMovieOverlap(
                    0,
                    10,
                    12,
                    1,
                    true),
            });
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(sample, appearances));
        var editedRange = new PlaybackIsoBmffEditedSampleByteRange(
            admission,
            new(8, checked((uint)payload.Length),
                checked((ulong)payload.Length + 8)),
            new(0, 1, 1, 0, 1, 1));
        var mediaData = new PlaybackIsoBmffEditedSampleMediaData(
            editedRange,
            Location(0x6D646174));
        return new(
            new PlaybackIsoBmffOwnedEditedSamplePayload(mediaData, payload),
            90_000);
    }

    private static PlaybackIsoBmffAvcDecoderConfiguration Configuration(
        byte lengthBytes,
        IReadOnlyList<PlaybackIsoBmffAvcParameterSet> parameterSets)
    {
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            Location(0x61766331),
            PlaybackIsoBmffSampleEntryCodecCandidate.Avc1,
            PlaybackIsoBmffHandlerKind.Video);
        var visual = new PlaybackIsoBmffVisualSampleEntry(
            selection,
            1,
            640,
            360,
            0,
            0,
            1,
            [],
            0x18,
            Array.AsReadOnly(Array.Empty<PlaybackIsoBmffBoxLocation>()));
        return new(
            visual,
            Location(0x61766343),
            66,
            0,
            30,
            lengthBytes,
            null,
            null,
            null,
            Array.AsReadOnly(parameterSets.ToArray()),
            [1]);
    }

    private static PlaybackIsoBmffBoxLocation Location(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
