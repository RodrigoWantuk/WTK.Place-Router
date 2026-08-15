using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed record PrdxReadLimits(
    int MaxEntries = 4096,
    long MaxManifestBytes = 1_048_576,
    long MaxProjectBytes = 64 * 1_048_576,
    long MaxSupplementaryEntryBytes = 512 * 1_048_576);

public interface IAtomicFileCommitter
{
    void Commit(string tempPath, string destinationPath);
}

public sealed class DefaultAtomicFileCommitter : IAtomicFileCommitter
{
    public void Commit(string tempPath, string destinationPath)
    {
        var backupPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".bak";
        if (File.Exists(destinationPath))
        {
            try
            {
                File.Replace(tempPath, destinationPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
            }
        }

        if (!File.Exists(destinationPath))
        {
            File.Move(tempPath, destinationPath);
            return;
        }

        File.Move(destinationPath, backupPath);
        try
        {
            File.Move(tempPath, destinationPath);
            TryDelete(backupPath);
        }
        catch
        {
            if (File.Exists(backupPath) && !File.Exists(destinationPath))
            {
                File.Move(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

public sealed class PrdxProjectStore(
    IPrdxSchemaValidator? schemaValidator = null,
    ICanonicalProjectValidator? integrityValidator = null,
    IAtomicFileCommitter? committer = null,
    PrdxReadLimits? limits = null) : IProjectStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IPrdxSchemaValidator _schemaValidator = schemaValidator ?? new SchemaRegistry();
    private readonly ICanonicalProjectValidator _integrityValidator = integrityValidator ?? new CanonicalIntegrityValidator();
    private readonly IAtomicFileCommitter _committer = committer ?? new DefaultAtomicFileCommitter();
    private readonly PrdxReadLimits _limits = limits ?? new PrdxReadLimits();

    public ProjectLoadResult Load(string path)
    {
        var diagnostics = new List<Diagnostic>();
        try
        {
            if (!File.Exists(path))
            {
                return Failure(Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "Container", $"PRDX file '{path}' does not exist."));
            }

            using var archive = ZipFile.OpenRead(path);
            diagnostics.AddRange(ValidateEntrySet(archive));
            if (diagnostics.HasBlockingDiagnostics())
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            var manifestEntry = archive.GetEntry(PrdxManifest.ManifestPath);
            if (manifestEntry is null)
            {
                return Failure(Diagnostic.Fatal(DiagnosticCodes.ManifestMissing, "Container", "PRDX manifest.json is missing."));
            }

            if (manifestEntry.Length > _limits.MaxManifestBytes)
            {
                return Failure(TooLarge(PrdxManifest.ManifestPath, manifestEntry.Length, _limits.MaxManifestBytes));
            }

            var manifestNode = ParseObject(ReadUtf8(manifestEntry, DiagnosticCodes.ManifestSchema), DiagnosticCodes.ManifestSchema);
            diagnostics.AddRange(_schemaValidator.Validate(PrdxSchemaKind.Manifest, manifestNode));
            diagnostics.AddRange(PrdxVersionPolicy.ValidateManifest(manifestNode));
            diagnostics.AddRange(ValidateManifestInvariants(manifestNode));
            if (diagnostics.HasBlockingDiagnostics())
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            var payloadPath = manifestNode["canonicalPayload"]?.GetValue<string>() ?? PrdxManifest.ProjectPayloadPath;
            var payloadEntry = archive.GetEntry(payloadPath);
            if (payloadEntry is null)
            {
                return Failure(Diagnostic.Fatal(DiagnosticCodes.PayloadMissing, "Container", $"PRDX payload '{payloadPath}' is missing."));
            }

            if (payloadEntry.Length > _limits.MaxProjectBytes)
            {
                return Failure(TooLarge(payloadPath, payloadEntry.Length, _limits.MaxProjectBytes));
            }

            var payloadBytes = ReadEntryBytes(payloadEntry, _limits.MaxProjectBytes);
            var actualHash = Sha256.Hex(payloadBytes);
            var expectedHash = manifestNode["payloadSha256"]?.GetValue<string>();
            if (!StringComparer.OrdinalIgnoreCase.Equals(expectedHash, actualHash))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PayloadHash,
                    DiagnosticSeverity.Error,
                    "Integrity",
                    $"Payload SHA-256 mismatch. Expected {expectedHash}, actual {actualHash}.",
                    Evidence: new Dictionary<string, object?> { ["expected"] = expectedHash, ["actual"] = actualHash },
                    Blocking: true));
                return new ProjectLoadResult(null, diagnostics);
            }

            var projectNode = ParseObject(StrictUtf8.GetString(payloadBytes), DiagnosticCodes.ProjectSchema);
            diagnostics.AddRange(_schemaValidator.Validate(PrdxSchemaKind.Project, projectNode));
            diagnostics.AddRange(PrdxVersionPolicy.ValidateProject(projectNode));
            diagnostics.AddRange(ValidateManifestAgainstProject(manifestNode, projectNode));
            if (diagnostics.HasBlockingDiagnostics())
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            var project = PrdxProjectMapper.ToDomain(projectNode);
            diagnostics.AddRange(ValidateSourceFingerprints(manifestNode, project));
            diagnostics.AddRange(_integrityValidator.Validate(project).Diagnostics);
            diagnostics.AddRange(ValidateEmbeddedSources(project, archive));

            if (diagnostics.HasBlockingDiagnostics())
            {
                return new ProjectLoadResult(null, diagnostics);
            }

            var context = new ProjectFileContext(
                Path.GetFullPath(path),
                manifestNode["formatVersion"]?.GetValue<string>() ?? PrdxManifest.CurrentFormatVersion,
                Strings(manifestNode["featureFlags"]).ToArray(),
                ReadFingerprints(manifestNode),
                ReadSupplementaryEntries(archive),
                []);

            return new ProjectLoadResult(new ProjectDocument(project, context), diagnostics);
        }
        catch (DecoderFallbackException ex)
        {
            return Failure(new Diagnostic(DiagnosticCodes.Utf8Invalid, DiagnosticSeverity.Error, "Encoding", $"Invalid UTF-8 in PRDX JSON entry: {ex.Message}", Blocking: true));
        }
        catch (InvalidDataException ex)
        {
            return Failure(Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "Container", $"Invalid PRDX ZIP container: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Failure(Diagnostic.Fatal(DiagnosticCodes.ProjectSchema, "Schema", $"Invalid PRDX JSON: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Failure(Diagnostic.Fatal(DiagnosticCodes.ContainerInvalid, "TechnicalFailure", $"Unexpected PRDX load failure: {ex.Message}"));
        }
    }

    public ProjectSaveResult Save(ProjectDocument document, string path)
    {
        var validation = _integrityValidator.Validate(document.Project);
        if (!validation.Success)
        {
            return new ProjectSaveResult(validation.Diagnostics);
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var projectNode = PrdxProjectMapper.ToJson(document.Project);
            var projectSchemaDiagnostics = _schemaValidator.Validate(PrdxSchemaKind.Project, projectNode).Concat(PrdxVersionPolicy.ValidateProject(projectNode)).ToArray();
            if (projectSchemaDiagnostics.HasBlockingDiagnostics())
            {
                return new ProjectSaveResult(projectSchemaDiagnostics);
            }

            var payloadBytes = StrictUtf8.GetBytes(projectNode.ToJsonString(JsonOptions) + "\n");
            var fingerprints = SourceFingerprintsFromProject(document);
            var manifest = new PrdxManifest(
                PrdxManifest.ExpectedFormat,
                PrdxManifest.CurrentFormatVersion,
                document.Project.ProjectId.Value,
                document.Project.ProjectRevision,
                document.Project.Metadata.CreatedAt.ToString("O"),
                document.Project.Metadata.ModifiedAt.ToString("O"),
                typeof(PrdxProjectStore).Assembly.GetName().Version?.ToString(),
                PrdxManifest.ProjectPayloadPath,
                Sha256.Hex(payloadBytes),
                document.FileContext.FeatureFlags,
                fingerprints.Select(f => new ManifestSourceFingerprint(f.SourceImportId.Value, f.Sha256)).ToArray());

            WriteArchive(tempPath, manifest, payloadBytes, document.FileContext);
            var tempLoad = Load(tempPath);
            if (!tempLoad.Success)
            {
                TryDelete(tempPath);
                return new ProjectSaveResult(tempLoad.Diagnostics);
            }

            _committer.Commit(tempPath, fullPath);
            var savedDocument = tempLoad.Document! with
            {
                FileContext = tempLoad.Document.FileContext with
                {
                    SourcePath = fullPath,
                    PendingSupplementaryFiles = []
                }
            };
            return new ProjectSaveResult([], savedDocument);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new ProjectSaveResult(
            [
                new Diagnostic(
                    DiagnosticCodes.SaveFailed,
                    DiagnosticSeverity.Error,
                    "Persistence",
                    $"PRDX save failed before replacing the destination: {ex.Message}",
                    Blocking: true)
            ]);
        }
    }

    private void WriteArchive(string path, PrdxManifest manifest, byte[] payloadBytes, ProjectFileContext context)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, PrdxManifest.ManifestPath, StrictUtf8.GetBytes(manifest.ToJsonObject().ToJsonString(JsonOptions) + "\n"));
            WriteEntry(archive, PrdxManifest.ProjectPayloadPath, payloadBytes);

            if (context.SourcePath is not null && File.Exists(context.SourcePath))
            {
                using var sourceArchive = ZipFile.OpenRead(context.SourcePath);
                foreach (var supplementary in context.SupplementaryEntries.OrderBy(e => e.Path, StringComparer.Ordinal))
                {
                    var sourceEntry = sourceArchive.GetEntry(supplementary.Path);
                    if (sourceEntry is null)
                    {
                        continue;
                    }

                    var targetEntry = archive.CreateEntry(supplementary.Path, CompressionLevel.Optimal);
                    using var sourceStream = sourceEntry.Open();
                    using var targetStream = targetEntry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }

            foreach (var pending in context.PendingSupplementaryFiles.OrderBy(e => e.EntryPath, StringComparer.Ordinal))
            {
                if (!File.Exists(pending.SourcePath))
                {
                    continue;
                }

                var targetEntry = archive.CreateEntry(pending.EntryPath, CompressionLevel.Optimal);
                using var sourceStream = File.OpenRead(pending.SourcePath);
                using var targetStream = targetEntry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        stream.Flush(flushToDisk: true);
    }

    private IReadOnlyList<Diagnostic> ValidateEntrySet(ZipArchive archive)
    {
        var diagnostics = new List<Diagnostic>();
        if (archive.Entries.Count > _limits.MaxEntries)
        {
            diagnostics.Add(TooLarge("entry-count", archive.Entries.Count, _limits.MaxEntries));
        }

        foreach (var duplicate in archive.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.EntryDuplicate,
                DiagnosticSeverity.Error,
                "Container",
                $"PRDX container contains duplicate entry '{duplicate.Key}'.",
                Blocking: true));
        }

        foreach (var entry in archive.Entries.Where(IsSupplementary))
        {
            if (entry.Length > _limits.MaxSupplementaryEntryBytes)
            {
                diagnostics.Add(TooLarge(entry.FullName, entry.Length, _limits.MaxSupplementaryEntryBytes));
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> ValidateManifestInvariants(JsonObject manifest)
    {
        var diagnostics = new List<Diagnostic>();
        if (!StringComparer.Ordinal.Equals(manifest["format"]?.GetValue<string>(), PrdxManifest.ExpectedFormat))
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.ManifestSchema, DiagnosticSeverity.Error, "Schema", $"PRDX manifest format must be '{PrdxManifest.ExpectedFormat}'.", Blocking: true));
        }

        if (!StringComparer.Ordinal.Equals(manifest["canonicalPayload"]?.GetValue<string>(), PrdxManifest.ProjectPayloadPath))
        {
            diagnostics.Add(new Diagnostic(DiagnosticCodes.ManifestSchema, DiagnosticSeverity.Error, "Schema", $"PRDX canonical payload must be '{PrdxManifest.ProjectPayloadPath}'.", Blocking: true));
        }

        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> ValidateManifestAgainstProject(JsonObject manifest, JsonObject project)
    {
        var diagnostics = new List<Diagnostic>();
        Check("projectId", manifest["projectId"]?.GetValue<string>(), project["projectId"]?.GetValue<string>());
        Check("projectRevision", manifest["projectRevision"]?.GetValue<long>().ToString(), project["projectRevision"]?.GetValue<long>().ToString());
        Check("createdAt", manifest["createdAt"]?.GetValue<string>(), (project["metadata"] as JsonObject)?["createdAt"]?.GetValue<string>());
        Check("modifiedAt", manifest["modifiedAt"]?.GetValue<string>(), (project["metadata"] as JsonObject)?["modifiedAt"]?.GetValue<string>());
        return diagnostics;

        void Check(string field, string? left, string? right)
        {
            if (!StringComparer.Ordinal.Equals(left, right))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ManifestProjectMismatch,
                    DiagnosticSeverity.Error,
                    "Integrity",
                    $"Manifest field '{field}' does not match project payload.",
                    Blocking: true));
            }
        }
    }

    private static IReadOnlyList<Diagnostic> ValidateSourceFingerprints(JsonObject manifest, CanonicalProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var imports = project.SourceImports.ToDictionary(s => s.Id.Value, StringComparer.Ordinal);
        foreach (var fingerprint in manifest["sourceFingerprints"] as JsonArray ?? [])
        {
            if (fingerprint is not JsonObject fp)
            {
                continue;
            }

            var id = fp["sourceImportId"]?.GetValue<string>() ?? string.Empty;
            var sha = fp["sha256"]?.GetValue<string>() ?? string.Empty;
            if (!imports.TryGetValue(id, out var import) || !StringComparer.OrdinalIgnoreCase.Equals(import.SourceSha256, sha))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ManifestProjectMismatch,
                    DiagnosticSeverity.Error,
                    "Integrity",
                    $"Manifest source fingerprint '{id}' does not match project source imports.",
                    Blocking: true));
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> ValidateEmbeddedSources(CanonicalProject project, ZipArchive archive)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var source in project.SourceImports.Where(s => !string.IsNullOrWhiteSpace(s.EmbeddedPath)))
        {
            if (archive.GetEntry(source.EmbeddedPath!) is null)
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.SupplementaryMissing,
                    "Integrity",
                    $"Source import '{source.Id}' references missing embedded entry '{source.EmbeddedPath}'.",
                    blocking: false));
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<SourceFingerprint> SourceFingerprintsFromProject(ProjectDocument document)
    {
        var fingerprints = document.Project.SourceImports
            .Select(source => new SourceFingerprint(source.Id, source.SourceSha256))
            .ToArray();

        return fingerprints.Length == 0 ? document.FileContext.SourceFingerprints : fingerprints;
    }

    private static IReadOnlyList<SourceFingerprint> ReadFingerprints(JsonObject manifest) =>
        (manifest["sourceFingerprints"] as JsonArray ?? [])
        .OfType<JsonObject>()
        .Select(o => new SourceFingerprint(new SourceImportId(o["sourceImportId"]?.GetValue<string>() ?? string.Empty), o["sha256"]?.GetValue<string>() ?? string.Empty))
        .ToArray();

    private static IReadOnlyList<SupplementaryEntry> ReadSupplementaryEntries(ZipArchive archive) =>
        archive.Entries
            .Where(IsSupplementary)
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .Select(e => new SupplementaryEntry(e.FullName, e.Length, HashEntry(e)))
            .ToArray();

    private static string HashEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Sha256.Hex(ReadAll(stream, long.MaxValue));
    }

    private static bool IsSupplementary(ZipArchiveEntry entry) =>
        !entry.FullName.EndsWith("/", StringComparison.Ordinal) &&
        (entry.FullName.StartsWith("source/", StringComparison.Ordinal) ||
         entry.FullName.StartsWith("assets/", StringComparison.Ordinal) ||
         entry.FullName.StartsWith("attachments/", StringComparison.Ordinal));

    private static JsonObject ParseObject(string json, string code) =>
        JsonNode.Parse(json) as JsonObject
        ?? throw new JsonException($"{code}: expected object root.");

    private static string ReadUtf8(ZipArchiveEntry entry, string code)
    {
        try
        {
            return StrictUtf8.GetString(ReadEntryBytes(entry, long.MaxValue));
        }
        catch (DecoderFallbackException ex)
        {
            throw new DecoderFallbackException($"{code}: {ex.Message}");
        }
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, long maxBytes)
    {
        using var stream = entry.Open();
        return ReadAll(stream, maxBytes);
    }

    private static byte[] ReadAll(Stream stream, long maxBytes)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("PRDX entry exceeded configured read limit.");
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(v => v?.GetValue<string>()).Where(v => v is not null).Select(v => v!)
            : [];

    private static Diagnostic TooLarge(string entry, long actual, long max) =>
        new(
            DiagnosticCodes.EntryTooLarge,
            DiagnosticSeverity.Error,
            "Container",
            $"PRDX entry '{entry}' size {actual} exceeds configured limit {max}.",
            Blocking: true);

    private static ProjectLoadResult Failure(Diagnostic diagnostic) => new(null, [diagnostic]);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
