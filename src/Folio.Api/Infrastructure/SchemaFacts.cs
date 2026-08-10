using System.Reflection;
using System.Text.Json.Nodes;
using Folio.Domain.Model;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Folio.Api.Infrastructure;

/// <summary>Corrects the two facts the schema generator cannot read off a wire type.</summary>
internal sealed class SchemaFacts : IOpenApiSchemaTransformer
{
    /// <summary>Applies both corrections to one schema.</summary>
    /// <param name="schema">The schema being built.</param>
    /// <param name="context">What the schema describes.</param>
    /// <param name="cancellationToken">Abandons the work.</param>
    /// <returns>A completed task.</returns>
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        Describe(schema, context);
        Omit(schema);

        return Task.CompletedTask;
    }

    /// <summary>Lists the members a string property accepts, when it carries an enum.</summary>
    /// <param name="schema">The property's schema.</param>
    /// <param name="context">What the schema describes.</param>
    private static void Describe(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        // JsonPropertyInfo.Name is the serialized name, which the naming policy has already camel-cased.
        PropertyInfo? property = context.JsonPropertyInfo?.DeclaringType.GetProperty(
            context.JsonPropertyInfo.Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property?.GetCustomAttribute<WireEnumAttribute>() is not { } wire)
        {
            return;
        }

        schema.Enum = [.. Members(wire).Select(name => (JsonNode)JsonValue.Create(name))];
    }

    /// <summary>Drops the properties the serializer omits from those the schema requires.</summary>
    /// <param name="schema">The schema being built.</param>
    private static void Omit(OpenApiSchema schema)
    {
        if (schema.Required is not { Count: > 0 } required || schema.Properties is not { } properties)
        {
            return;
        }

        // A null-valued optional is written by omitting its key, so the schema must not require it.
        foreach (string name in required.ToArray())
        {
            if (properties.TryGetValue(name, out IOpenApiSchema? property)
                && property.Type?.HasFlag(JsonSchemaType.Null) == true)
            {
                _ = required.Remove(name);
            }
        }
    }

    /// <summary>Writes every member of a declared enum the way the mapper writes it.</summary>
    /// <param name="wire">The declaration.</param>
    /// <returns>The member names, in declaration order.</returns>
    private static IEnumerable<string> Members(WireEnumAttribute wire) => wire.Naming switch
    {
        WireNaming.Vocabulary => RelationVocabulary.All.Select(RelationVocabulary.Name),
        WireNaming.Hyphenated => Enum.GetNames(wire.EnumType).Select(Wire.Hyphenate),
        _ => Enum.GetNames(wire.EnumType).Select(Wire.Lower),
    };
}
