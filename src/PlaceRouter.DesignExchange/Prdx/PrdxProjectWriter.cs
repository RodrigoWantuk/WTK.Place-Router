using System.IO.Compression;
using System.Text;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class PrdxProjectWriter(PrdxProjectReader? reader = null, ICanonicalProjectValidator? validator = null) : IPrdxProjectWriter
{
    private readonly PrdxProjectReader _reader = reader ?? new PrdxProjectReader();
    private readonly ICanonicalProjectValidator _validator = validator ?? new CanonicalIntegrityValidator();

    public ProjectSaveResult Save(CanonicalProject project, string path, PrdxWriteOptions? options = null)
    {
        var validation = _validator.Validate(project);
        if (!validation.Success)
        {
            return new ProjectSaveResult(validation.Diagnostics);
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
            fullPath = Path.Combine(directory, fullPath);
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.bak");

        try
        {
            var payloadBytes = project.ToUtf8JsonBytes(indented: true);
            var metadata = project.Root["metadata"];
            var createdAt = metadata?["createdAt"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("O");
            var modifiedAt = metadata?["modifiedAt"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("O");
            var manifest = new PrdxManifest(
                PrdxManifest.ExpectedFormat,
                PrdxManifest.CurrentFormatVersion,
                project.ProjectId,
                project.ProjectRevision,
                createdAt,
                modifiedAt,
                typeof(PrdxProjectWriter).Assembly.GetName().Version?.ToString(),
                PrdxManifest.ProjectPayloadPath,
                Sha256.Hex(payloadBytes));

            var preservedEntries = File.Exists(fullPath) ? ReadPreservedEntries(fullPath) : [];
            WriteArchive(tempPath, manifest, payloadBytes, preservedEntries);

            var tempLoad = _reader.Load(tempPath);
            if (!tempLoad.Success)
            {
                TryDelete(tempPath);
                return new ProjectSaveResult(tempLoad.Diagnostics);
            }

            options?.BeforeCommit?.Invoke(tempPath);

            if (File.Exists(fullPath))
            {
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return new ProjectSaveResult([]);
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

    private static void WriteArchive(string path, PrdxManifest manifest, byte[] payloadBytes, IReadOnlyDictionary<string, byte[]> preservedEntries)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, PrdxManifest.ManifestPath, Encoding.UTF8.GetBytes(manifest.ToJsonObject().ToJsonString(new() { WriteIndented = true })));
            WriteEntry(archive, PrdxManifest.ProjectPayloadPath, payloadBytes);

            foreach (var (entryPath, bytes) in preservedEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                WriteEntry(archive, entryPath, bytes);
            }
        }

        stream.Flush(flushToDisk: true);
    }

    private static Dictionary<string, byte[]> ReadPreservedEntries(string existingPath)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(existingPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Equals(PrdxManifest.ManifestPath, StringComparison.Ordinal) ||
                entry.FullName.Equals(PrdxManifest.ProjectPayloadPath, StringComparison.Ordinal) ||
                entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!entry.FullName.StartsWith("source/", StringComparison.Ordinal) &&
                !entry.FullName.StartsWith("assets/", StringComparison.Ordinal) &&
                !entry.FullName.StartsWith("attachments/", StringComparison.Ordinal))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            result[entry.FullName] = memory.ToArray();
        }

        return result;
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
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
            // Best effort cleanup; the original project file has not been replaced.
        }
    }
}
