namespace Folio.Api.Infrastructure;

/// <summary>How an enum member's name reaches the wire.</summary>
internal enum WireNaming
{
    /// <summary>Lowercased, as <see cref="Wire.Lower{TEnum}" /> writes it.</summary>
    Lower,

    /// <summary>Hyphenated, as <see cref="Wire.Hyphenate" /> writes it.</summary>
    Hyphenated,

    /// <summary>Named by <see cref="Folio.Domain.Model.RelationVocabulary" />.</summary>
    Vocabulary,
}

/// <summary>Declares the enum a wire property's string values come from.</summary>
/// <param name="enumType">The enum backing the property.</param>
/// <param name="naming">How its members are written.</param>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class WireEnumAttribute(Type enumType, WireNaming naming = WireNaming.Lower) : Attribute
{
    /// <summary>Gets the enum backing the property.</summary>
    public Type EnumType => enumType;

    /// <summary>Gets how its members are written.</summary>
    public WireNaming Naming => naming;
}
