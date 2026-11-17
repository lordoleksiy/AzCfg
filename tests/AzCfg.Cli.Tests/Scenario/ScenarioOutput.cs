namespace AzCfg.Cli.Tests.Scenario;

/// <summary>Checks the human (non-JSON) output of scenario commands.</summary>
public static class ScenarioOutput
{
    /// <summary>Asserts that one line of <paramref name="output"/> (e.g. a table row) contains every part, in order.</summary>
    public static void AssertLine(string output, params string[] parts) =>
        Assert.True(
            output.Split('\n').Any(line => ContainsInOrder(line, parts)),
            $"No line contains [{string.Join(", ", parts)}] in order:\n{output}");

    private static bool ContainsInOrder(string line, IEnumerable<string> parts)
    {
        var index = 0;
        foreach (var part in parts)
        {
            index = line.IndexOf(part, index, StringComparison.Ordinal);
            if (index < 0)
                return false;
            index += part.Length;
        }

        return true;
    }
}
