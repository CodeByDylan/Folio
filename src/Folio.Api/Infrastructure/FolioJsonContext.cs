using System.Text.Json.Serialization;
using Diagnostics = Folio.Api.Features.Diagnostics.GetDiagnostics;
using Projects = Folio.Api.Features.Projects;
using Refresh = Folio.Api.Features.Refresh.TriggerRefresh;
using Site = Folio.Api.Features.Site.GetSite;

namespace Folio.Api.Infrastructure;

/// <summary>Serialization metadata for every response type.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Site.Response), TypeInfoPropertyName = "SiteResponse")]
[JsonSerializable(typeof(Projects.ListProjects.Response), TypeInfoPropertyName = "ListProjectsResponse")]
[JsonSerializable(typeof(Projects.GetProject.Response), TypeInfoPropertyName = "GetProjectResponse")]
[JsonSerializable(typeof(Diagnostics.Response), TypeInfoPropertyName = "DiagnosticsResponse")]
[JsonSerializable(typeof(Refresh.Response), TypeInfoPropertyName = "RefreshResponse")]
internal sealed partial class FolioJsonContext : JsonSerializerContext;
