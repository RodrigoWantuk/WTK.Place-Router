using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceRouter.Application.Projects;
using PlaceRouter.Cli;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.DesignExchange.Prdx;

namespace PlaceRouter.DesignExchange.Tests;

public sealed class PrdxPlan01RTests
{
    [Fact]
    public void Full_and_incomplete_fixtures_schema_validate_and_load_as_typed_domain()
    {
        using var temp = new TempDirectory();
        var store = new PrdxProjectStore();

        var full = store.Load(PrdxTestData.CreateFixturePrdx(temp.Path));
        var incomplete = store.Load(PrdxTestData.CreateFixturePrdx(temp.Path, PrdxTestData.IncompleteProjectJson()));

        Assert.True(full.Success, Messages(full.Diagnostics));
        Assert.Equal("cmp_u1", full.Project!.LogicalDesign.Components.Single().Id.Value);
        Assert.Equal("layer_top_cu", full.Project.Board.Layers.First().Id.Value);
        Assert.True(full.Project.Board.Layers.First().IsCopperCapable);

        Assert.True(incomplete.Success, Messages(incomplete.Diagnostics));
        Assert.Null(incomplete.Project!.Board.Outline);
        Assert.Empty(incomplete.Project.Board.Layers);
        Assert.Null(incomplete.Project.LogicalDesign.Components.Single().FootprintId);
        Assert.Equal("24", incomplete.Project.LogicalDesign.Nets.Single().Endpoints.Single().PinRef);
        Assert.Equal("INCOMPLETE", incomplete.Project.PhysicalDesignState.Status);
    }

    [Theory]
    [InlineData("additional")]
    [InlineData("enum")]
    [InlineData("const")]
    [InlineData("oneOf")]
    [InlineData("minimum")]
    [InlineData("pattern")]
    [InlineData("unique")]
    [InlineData("anyOf")]
    [InlineData("nullable")]
    public void Schema_subset_rejects_contract_violations(string mutation)
    {
        var schema = new SchemaRegistry();
        var project = JsonNode.Parse(PrdxTestData.IncompleteProjectJson())!.AsObject();
        JsonObject? manifest = null;

        switch (mutation)
        {
            case "additional":
                project["unexpected"] = true;
                break;
            case "enum":
                project["physicalDesignState"]!["status"] = "BROKEN";
                break;
            case "const":
                manifest = new PrdxManifest("BAD", "0.1.0", "prj_x", 0, "2026-08-14T20:00:00Z", "2026-08-14T20:00:00Z", "test", "project.json", new string('0', 64)).ToJsonObject();
                break;
            case "oneOf":
                project["board"]!["outline"] = 42;
                break;
            case "minimum":
                project["projectRevision"] = -1;
                break;
            case "pattern":
                project["sourceImports"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "src_bad",
                    ["adapterId"] = "test",
                    ["adapterVersion"] = "0.1.0",
                    ["sourceType"] = "TEST",
                    ["sourceName"] = "bad",
                    ["sourceSha256"] = "not-a-sha",
                    ["importedAt"] = "2026-08-14T20:00:00Z",
                    ["embeddedPath"] = null,
                    ["capabilities"] = new JsonObject()
                });
                break;
            case "unique":
                manifest = new PrdxManifest("WTK.PRDX", "0.1.0", "prj_x", 0, "2026-08-14T20:00:00Z", "2026-08-14T20:00:00Z", "test", "project.json", new string('0', 64), ["dup", "dup"]).ToJsonObject();
                break;
            case "anyOf":
                project["logicalDesign"]!["netlist"]!["nets"]![0]!["endpoints"]![0]!.AsObject().Remove("pinRef");
                break;
            case "nullable":
                project["logicalDesign"]!["components"]![0]!["footprintId"] = "fp_missing_but_schema_valid";
                break;
        }

        var diagnostics = manifest is null
            ? schema.Validate(PrdxSchemaKind.Project, project)
            : schema.Validate(PrdxSchemaKind.Manifest, manifest);

