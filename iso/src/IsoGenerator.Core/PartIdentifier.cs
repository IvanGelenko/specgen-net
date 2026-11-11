namespace IsoGenerator.Core;
public static class PartIdentifier
{
    // Plant's Tag is the component identifier. Placeholder punctuation is not an identifier.
    public static string Read(Part part)
    {
        string tag=part.Properties.GetValueOrDefault("Tag","").Trim();
        return tag.Any(char.IsLetterOrDigit)?tag:"";
    }
}
