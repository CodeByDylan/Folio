namespace Folio.Domain.Configuration;

/// <summary>
/// The locale keys a section's words are declared under. Every key shape is stated here once, so
/// resolution and the orphan audit cannot drift apart.
/// </summary>
internal static class SectionKeys
{
    /// <summary>Gets the prefix every key of one section shares.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The dotted prefix.</returns>
    public static string Prefix(string id) => $"section.{id}";

    /// <summary>Gets the key a hero's headline is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The dotted key.</returns>
    public static string Headline(string id) => $"{Prefix(id)}.headline";

    /// <summary>Gets the key a hero's subheadline is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The dotted key.</returns>
    public static string Subheadline(string id) => $"{Prefix(id)}.subheadline";

    /// <summary>Gets the key one call to action's label is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="action">The action id.</param>
    /// <returns>The dotted key.</returns>
    public static string Action(string id, string action) => $"{Prefix(id)}.action.{action}";

    /// <summary>Gets the key one image's alt text is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="role">The image role.</param>
    /// <returns>The dotted key.</returns>
    public static string MediaAlt(string id, string role) => $"{Prefix(id)}.media.{role}.alt";

    /// <summary>Gets the key one skill category's label is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="category">The category id.</param>
    /// <returns>The dotted key.</returns>
    public static string Category(string id, string category) => $"{Prefix(id)}.category.{category}";

    /// <summary>Gets the key one skill's label is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="skill">The skill id.</param>
    /// <returns>The dotted key.</returns>
    public static string Skill(string id, string skill) => $"{Prefix(id)}.skill.{skill}";

    /// <summary>Gets the key one question is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <param name="entry">The entry id.</param>
    /// <returns>The dotted key.</returns>
    public static string Question(string id, string entry) => $"{Prefix(id)}.question.{entry}";

    /// <summary>Gets the key a section's heading is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The dotted key.</returns>
    public static string Heading(string id) => $"{Prefix(id)}.heading";

    /// <summary>Gets the key a section's blurb is declared under.</summary>
    /// <param name="id">The section id.</param>
    /// <returns>The dotted key.</returns>
    public static string Blurb(string id) => $"{Prefix(id)}.blurb";

    /// <summary>Every key one section declares, so the orphan audit never restates them.</summary>
    /// <param name="entry">The section.</param>
    /// <returns>The dotted keys, in no particular order.</returns>
    public static IEnumerable<string> All(SectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        switch (entry)
        {
            case HeroSectionEntry hero:
                yield return Headline(hero.Id);
                yield return Subheadline(hero.Id);

                foreach (HeroActionEntry action in hero.Actions)
                {
                    yield return Action(hero.Id, action.Id);
                }

                foreach (string role in hero.Media.Keys)
                {
                    yield return MediaAlt(hero.Id, role);
                }

                break;

            case SkillsSectionEntry skills:
                foreach (SkillCategoryEntry category in skills.Categories)
                {
                    yield return Category(skills.Id, category.Id);

                    foreach (SkillEntry skill in category.Skills)
                    {
                        yield return Skill(skills.Id, skill.Id);
                    }
                }

                break;

            case QaSectionEntry qa:
                foreach (string id in qa.Entries)
                {
                    yield return Question(qa.Id, id);
                }

                break;

            case ContactSectionEntry contact:
                yield return Heading(contact.Id);
                yield return Blurb(contact.Id);
                break;

            case ProjectsSectionEntry projects:
                yield return Heading(projects.Id);
                break;

            default:
                break;
        }
    }
}
