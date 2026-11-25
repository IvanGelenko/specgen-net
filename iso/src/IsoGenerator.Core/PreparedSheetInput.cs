namespace IsoGenerator.Core;

// Validation and model topology depend on the fragment, not on paper margins or pipe extensions.
internal sealed record PreparedSheetInput(PipelineSnapshot Snapshot,ValidationResult Validation,PipelineTopology Topology,
    IReadOnlySet<string>? ValveKitExclusions)
{
    public static PreparedSheetInput Create(PipelineSnapshot snapshot,ValidationResult? validation=null,
        IReadOnlySet<string>? valveKitExclusions=null,IReadOnlySet<string>? cappedVentDrainPipes=null)
    {
        validation??=SnapshotValidation.Validate(snapshot);
        if(validation.Errors.Count>0)throw new InvalidDataException(string.Join("\n",validation.Errors));
        return new(snapshot,validation,PipelineTopology.Build(snapshot,cappedVentDrainPipes),valveKitExclusions);
    }
}
