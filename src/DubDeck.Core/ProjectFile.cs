namespace DubDeck.Core;

public static class ProjectFile
{
    public const string Extension = ".dubdeck";

    public static bool IsProjectPath(string path) =>
        Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
}