        if (mutation == "nullable")
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var expectedCode = manifest is null ? DiagnosticCodes.ProjectSchema : DiagnosticCodes.ManifestSchema;
            Assert.Contains(diagnostics, d => d.Blocking && d.Code == expectedCode);
        }
    }

    [Theory]
    [InlineData("wrong-hash", DiagnosticCodes.PayloadHash)]
    [InlineData("manifest-project-id", DiagnosticCodes.ManifestProjectMismatch)]
    [InlineData("manifest-revision", DiagnosticCodes.ManifestProjectMismatch)]
    [InlineData("unsupported-format", DiagnosticCodes.VersionUnsupported)]
    [InlineData("unsupported-schema", DiagnosticCodes.VersionUnsupported)]
    [InlineData("unsupported-feature", DiagnosticCodes.FeatureUnsupported)]
    public void Package_load_reports_typed_diagnostics(string scenario, string expectedCode)
    {
        using var temp = new TempDirectory();
        var projectJson = PrdxTestData.FullProjectJson();
        var path = scenario switch
        {
            "wrong-hash" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson, m => m["payloadSha256"] = new string('0', 64)),
            "manifest-project-id" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson, m => m["projectId"] = "prj_other"),
            "manifest-revision" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson, m => m["projectRevision"] = 99),
            "unsupported-format" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson, m => m["formatVersion"] = "0.1.1"),
            "unsupported-schema" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson.Replace("\"schemaVersion\": \"0.1.0\"", "\"schemaVersion\": \"0.2.0\"")),
            "unsupported-feature" => PrdxTestData.CreateFixturePrdx(temp.Path, projectJson, m => m["featureFlags"] = new JsonArray("requires_future")),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = new PrdxProjectStore().Load(path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode && d.Blocking);
    }

    [Fact]
    public void Package_load_detects_duplicate_project_invalid_utf8_size_limit_and_missing_source()
    {
        using var temp = new TempDirectory();

        var duplicate = Path.Combine(temp.Path, "duplicate.prdx");
        using (var archive = ZipFile.Open(duplicate, ZipArchiveMode.Create))
        {
            var json = Encoding.UTF8.GetBytes(PrdxTestData.FullProjectJson());
            var manifest = new PrdxManifest("WTK.PRDX", "0.1.0", "prj_demo_001", 1, "2026-08-14T19:00:00Z", "2026-08-14T19:00:00Z", "test", "project.json", PrdxTestData.Sha256(json)).ToJsonObject().ToJsonString();
            PrdxTestData.WriteEntry(archive, "manifest.json", Encoding.UTF8.GetBytes(manifest));
            PrdxTestData.WriteEntry(archive, "project.json", json);
            PrdxTestData.WriteEntry(archive, "project.json", json);
        }

        var badUtf8 = PrdxTestData.CreatePrdxWithProjectBytes(temp.Path, [0xff, 0xfe, 0xfd]);
        var tooLarge = new PrdxProjectStore(limits: new PrdxReadLimits(MaxProjectBytes: 8)).Load(PrdxTestData.CreateFixturePrdx(temp.Path));

        var missingSourceJson = PrdxTestData.FullProjectJson().Replace("\"embeddedPath\": null", "\"embeddedPath\": \"source/original.dsn\"");
        var missingSource = new PrdxProjectStore().Load(PrdxTestData.CreateFixturePrdx(temp.Path, missingSourceJson));

        Assert.Contains(new PrdxProjectStore().Load(duplicate).Diagnostics, d => d.Code == DiagnosticCodes.EntryDuplicate);
        Assert.Contains(new PrdxProjectStore().Load(badUtf8).Diagnostics, d => d.Code == DiagnosticCodes.Utf8Invalid);
        Assert.Contains(tooLarge.Diagnostics, d => d.Code == DiagnosticCodes.EntryTooLarge);
        Assert.True(missingSource.Success, Messages(missingSource.Diagnostics));
        Assert.Contains(missingSource.Diagnostics, d => d.Code == DiagnosticCodes.SupplementaryMissing && !d.Blocking);
    }

    [Fact]
    public void Integrity_validator_flags_blocking_and_nonblocking_conditions()
    {
        var store = new PrdxProjectStore();
        using var temp = new TempDirectory();
        var loaded = store.Load(PrdxTestData.CreateFixturePrdx(temp.Path));
        var project = loaded.Project!;
        var validator = new CanonicalIntegrityValidator();

        var duplicateComponents = project with
        {
            LogicalDesign = project.LogicalDesign with
            {
                Components = [.. project.LogicalDesign.Components, project.LogicalDesign.Components[0]]
            }
        };

        var missingLayerRoute = project with
        {
            PhysicalDesignState = project.PhysicalDesignState with
            {
                Routes =
                [
                    new(
                        new("route_1"),
                        new("net_in"),
                        "ROUTED",
                        "REROUTABLE",
                        [new(new("trk_1"), "LINE", new("layer_missing"), new(100), new(new(0), new(0)), new(new(100), new(0)), null, null)],
                        [],
                        project.LogicalDesign.Nets[0].Provenance,
                        project.Extensions)
                ]
            }
        };

        var unresolved = new PrdxProjectStore().Load(PrdxTestData.CreateFixturePrdx(temp.Path, PrdxTestData.IncompleteProjectJson()));

        Assert.Contains(validator.Validate(duplicateComponents).Diagnostics, d => d.Code == DiagnosticCodes.DuplicateId && d.Blocking);
        Assert.Contains(validator.Validate(missingLayerRoute).Diagnostics, d => d.Code == DiagnosticCodes.LayerNotFound && d.Blocking);
        Assert.True(unresolved.Success);
        Assert.Contains(unresolved.Diagnostics, d => d.Code == DiagnosticCodes.FootprintUnresolved && !d.Blocking);
        Assert.Contains(unresolved.Diagnostics, d => d.Code == DiagnosticCodes.PadMappingUnresolved && !d.Blocking);
    }

    [Theory]
    [InlineData("constraint-missing-component", DiagnosticCodes.RefNotFound)]
    [InlineData("constraint-missing-layer", DiagnosticCodes.LayerNotFound)]
    [InlineData("constraint-missing-group", DiagnosticCodes.RefNotFound)]
    [InlineData("constraint-missing-region", DiagnosticCodes.RefNotFound)]
    [InlineData("semantic-missing-net", DiagnosticCodes.RefNotFound)]
    [InlineData("unknown-entity-type", DiagnosticCodes.EntityTypeUnknown)]
    public void Integrity_validator_rejects_invalid_constraint_and_semantic_references(string scenario, string expectedCode)
    {
        using var temp = new TempDirectory();
        var project = JsonNode.Parse(PrdxTestData.FullProjectJson())!.AsObject();

        switch (scenario)
        {
            case "constraint-missing-component":
                AddConstraint(project, sourceKind: "ENTITY", entityType: "COMPONENT", entityIds: ["cmp_missing"]);
                break;
            case "constraint-missing-layer":
                AddConstraint(project, layerIds: ["layer_missing"]);
                break;
            case "constraint-missing-group":
                AddConstraint(project, sourceKind: "GROUP", entityType: null, entityIds: ["grp_missing"]);
                break;
            case "constraint-missing-region":
                AddConstraint(project, sourceKind: "REGION", entityType: null, entityIds: ["region_missing"]);
                break;
            case "semantic-missing-net":
                AddSemanticRelationship(project, "NET", "net_missing");
                break;
            case "unknown-entity-type":
                AddConstraint(project, sourceKind: "ENTITY", entityType: "COMPONNET", entityIds: ["cmp_u1"]);
                break;
        }

        var result = new PrdxProjectStore().Load(PrdxTestData.CreateFixturePrdx(temp.Path, project.ToJsonString()));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode && d.Blocking);
    }

    [Fact]
    public void Integrity_validator_rejects_duplicate_track_semantic_relationship_and_review_decision_ids()
    {
        using var temp = new TempDirectory();
        var project = JsonNode.Parse(PrdxTestData.FullProjectJson())!.AsObject();
        AddDuplicateTracks(project);
        AddSemanticRelationship(project, "NET", "net_in");
        AddSemanticRelationship(project, "NET", "net_gnd");
        project["semantics"]!["relationships"]![1]!["id"] = "sem_rel_1";
        project["reviewDecisions"] = new JsonArray(
            ReviewDecision("review_1"),
            ReviewDecision("review_1"));

        var result = new PrdxProjectStore().Load(PrdxTestData.CreateFixturePrdx(temp.Path, project.ToJsonString()));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateId && d.Message.Contains("TRACK_SEGMENT", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateId && d.Message.Contains("SEMANTIC_RELATIONSHIP", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.DuplicateId && d.Message.Contains("REVIEW_DECISION", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostic_entity_refs_and_evidence_round_trip_through_prdx()
    {
        using var temp = new TempDirectory();
        var project = JsonNode.Parse(PrdxTestData.FullProjectJson())!.AsObject();
        AddConstraint(project);
        AddSemanticRelationship(project, "NET", "net_in");
        var sourceImport = project["sourceImports"]![0]!.AsObject();
        sourceImport["lossDiagnostics"] = new JsonArray(new JsonObject
        {
            ["code"] = "IMPORT-PIN-NAME-PARTIAL",
            ["severity"] = "WARNING",
            ["category"] = "Import",
            ["message"] = "Pin names were partially inferred.",
            ["entityRefs"] = new JsonArray(new JsonObject
            {
                ["entityType"] = "COMPONENT",
                ["entityId"] = "cmp_u1"
            }),
            ["evidence"] = new JsonObject
            {
                ["adapter"] = "fixture",
                ["pin"] = 24,
                ["nested"] = new JsonObject { ["ok"] = true }
            },
            ["remediation"] = "Review pin names.",
            ["source"] = "fixture",
            ["blocking"] = false
        });

        var store = new PrdxProjectStore();
        var loaded = store.Load(PrdxTestData.CreateFixturePrdx(temp.Path, project.ToJsonString()));
        Assert.True(loaded.Success, Messages(loaded.Diagnostics));
        var diagnostic = loaded.Project!.SourceImports.Single().LossDiagnostics.Single();
        Assert.NotNull(diagnostic.EntityRefs);
        var entityRef = Assert.Single(diagnostic.EntityRefs);
        Assert.Equal("COMPONENT", entityRef.EntityType);
        Assert.Equal("cmp_u1", entityRef.EntityId);
        Assert.NotNull(diagnostic.Evidence);
        Assert.True(diagnostic.Evidence!.ContainsKey("nested"));

        var saveAs = Path.Combine(temp.Path, "roundtrip.prdx");
        Assert.True(store.Save(loaded.Document!, saveAs).Success);
        var reopened = store.Load(saveAs);
        Assert.True(reopened.Success, Messages(reopened.Diagnostics));
        var reopenedDiagnostic = reopened.Project!.SourceImports.Single().LossDiagnostics.Single();

        Assert.Equal(diagnostic.EntityRefs, reopenedDiagnostic.EntityRefs);
        Assert.Equal(EvidenceJson(diagnostic), EvidenceJson(reopenedDiagnostic));
        Assert.Single(reopened.Project.Constraints);
        Assert.Single(reopened.Project.Semantics.Relationships);
    }

    [Fact]
    public void Save_reopen_is_semantically_equal_and_save_as_preserves_supplementary_entries()
    {
        using var temp = new TempDirectory();
        var extras = new[]
        {
            ("source/original.dsn", Encoding.UTF8.GetBytes("dsn-source")),
            ("assets/reference.png", [1, 2, 3, 4]),
            ("attachments/note.txt", Encoding.UTF8.GetBytes("note"))
        };
        var source = PrdxTestData.CreateFixturePrdx(temp.Path, extras: extras);
        var store = new PrdxProjectStore();
        var loaded = store.Load(source);
        Assert.True(loaded.Success, Messages(loaded.Diagnostics));

        var saveAs = Path.Combine(temp.Path, "copy.prdx");
        var save = store.Save(loaded.Document!, saveAs);
        Assert.True(save.Success, Messages(save.Diagnostics));

        var reopened = store.Load(saveAs);
        Assert.True(reopened.Success, Messages(reopened.Diagnostics));
        Assert.Equal(loaded.Project!.Summary, reopened.Project!.Summary);
        Assert.Equal(loaded.Document!.FileContext.SourceFingerprints, reopened.Document!.FileContext.SourceFingerprints);

        using var copiedArchive = ZipFile.OpenRead(saveAs);
        foreach (var (path, bytes) in extras)
        {
            using var stream = copiedArchive.GetEntry(path)!.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            Assert.Equal(bytes, memory.ToArray());
        }

        var second = Path.Combine(temp.Path, "copy2.prdx");
        Assert.True(store.Save(reopened.Document!, second).Success);
        Assert.Equal(ProjectPayloadHash(saveAs), ProjectPayloadHash(second));
    }

    [Fact]
    public void Commit_failure_after_temp_validation_preserves_original()
    {
        using var temp = new TempDirectory();
        var path = PrdxTestData.CreateFixturePrdx(temp.Path);
        var originalBytes = File.ReadAllBytes(path);
        var store = new PrdxProjectStore();
        var loaded = store.Load(path);
        var committer = new ThrowingCommitter();

        var result = new PrdxProjectStore(committer: committer).Save(loaded.Document!, path);

        Assert.True(committer.Invoked);
        Assert.False(result.Success);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(store.Load(path).Success);
    }

    [Fact]
    public void Cli_validate_and_inspect_are_testable_in_process()
    {
        using var temp = new TempDirectory();
        var valid = PrdxTestData.CreateFixturePrdx(temp.Path);
        var invalid = PrdxTestData.CreateFixturePrdx(temp.Path, mutateManifest: m => m["payloadSha256"] = new string('0', 64));
        var service = new ProjectService(new PrdxProjectStore(), new CanonicalIntegrityValidator());

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var cli = new CliApplication(service, stdout, stderr, "test");

        Assert.Equal(0, cli.Run(["validate", valid]));
        Assert.Equal(2, cli.Run(["validate", invalid]));
        Assert.Equal(2, cli.Run(["validate", valid, "--unknown"]));

        stdout.GetStringBuilder().Clear();
        Assert.Equal(0, cli.Run(["inspect", valid, "--json"]));
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("0.1.0", json.RootElement.GetProperty("formatVersion").GetString());
        Assert.Equal("0.1.0", json.RootElement.GetProperty("schemaVersion").GetString());
    }

    private static string ProjectPayloadHash(string prdx)
    {
        using var archive = ZipFile.OpenRead(prdx);
        using var stream = archive.GetEntry("project.json")!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return PrdxTestData.Sha256(memory.ToArray());
    }

    private static string Messages(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private static void AddConstraint(JsonObject project, string sourceKind = "ENTITY", string? entityType = "COMPONENT", string[]? entityIds = null, string[]? layerIds = null)
    {
        project["constraints"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "constraint_1",
            ["type"] = "MinimumSeparation",
            ["sourceSelector"] = new JsonObject
            {
                ["kind"] = sourceKind,
                ["entityType"] = entityType,
                ["entityIds"] = JsonArray(entityIds ?? ["cmp_u1"]),
                ["query"] = null
            },
            ["targetSelector"] = null,
            ["parameters"] = new JsonObject { ["distanceUnits"] = 1000 },
            ["enforcement"] = "REQUIRED",
            ["scope"] = new JsonObject
            {
                ["layerIds"] = JsonArray(layerIds ?? ["layer_top_cu"]),
                ["measurement"] = null,
                ["projectionMode"] = null,
                ["geometryTypes"] = new JsonArray()
            },
            ["provenance"] = Provenance(),
            ["reason"] = null,
            ["enabled"] = true
        });
    }

    private static void AddSemanticRelationship(JsonObject project, string entityType, string entityId)
    {
        project["semantics"]!["relationships"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "sem_rel_" + (project["semantics"]!["relationships"]!.AsArray().Count + 1),
            ["type"] = "ADC_REFERENCE",
            ["entityRefs"] = new JsonArray(new JsonObject
            {
                ["role"] = "target",
                ["entityType"] = entityType,
                ["entityId"] = entityId
            }),
            ["properties"] = new JsonObject(),
            ["confidence"] = 1.0,
            ["evidenceRefs"] = new JsonArray(),
            ["provenance"] = Provenance()
        });
    }

    private static void AddDuplicateTracks(JsonObject project)
    {
        var physical = project["physicalDesignState"]!.AsObject();
        physical["routes"] = new JsonArray(
            Route("route_a", "net_in"),
            Route("route_b", "net_gnd"));
    }

    private static JsonObject Route(string routeId, string netId) => new()
    {
        ["id"] = routeId,
        ["netId"] = netId,
        ["status"] = "ROUTED",
        ["policy"] = "REROUTABLE",
        ["trackSegments"] = new JsonArray(new JsonObject
        {
            ["id"] = "trk_001",
            ["geometryKind"] = "LINE",
            ["layerId"] = "layer_top_cu",
            ["widthUnits"] = 150,
            ["start"] = new JsonObject { ["x"] = 0, ["y"] = 0 },
            ["end"] = new JsonObject { ["x"] = 1000, ["y"] = 0 },
            ["arcCenter"] = null,
            ["clockwise"] = null
        }),
        ["viaIds"] = new JsonArray(),
        ["provenance"] = Provenance(),
        ["metadata"] = new JsonObject()
    };

    private static JsonObject ReviewDecision(string id) => new()
    {
        ["id"] = id,
        ["decisionType"] = "NOTE",
        ["fingerprint"] = "fp-" + id,
        ["entityRefs"] = new JsonArray("cmp_u1"),
        ["reason"] = "test",
        ["createdAt"] = "2026-08-14T20:00:00Z",
        ["createdBy"] = "test"
    };

    private static JsonObject Provenance() => new()
    {
        ["kind"] = "USER_DEFINED",
        ["sourceRef"] = null,
        ["model"] = null,
        ["operation"] = null,
        ["timestamp"] = null,
        ["note"] = null
    };

    private static JsonArray JsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string EvidenceJson(Diagnostic diagnostic)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in diagnostic.Evidence!.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            obj[key] = value is JsonElement element
                ? JsonNode.Parse(element.GetRawText())
                : JsonSerializer.SerializeToNode(value);
        }

        return obj.ToJsonString();
    }

    private sealed class ThrowingCommitter : IAtomicFileCommitter
    {
        public bool Invoked { get; private set; }

        public void Commit(string tempPath, string destinationPath)
        {
            Invoked = true;
            Assert.True(File.Exists(tempPath));
            throw new IOException("simulated commit failure");
        }
    }
}
