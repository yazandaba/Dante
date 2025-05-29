using Dante.Generators;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Z3;

namespace Dante;

internal class FlowCaptureReferenceEntry
{
    public required CaptureId CaptureId { get; init; }
    public FlowCaptureReferenceEntry? ReferencedFlowCapture { get; init; }
}

internal class FlowCaptureEntry : FlowCaptureReferenceEntry
{
    public required Expr Value { get; init; }
    public required IOperation CapturedValue { get; init; }
}

/// <summary>
///     table that will get used to track captured intermediate values (which are generated from nullable expressions in
///     C#)
/// </summary>
/// <remarks>
///     instance of table will get used accross single or multiple basic blocks so it instance of it must be created
///     per CFG generator i.e <see cref="FunctionGenerator" />
/// </remarks>
internal class FlowCaptureTable
{
    private readonly Dictionary<CaptureId, FlowCaptureReferenceEntry> _flowCaptureTable = [];

    /// <summary>
    ///     track flow capture if and only if the 'value' of <paramref name="flowCaptureOperation" /> is reference
    ///     to another instance of <see cref="IFlowCaptureOperation" /> or <see cref="IFlowCaptureReferenceOperation" />
    /// </summary>
    /// <param name="flowCaptureOperation">intermediate or local value captured in region of control flow graph</param>
    public void TryTrack(IFlowCaptureOperation flowCaptureOperation)
    {
        var captureId = flowCaptureOperation.Id;
        var capturedValue = flowCaptureOperation.Value;
        if (capturedValue is IFlowCaptureReferenceOperation flowCaptureReferenceOperation)
        {
            var capturedValueCaptureId = flowCaptureReferenceOperation.Id;
            _flowCaptureTable.Should().ContainKey(capturedValueCaptureId, "referenced flow capture " +
                                                                          "must be evaluated before being referenced");
            var referencedEntry = _flowCaptureTable[capturedValueCaptureId];
            _flowCaptureTable.TryAdd(captureId, new FlowCaptureReferenceEntry
            {
                CaptureId = capturedValueCaptureId,
                ReferencedFlowCapture = referencedEntry
            });
        }
    }


    public void Bind(IFlowCaptureOperation flowCaptureOperation, Expr generatedCapturedValue)
    {
        var captureId = flowCaptureOperation.Id;
        _flowCaptureTable[captureId] = new FlowCaptureEntry
        {
            CaptureId = captureId,
            ReferencedFlowCapture = default,
            CapturedValue = flowCaptureOperation.Value,
            Value = generatedCapturedValue
        };
    }

    private FlowCaptureEntry CapturedEntry(IFlowCaptureReferenceOperation flowCaptureReferenceOperation)
    {
        var referencedCaptureId = flowCaptureReferenceOperation.Id;
        _flowCaptureTable.Should().ContainKey(referencedCaptureId, "referenced flow capture was not added or bind " +
                                                                   "before, but it is being fetched");

        var referencedCapture = _flowCaptureTable[flowCaptureReferenceOperation.Id];
        while (referencedCapture.ReferencedFlowCapture is not null)
        {
            referencedCapture = referencedCapture.ReferencedFlowCapture;
        }

        if (referencedCapture is FlowCaptureEntry flowCaptureEntry)
        {
            return flowCaptureEntry;
        }

        throw new InvalidOperationException(
            $"fetching captured expression starting from capture id '{referencedCaptureId}' is invalid , as there is " +
            $"no such entry");
    }

    public Expr Fetch(IFlowCaptureReferenceOperation flowCaptureReferenceOperation)
    {
        return CapturedEntry(flowCaptureReferenceOperation).Value;
    }

    public IOperation Captured(IFlowCaptureReferenceOperation flowCaptureReferenceOperation)
    {
        return CapturedEntry(flowCaptureReferenceOperation).CapturedValue;
    }
}