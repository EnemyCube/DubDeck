namespace DubDeck.Core;

public static class ProjectFile
{
    public const string Extension = ".dubdeck";
    public const string LegacyExtension = ".videosync";
    public const string PreviousExtension = ".dubsync";

    public static bool IsProjectPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(Extension, StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(LegacyExtension, StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(PreviousExtension, StringComparison.OrdinalIgnoreCase);
    }
}
