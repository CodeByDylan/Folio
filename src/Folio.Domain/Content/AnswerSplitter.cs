namespace Folio.Domain.Content;

/// <summary>One answer lifted out of a Q&amp;A file.</summary>
/// <param name="Id">The heading's text, which names the entry it answers.</param>
/// <param name="Body">Everything until the next heading of the same level.</param>
internal sealed record Answer(string Id, string Body);

/// <summary>Splits a Q&amp;A file into its answers on the level-two headings that open them.</summary>
internal static class AnswerSplitter
{
    /// <summary>Splits a file, in document order.</summary>
    /// <param name="source">The authored markdown.</param>
    /// <param name="preamble">Anything before the first heading, which answers nothing.</param>
    /// <returns>The answers, in the order they appear.</returns>
    public static IReadOnlyList<Answer> Split(string source, out string preamble)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        List<Answer> answers = [];
        List<string> body = [];
        List<string> before = [];
        string? current = null;
        bool fenced = false;

        foreach (string line in lines)
        {
            // A '##' inside a fenced block is code, not a heading.
            if (line.StartsWith("```", StringComparison.Ordinal)
                || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }

            if (!fenced && Heading(line) is { } heading)
            {
                if (current is not null)
                {
                    answers.Add(new Answer(current, Join(body)));
                }

                current = heading;
                body.Clear();
                continue;
            }

            (current is null ? before : body).Add(line);
        }

        if (current is not null)
        {
            answers.Add(new Answer(current, Join(body)));
        }

        preamble = Join(before);
        return answers;
    }

    /// <summary>Reads a level-two ATX heading, which is the only level that opens an answer.</summary>
    private static string? Heading(string line)
    {
        if (!line.StartsWith("## ", StringComparison.Ordinal))
        {
            return null;
        }

        string text = line[3..].Trim().TrimEnd('#').Trim();

        return text.Length > 0 ? text : null;
    }

    private static string Join(IEnumerable<string> lines) => string.Join('\n', lines).Trim();
}
