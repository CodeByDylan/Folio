using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;

namespace Folio.Api.Infrastructure;

/// <summary>Names the schemas a slice contributes to the OpenAPI document.</summary>
internal static class SchemaIds
{
    private static readonly string[] Ambiguous = ["Request", "Response"];

    /// <summary>Names one schema, qualifying the per-slice types that would otherwise collide.</summary>
    /// <param name="type">The type being described.</param>
    /// <returns>The schema identifier, or <see langword="null" /> to inline the schema.</returns>
    public static string? For(JsonTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);

        string? name = OpenApiOptions.CreateDefaultSchemaReferenceId(type);

        if (name is null || !Ambiguous.Contains(name, StringComparer.Ordinal))
        {
            return name;
        }

        // Every slice names its response Response; the document's schema namespace is flat.
        string? operation = type.Type.Namespace?.Split('.').LastOrDefault();

        return operation is null ? name : operation + name;
    }
}
