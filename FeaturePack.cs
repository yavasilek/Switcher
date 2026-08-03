using System.Text;

namespace Switcher;

internal enum ContextLayout
{
    Automatic,
    Russian,
    English,
}

internal sealed record ApplicationLayoutRule(string ProcessName, ContextLayout Layout)
{
    public static List<ApplicationLayoutRule> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, ApplicationLayoutRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0)
            {
                continue;
            }

            var process = Path.GetFileNameWithoutExtension(parts[0]);
            var layout = parts[1].Trim().ToUpperInvariant() switch
            {
                "RU" or "RUSSIAN" or "РУ" => ContextLayout.Russian,
                "EN" or "ENGLISH" or "АНГЛ" => ContextLayout.English,
                _ => ContextLayout.Automatic,
            };
            result[process] = new ApplicationLayoutRule(process, layout);
        }

        return result.Values.OrderBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public override string ToString() => $"{ProcessName}={(Layout == ContextLayout.Russian ? "RU" : Layout == ContextLayout.English ? "EN" : "AUTO")}";
}

internal sealed record SnippetRule(string Trigger, string Replacement)
{
    public bool Enabled { get; set; } = true;
    public static List<SnippetRule> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, SnippetRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var parts = raw.Split("=>", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IsValidTrigger(parts[0]))
            {
                continue;
            }

            var replacement = Unescape(parts[1]);
            if (replacement.Length is > 0 and <= 4000)
            {
                result[parts[0]] = new SnippetRule(parts[0], replacement);
            }
        }

        return result.Values.OrderBy(rule => rule.Trigger, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsValidTrigger(string value) => value.Length is > 0 and <= 64 && !value.Any(char.IsWhiteSpace);

    public override string ToString() => $"{Trigger} => {Escape(Replacement)}";

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            result.Append(value[++i] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                var ch => ch,
            });
        }

        return result.ToString();
    }
}

internal static class FeaturePackSelfTests
{
    public static void Run(List<string> failures)
    {
        var rules = ApplicationLayoutRule.Parse(["chrome=EN", "telegram = RU", "bad"]);
        if (rules.Count != 2 || rules.Single(rule => rule.ProcessName == "chrome").Layout != ContextLayout.English)
        {
            failures.Add("FEATURE PACK: application layout rules parsing failed");
        }

        var snippets = SnippetRule.Parse(["sig => Best regards,\\nAlex", "broken"]);
        if (snippets.Count != 1 || snippets[0].Replacement != "Best regards,\nAlex")
        {
            failures.Add("FEATURE PACK: snippet parsing failed");
        }
    }
}
