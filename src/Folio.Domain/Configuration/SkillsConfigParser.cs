using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>Parses <c>sections/&lt;id&gt;.toml</c> for a skills section.</summary>
internal static class SkillsConfigParser
{
    private static readonly HashSet<string> CategoryKeys = new(StringComparer.Ordinal) { "id" };

    private static readonly HashSet<string> SkillKeys = new(StringComparer.Ordinal) { "id", "level" };

    private static readonly HashSet<string> Arrays =
        new(StringComparer.Ordinal) { "categories", "categories.skills" };

    /// <summary>Reads one skills section's data.</summary>
    /// <param name="files">The central file set.</param>
    /// <param name="folioRoot">The path of the <c>.folio</c> directory.</param>
    /// <param name="id">The section id.</param>
    /// <param name="sink">Where problems are reported.</param>
    /// <returns>The skills section, or <see langword="null" /> if the file is missing or unparseable.</returns>
    public static SkillsSectionEntry? Read(FileSet files, string folioRoot, string id, DiagnosticSink sink)
    {
        if (SectionDataFile.Read(files, folioRoot, id, SectionDataFile.None, SectionDataFile.None, Arrays, sink) is not { } data)
        {
            return null;
        }

        return new SkillsSectionEntry(id, Categories(data.Document, id, data.Sink));
    }

    private static IReadOnlyList<SkillCategoryEntry> Categories(
        TomlDocumentReader document,
        string id,
        DiagnosticSink sink)
    {
        // Sub-table arrays are indexed by path alone, so a skill belongs to the category above it.
        List<TomlTableReader> declared = [.. Ordered(document.TableArray("categories"))];
        List<TomlTableReader> skills = [.. Ordered(document.TableArray("categories.skills"))];

        List<SkillCategoryEntry> categories = [];
        HashSet<string> seenCategories = new(StringComparer.Ordinal);
        HashSet<string> seenSkills = new(StringComparer.Ordinal);

        for (int index = 0; index < declared.Count; index++)
        {
            TomlTableReader entry = declared[index];
            entry.ReportUnknownKeys(CategoryKeys, sink);
            string? category = entry.String("id", sink);

            int start = entry.Position.Line;
            int end = index + 1 < declared.Count ? declared[index + 1].Position.Line : int.MaxValue;

            List<SkillEntry> held = [.. Skills(
                skills.Where(skill => skill.Position.Line > start && skill.Position.Line < end),
                category ?? "?",
                seenSkills,
                sink)];

            if (string.IsNullOrWhiteSpace(category))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"A category on '{id}' has no 'id'; it was dropped.",
                    entry.Position);
                continue;
            }

            if (!seenCategories.Add(category))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Category id '{category}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            categories.Add(new SkillCategoryEntry(category, held));
        }

        return categories;
    }

    private static IEnumerable<SkillEntry> Skills(
        IEnumerable<TomlTableReader> entries,
        string category,
        HashSet<string> seen,
        DiagnosticSink sink)
    {
        foreach (TomlTableReader entry in entries)
        {
            entry.ReportUnknownKeys(SkillKeys, sink);
            string? skill = entry.String("id", sink);

            if (string.IsNullOrWhiteSpace(skill))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"A skill in '{category}' has no 'id'; it was dropped.",
                    entry.Position);
                continue;
            }

            if (EnumNames.Level(entry, "level", sink) is not { } level)
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Skill '{skill}' has no known 'level'; it was dropped.",
                    entry.PositionOf("level"));
                continue;
            }

            // One skill belongs in one category, so a repeat is a mistake wherever it sits.
            if (!seen.Add(skill))
            {
                sink.Warning(
                    DiagnosticCodes.SchemaInvalidValue,
                    $"Skill id '{skill}' is declared more than once; the later one was dropped.",
                    entry.Position);
                continue;
            }

            yield return new SkillEntry(skill, level);
        }
    }

    private static IEnumerable<TomlTableReader> Ordered(IEnumerable<TomlTableReader> entries) =>
        entries.OrderBy(entry => entry.Position.Line).ThenBy(entry => entry.Position.Column);
}
