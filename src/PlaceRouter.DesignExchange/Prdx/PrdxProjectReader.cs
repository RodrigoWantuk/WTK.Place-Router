using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class PrdxProjectReader(
    SchemaRegistry? schemaRegistry = null,
    CanonicalIntegrityValidator? integrityValidator = null) : IPrdxProjectReader
{
    private readonly SchemaRegistry _schemaRegistry = schemaRegistry ?? new SchemaRegistry();
    private readonly CanonicalIntegrityValidator _integrityValidator = integrityValidator ?? new CanonicalIntegrityValidator(schemaRegistry);

    public ProjectLoadResult Load(string path)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            if (!File.Exists(path))
            {
                return new ProjectLoadResult(null,
                [
                    Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "Container", $"PRDX file '{path}' does not exist.")
                ]);
            }

            using var archive = ZipFile.OpenRead(path);
            var manifestEntry = archive.GetEntry(PrdxManifest.ManifestPath);
            if (manifestEntry is null)
            {
                return new ProjectLoadResult(null,
                [
                    Diagnostic.Fatal(DiagnosticCodes.ManifestMissing, "Container", "PRDX manifest.json is missing.")
                ]);
            }

            var manifestJson = ReadEntryText(manifestEntry);
            var manifestNode = JsonNode.Parse(manifestJson) as JsonObject;
            if (manifestNode is null)
            {
                return new ProjectLoadResult(null,
                [
                    Diagnostic.Fatal(DiagnosticCodes.ManifestSchema, "Schema", "PRDX manifest.json root must be an object.")
                ]);
            }

            diagnostics.AddRange(_schemaRegistry.ValidateManifest(manifestNode));
            diagnostics.AddRange(ValidateManifestInvariants(manifestNode));
            if (diagnostics.Any(d => d.Blocking))
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            var canonicalPayload = manifestNode["canonicalPayload"]?.GetValue<string>() ?? PrdxManifest.ProjectPayloadPath;
            var payloadEntry = archive.GetEntry(canonicalPayload);
            if (payloadEntry is null)
            {
                diagnostics.Add(Diagnostic.Fatal(DiagnosticCodes.PayloadMissing, "Container", $"PRDX payload '{canonicalPayload}' is missing."));
                return new ProjectLoadResult(null, diagnostics);
            }

            var payloadBytes = ReadEntryBytes(payloadEntry);
            var expectedHash = manifestNode["payloadSha256"]?.GetValue<string>();
            var actualHash = Sha256.Hex(payloadBytes);
            if (!StringComparer.OrdinalIgnoreCase.Equals(expectedHash, actualHash))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PayloadHash,
                    DiagnosticSeverity.Error,
                    "Integrity",
                    $"Payload SHA-256 mismatch. Expected {expectedHash}, actual {actualHash}.",
                    Evidence: new Dictionary<string, object?>
                    {
                        ["expected"] = expectedHash,
                        ["actual"] = actualHash
                    },
                    Blocking: true));
                return new ProjectLoadResult(null, diagnostics);
            }

            var projectJson = Encoding.UTF8.GetString(payloadBytes);
            var projectNode = JsonNode.Parse(projectJson) as JsonObject;
            if (projectNode is null)
            {
                diagnostics.Add(Diagnostic.Fatal(DiagnosticCodes.ProjectSchema, "Schema", "PRDX project.json root must be an object."));
                return new ProjectLoadResult(null, diagnostics);
            }

            diagnostics.AddRange(_schemaRegistry.ValidateProject(projectNode));
            if (diagnostics.Any(d => d.Blocking))
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            diagnostics.AddRange(_integrityValidator.ValidateIntegrity(projectNode));
            if (diagnostics.Any(d => d.Blocking))
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            return new ProjectLoadResult(new CanonicalProject(projectNode), diagnostics);
        }
        catch (InvalidDataException ex)
        {
            return new ProjectLoadResult(null,
            [
                Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "Container", $"Invalid PRDX ZIP container: {ex.Message}")
            ]);
        }
        catch (JsonException ex)
        {
            return new ProjectLoadResult(null,
            [
                Diagnostic.Fatal(DiagnosticCodes.ProjectSchema, "Schema", $"Invalid PRDX JSON: {ex.Message}")
            ]);
        }
        catch (Exception ex)
        {
            return new ProjectLoadResult(null,
            [
                Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "TechnicalFailure", $"Unexpected PRDX load failure: {ex.Message}")
            ]);
        }
    }

    private static string ReadEntryText(ZipArchiveEntry entry) => Encoding.UTF8.GetString(ReadEntryBytes(entry));

    private static IReadOnlyList<Diagnostic> ValidateManifestInvariants(JsonObject manifestNode)
    {
        var diagnostics = new List<Diagnostic>();
        var format = manifestNode["format"]?.GetValue<string>();
        if (!StringComparer.Ordinal.Equals(format, PrdxManifest.ExpectedFormat))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ManifestSchema,
                DiagnosticSeverity.Error,
                "Schema",
                $"PRDX manifest format must be '{PrdxManifest.ExpectedFormat}'.",
                Blocking: true));
        }

        var payload = manifestNode["canonicalPayload"]?.GetValue<string>();
        if (!StringComparer.Ordinal.Equals(payload, PrdxManifest.ProjectPayloadPath))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ManifestSchema,
                DiagnosticSeverity.Error,
                "Schema",
                $"PRDX canonical payload must be '{PrdxManifest.ProjectPayloadPath}'.",
                Blocking: true));
        }

        return diagnostics;
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
