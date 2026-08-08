using System.IO;
using System.Reflection;

namespace WinQuickSwitch.Features.Legal;

internal enum LegalDocumentKind
{
    Eula,
    PrivacyPolicy,
}

internal sealed record LegalDocument(string Title, string DisplayText);

internal static class LegalDocuments
{
    private const string EulaResource = "WinQuickSwitch.Legal.EULA.md";
    private const string PrivacyResource =
        "WinQuickSwitch.Legal.PRIVACY_POLICY.md";

    public static LegalDocument Get(LegalDocumentKind kind) => kind switch
    {
        LegalDocumentKind.Eula => new(
            "End User License Agreement",
            ToDisplayText(ReadResource(EulaResource))),
        LegalDocumentKind.PrivacyPolicy => new(
            "Privacy Policy",
            ToDisplayText(ReadResource(PrivacyResource))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string ToDisplayText(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].TrimStart();

            if (trimmed.StartsWith('#'))
            {
                lines[index] = trimmed.TrimStart('#', ' ');
            }
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                lines[index] = trimmed[2..];
            }
        }

        return string.Join(Environment.NewLine, lines)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string ReadResource(string resourceName)
    {
        Assembly assembly = typeof(LegalDocuments).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded legal document '{resourceName}' is missing.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
