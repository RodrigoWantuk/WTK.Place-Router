using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.DesignExchange.Specctra;

public sealed class SpecctraDsnImporter : IDesignImporter
{
    public string AdapterId => "placerouter.specctra-dsn";

    public string AdapterVersion => "0.1.0-plan03";

    public string SourceType => "SPECCTRA_DSN";

    public bool CanImport(ImportRequest request) =>
        string.Equals(Path.GetExtension(request.SourcePath), ".dsn", StringComparison.OrdinalIgnoreCase);

    public ImportResult Import(ImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
        {
            var diagnostic = Diagnostic.Fatal(
                DiagnosticCodes.ImportInvalidSource,
                "Import",
                $"DSN source '{request.SourcePath}' does not exist.");
            return new ImportResult(null, new Dictionary<string, string>(StringComparer.Ordinal), [diagnostic], null, new ImportLossReport([]));
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(request.SourcePath);
            var sourceText = Encoding.UTF8.GetString(sourceBytes);
            var root = SExpression.Parse(sourceText);
            if (!root.IsList("pcb"))
            {
                throw new InvalidDataException("Root DSN expression must be '(pcb ...)'.");
            }

            var sourceHash = Sha256.Hex(sourceBytes);
            var importedAt = DateTimeOffset.UtcNow;
            var sourceImportId = new SourceImportId("src_" + sourceHash[..12]);
            var embeddedPath = request.SourceRetentionPolicy == SourceRetentionPolicy.Embed
                ? "source/" + Path.GetFileName(request.SourcePath)
                : null;
            var capabilities = new Dictionary<string, string>(StringComparer.Ordinal);
            var losses = new List<Diagnostic>();

            var project = ToProject(root, request.SourcePath, sourceHash, sourceImportId, embeddedPath, importedAt, capabilities, losses);
            var fileContext = new ProjectFileContext(
                null,
                CanonicalProject.CurrentSchemaVersion,
                [],
                [new SourceFingerprint(sourceImportId, sourceHash)],
                [],
                request.SourceRetentionPolicy == SourceRetentionPolicy.Embed
                    ? [new PendingSupplementaryFile(Path.GetFullPath(request.SourcePath), embeddedPath!)]
                    : []);

            return new ImportResult(
                new ProjectDocument(project, fileContext),
                capabilities,
                losses,
                new SourceFingerprint(sourceImportId, sourceHash),
                new ImportLossReport(losses));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException)
        {
            var diagnostic = new Diagnostic(
                DiagnosticCodes.ImportInvalidSource,
                DiagnosticSeverity.Error,
                "Import",
                $"DSN import failed: {ex.Message}",
                Blocking: true);
            return new ImportResult(null, new Dictionary<string, string>(StringComparer.Ordinal), [diagnostic], null, new ImportLossReport([]));
        }
    }

    private CanonicalProject ToProject(
        SExpression root,
        string sourcePath,
        string sourceHash,
        SourceImportId sourceImportId,
        string? embeddedPath,
        DateTimeOffset importedAt,
        IDictionary<string, string> capabilities,
        List<Diagnostic> losses)
    {
        var name = root.AtomAt(1) ?? Path.GetFileNameWithoutExtension(sourcePath);
        var projectId = new ProjectId("prj_" + StableToken(name + "_" + sourceHash[..12]));
        var units = ReadUnits(root);
        var layers = ReadLayers(root, losses).ToArray();
        var layerByName = layers.ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);
        var outline = ReadOutline(root, units);
        var components = new List<Component>();
        var poses = new List<ComponentPose>();
        var footprints = new Dictionary<string, Footprint>(StringComparer.Ordinal);
        var componentPadIds = new Dictionary<string, Dictionary<string, PadId>>(StringComparer.OrdinalIgnoreCase);
        var libraryImages = ReadLibraryImages(root, layerByName, units, sourceImportId, losses);

        foreach (var componentNode in root.Descendants("component"))
        {
            ImportComponentNode(
                componentNode,
                libraryImages,
                layerByName,
                units,
                sourceImportId,
                components,
                poses,
                footprints,
                componentPadIds,
                losses);
        }

        foreach (var image in libraryImages.Values)
        {
            if (!footprints.ContainsKey(image.Footprint.Id.Value))
            {
                footprints[image.Footprint.Id.Value] = image.Footprint;
            }
        }

        var nets = ReadNets(root, componentPadIds).ToArray();
        var rules = ReadRules(root, units);
        SetCapabilities(capabilities, outline, layers, components, footprints.Values.ToArray(), poses, nets, rules, losses);
        var manufacturing = new ManufacturingProfile(
            new StableId("mfg_imported_" + sourceHash[..8]),
            "Imported DSN manufacturing rules",
            "0.1",
            null,
            null,
            rules,
            ImportedProvenance(sourceImportId));
        var sourceImport = new SourceImport(
            sourceImportId,
            AdapterId,
            AdapterVersion,
            SourceType,
            Path.GetFileName(sourcePath),
            sourceHash,
            importedAt,
            embeddedPath,
            capabilities.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
            losses);

        return new CanonicalProject(
            CanonicalProject.CurrentSchemaVersion,
            projectId,
            0,
            new ProjectMetadata(name, "Imported from Specctra DSN.", importedAt, importedAt, null, ["imported", "dsn"]),
            [sourceImport],
            new LogicalDesign(components.OrderBy(c => c.ReferenceDesignator, StringComparer.Ordinal).ToArray(), footprints.Values.OrderBy(f => f.Id.Value, StringComparer.Ordinal).ToArray(), nets, [], []),
            new BoardDefinition(
                ZeroPoint(),
                outline,
                [],
                [],
                null,
                SourcedValue.Unknown(),
                layers,
                layers.Select(l => new StackupEntry(l.Id, [])).ToArray(),
                [],
                []),
            manufacturing,
            [],
            new Semantics([]),
            new PhysicalDesignState(
                new PhysicalStateId("state_imported"),
                0,
                "UNROUTED",
                0,
                poses.OrderBy(p => p.ComponentId.Value, StringComparer.Ordinal).ToArray(),
                [],
                [],
                [],
                importedAt,
                AdapterId),
            [],
            new ProjectSettings(JsonDefaults.EmptyObject, [], embeddedPath is null ? "REFERENCE_ONLY" : "EMBED"),
            JsonDefaults.EmptyObject);
    }

    private void ImportComponentNode(
        SExpression componentNode,
        IReadOnlyDictionary<string, LibraryImage> libraryImages,
        IReadOnlyDictionary<string, LayerId> layerByName,
        UnitScale units,
        SourceImportId sourceImportId,
        List<Component> components,
        List<ComponentPose> poses,
        Dictionary<string, Footprint> footprints,
        Dictionary<string, Dictionary<string, PadId>> componentPadIds,
        List<Diagnostic> losses)
    {
        var firstAtom = componentNode.AtomAt(1) ?? throw new InvalidDataException("Component without identifier.");
        var placeChildren = componentNode.Children.Where(c => c.IsList("place")).ToArray();
        if (placeChildren.Length > 0 && componentNode.Child("footprint") is null && libraryImages.Count > 0)
        {
            ImportSpecctraPlacementComponent(componentNode, firstAtom, placeChildren, libraryImages, components, poses, footprints, componentPadIds, losses);
            return;
        }

        ImportInlineComponent(componentNode, firstAtom, layerByName, units, sourceImportId, components, poses, footprints, componentPadIds, losses);
    }

    private void ImportInlineComponent(
        SExpression componentNode,
        string reference,
        IReadOnlyDictionary<string, LayerId> layerByName,
        UnitScale units,
        SourceImportId sourceImportId,
        List<Component> components,
        List<ComponentPose> poses,
        Dictionary<string, Footprint> footprints,
        Dictionary<string, Dictionary<string, PadId>> componentPadIds,
        List<Diagnostic> losses)
    {
        var footprintName = componentNode.Child("footprint")?.AtomAt(1);
        FootprintId? footprintId = string.IsNullOrWhiteSpace(footprintName)
            ? null
            : new FootprintId("fp_" + StableToken(footprintName));
        var componentId = new ComponentId("cmp_" + StableToken(reference));
        var pads = footprintId is null
            ? []
            : ReadInlinePads(componentNode, footprintId.Value, layerByName, units, losses).ToArray();
        if (footprintId is not null && !footprints.ContainsKey(footprintId.Value.Value))
        {
            footprints[footprintId.Value.Value] = new Footprint(
                footprintId.Value,
                footprintName!,
                ZeroPoint(),
                null,
                BodyFromPads(pads),
                null,
                pads,
                [],
                [],
                ImportedProvenance(sourceImportId));
        }
        else if (footprintId is null)
        {
            AddLoss(losses, "footprints", $"Component '{reference}' did not provide a footprint; footprintId remains Unknown.", DiagnosticSeverity.Warning);
        }

        componentPadIds[reference] = pads.ToDictionary(p => p.Number, p => p.Id, StringComparer.OrdinalIgnoreCase);
        AddComponent(components, componentId, reference, footprintId, sourceImportId);

        var place = componentNode.Child("place");
        if (place is null)
        {
            AddLoss(losses, "componentPlacement", $"Component '{reference}' did not provide placement; no ComponentPose was created.", DiagnosticSeverity.Warning);
            return;
        }

        AddPose(reference, componentId, place, 1, units, poses, losses);
    }

    private void ImportSpecctraPlacementComponent(
        SExpression componentNode,
        string imageName,
        IReadOnlyList<SExpression> places,
        IReadOnlyDictionary<string, LibraryImage> libraryImages,
        List<Component> components,
        List<ComponentPose> poses,
        Dictionary<string, Footprint> footprints,
        Dictionary<string, Dictionary<string, PadId>> componentPadIds,
        List<Diagnostic> losses)
    {
        if (!libraryImages.TryGetValue(imageName, out var image))
        {
            AddLoss(losses, "footprints", $"Placement component references image '{imageName}', but no matching library image was found.", DiagnosticSeverity.Warning);
            return;
        }

        footprints.TryAdd(image.Footprint.Id.Value, image.Footprint);
        foreach (var place in places)
        {
            var reference = place.AtomAt(1);
            if (string.IsNullOrWhiteSpace(reference))
            {
                AddLoss(losses, "components", $"Placement for image '{imageName}' did not provide a component reference.", DiagnosticSeverity.Warning);
                continue;
            }

            var componentId = new ComponentId("cmp_" + StableToken(reference));
            componentPadIds[reference] = image.PadIdsByPinName.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            AddComponent(components, componentId, reference, image.Footprint.Id, image.SourceImportId);
            AddPose(reference, componentId, place, 2, image.Units, poses, losses);
        }
    }

    private void AddPose(string reference, ComponentId componentId, SExpression place, int coordinateStart, UnitScale units, List<ComponentPose> poses, List<Diagnostic> losses)
    {
        if (!TryRequiredUnit(place, coordinateStart, units, $"Component '{reference}' placement X is missing; no ComponentPose was created.", losses, "componentPlacement", out var x) ||
            !TryRequiredUnit(place, coordinateStart + 1, units, $"Component '{reference}' placement Y is missing; no ComponentPose was created.", losses, "componentPlacement", out var y))
        {
            return;
        }

        var sideAtom = place.AtomAt(coordinateStart + 2);
        if (sideAtom is null)
        {
            AddLoss(losses, "componentPlacement", $"Component '{reference}' placement side is missing; no ComponentPose was created.", DiagnosticSeverity.Warning);
            return;
        }

        var rotationAtom = place.AtomAt(coordinateStart + 3);
        if (rotationAtom is null)
        {
            AddLoss(losses, "componentPlacement", $"Component '{reference}' placement rotation is missing; no ComponentPose was created.", DiagnosticSeverity.Warning);
            return;
        }

        var side = sideAtom.ToLowerInvariant() switch
        {
            "front" or "top" => "TOP",
            "back" or "bottom" => "BOTTOM",
            _ => null
        };
        if (side is null)
        {
            AddLoss(losses, "componentPlacement", $"Component '{reference}' placement side '{sideAtom}' is unsupported; no ComponentPose was created.", DiagnosticSeverity.Warning);
            return;
        }

        var rotation = decimal.Parse(rotationAtom, System.Globalization.CultureInfo.InvariantCulture);
        poses.Add(new ComponentPose(componentId, new Point2(x, y), new AngleDegrees(rotation), side, "PLACED", AdapterId));
    }

    private void AddComponent(List<Component> components, ComponentId componentId, string reference, FootprintId? footprintId, SourceImportId sourceImportId)
    {
        if (components.Any(c => c.Id == componentId))
        {
            return;
        }

        components.Add(new Component(
            componentId,
            reference,
            null,
            null,
            null,
            footprintId,
            "MOVABLE",
            new Dictionary<string, SourcedValue>(StringComparer.Ordinal),
            SourcedValue.Unknown(),
            JsonObject(("sourceRef", reference)),
            ImportedProvenance(sourceImportId)));
    }

    private static UnitScale ReadUnits(SExpression root)
    {
        if (root.Child("unit")?.AtomAt(1) is { } unit)
        {
            return unit.ToLowerInvariant() switch
            {
                "um" or "micron" or "microns" => new UnitScale(unit, 1m),
                "mm" => new UnitScale(unit, 1000m),
                "mil" or "mils" => new UnitScale(unit, 25.4m),
                "inch" or "in" => new UnitScale(unit, 25_400m),
                _ => throw new InvalidDataException($"DSN unit '{unit}' is not supported.")
            };
        }

        var resolution = root.Child("resolution") ?? throw new InvalidDataException("DSN source did not declare '(unit ...)' or '(resolution ...)'.");
        var resolutionUnit = resolution.AtomAt(1) ?? throw new InvalidDataException("DSN resolution did not declare a unit.");
        var denominator = decimal.Parse(resolution.AtomAt(2) ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        if (denominator <= 0)
        {
            throw new InvalidDataException("DSN resolution denominator must be greater than zero.");
        }

        var unitMicrometers = resolutionUnit.ToLowerInvariant() switch
        {
            "um" or "micron" or "microns" => 1m,
            "mm" => 1000m,
            "mil" or "mils" => 25.4m,
            "inch" or "in" => 25_400m,
            _ => throw new InvalidDataException($"DSN resolution unit '{resolutionUnit}' is not supported.")
        };
        return new UnitScale(resolutionUnit, unitMicrometers / denominator);
    }

    private static IReadOnlyList<BoardLayer> ReadLayers(SExpression root, List<Diagnostic> losses)
    {
        return root.Descendants("layer")
            .Select((node, index) =>
            {
                var name = node.AtomAt(1) ?? throw new InvalidDataException("Layer without name.");
                var typeAtom = node.AtomAt(2) ?? node.Child("type")?.AtomAt(1);
                if (typeAtom is null)
                {
                    AddLoss(losses, "layers", $"Layer '{name}' did not provide a type; layer was not imported.", DiagnosticSeverity.Warning);
                    return null;
                }

                var type = typeAtom.ToLowerInvariant() switch
                {
                    "signal" => "COPPER_SIGNAL",
                    "plane" => "COPPER_PLANE",
                    _ => null
                };
                if (type is null)
                {
                    AddLoss(losses, "layers", $"Layer '{name}' uses unsupported type '{typeAtom}'; layer was not imported.", DiagnosticSeverity.Warning);
                    return null;
                }

                return new BoardLayer(LayerIdFor(name), name, type, index + 1, null, SourcedValue.Unknown(), new Dictionary<string, SourcedValue>(StringComparer.Ordinal));
            })
            .Where(layer => layer is not null)
            .Select(layer => layer!)
            .ToArray();
    }

    private static Polygon2? ReadOutline(SExpression root, UnitScale units)
    {
        var path = root.Descendants("boundary").SelectMany(b => b.Children.Where(c => c.IsList("path"))).FirstOrDefault()
            ?? root.Descendants("path").FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        var numbers = path.Items.Skip(2).Where(i => i.IsAtom).Select(i => Unit(i.Value, units).Value).ToArray();
        if (numbers.Length < 6 || numbers.Length % 2 != 0)
        {
            throw new InvalidDataException("Boundary path must contain at least three coordinate pairs.");
        }

        var points = new List<Point2>();
        for (var i = 0; i < numbers.Length; i += 2)
        {
            points.Add(new Point2(new LengthUnits(numbers[i]), new LengthUnits(numbers[i + 1])));
        }

        return new Polygon2(points, []);
    }

    private static IReadOnlyDictionary<string, LibraryImage> ReadLibraryImages(
        SExpression root,
        IReadOnlyDictionary<string, LayerId> layerByName,
        UnitScale units,
        SourceImportId sourceImportId,
        List<Diagnostic> losses)
    {
        var padstacks = ReadPadstacks(root, layerByName, units, losses);
        var images = new Dictionary<string, LibraryImage>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageNode in root.Descendants("image"))
        {
            var imageName = imageNode.AtomAt(1);
            if (string.IsNullOrWhiteSpace(imageName))
            {
                AddLoss(losses, "footprints", "Library image did not provide a name; image was skipped.", DiagnosticSeverity.Warning);
                continue;
            }

            var footprintId = new FootprintId("fp_" + StableToken(imageName));
            var pads = new List<Pad>();
            foreach (var pin in imageNode.Children.Where(c => c.IsList("pin")))
            {
                var padstackName = pin.AtomAt(1);
                var pinName = pin.AtomAt(2);
                if (string.IsNullOrWhiteSpace(padstackName) || string.IsNullOrWhiteSpace(pinName))
                {
                    AddLoss(losses, "pads", $"Image '{imageName}' contains a pin without padstack or pin name; pin was skipped.", DiagnosticSeverity.Warning);
                    continue;
                }

                if (!padstacks.TryGetValue(padstackName, out var padstack))
                {
                    AddLoss(losses, "pads", $"Image '{imageName}' pin '{pinName}' references missing padstack '{padstackName}'; pin was skipped.", DiagnosticSeverity.Warning);
                    continue;
                }

                if (!TryRequiredUnit(pin, 3, units, $"Image '{imageName}' pin '{pinName}' position X is missing; pin was skipped.", losses, "pads", out var x) ||
                    !TryRequiredUnit(pin, 4, units, $"Image '{imageName}' pin '{pinName}' position Y is missing; pin was skipped.", losses, "pads", out var y))
                {
                    continue;
                }

                var rotation = pin.AtomAt(5) is { } rawRotation
                    ? decimal.Parse(rawRotation, System.Globalization.CultureInfo.InvariantCulture)
                    : 0m;
                pads.Add(new Pad(
                    new PadId("pad_" + StableToken(footprintId.Value + "_" + pinName)),
                    pinName,
                    null,
                    pinName,
                    new Point2(x, y),
                    new AngleDegrees(rotation),
                    padstack.Shape,
                    padstack.SizeX,
                    padstack.SizeY,
                    null,
                    padstack.PadType,
                    padstack.LayerIds,
                    null,
                    null,
                    null));
            }

            var footprint = new Footprint(
                footprintId,
                imageName,
                ZeroPoint(),
                null,
                BodyFromPads(pads),
                null,
                pads,
                [],
                [],
                ImportedProvenance(sourceImportId));
            images[imageName] = new LibraryImage(
                footprint,
                pads.ToDictionary(p => p.Number, p => p.Id, StringComparer.OrdinalIgnoreCase),
                units,
                sourceImportId);
        }

        return images;
    }

    private static IReadOnlyDictionary<string, PadstackDefinition> ReadPadstacks(
        SExpression root,
        IReadOnlyDictionary<string, LayerId> layerByName,
        UnitScale units,
        List<Diagnostic> losses)
    {
        var padstacks = new Dictionary<string, PadstackDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var padstack in root.Descendants("padstack"))
        {
            var name = padstack.AtomAt(1);
            if (string.IsNullOrWhiteSpace(name))
            {
                AddLoss(losses, "pads", "Padstack without name was skipped.", DiagnosticSeverity.Warning);
                continue;
            }

            var shapeNode = padstack.Children.FirstOrDefault(c => c.IsList("shape"));
            if (shapeNode is null || !TryReadPadstackShape(shapeNode, layerByName, units, losses, out var definition))
            {
                AddLoss(losses, "pads", $"Padstack '{name}' does not contain a supported shape; pins using it will be skipped.", DiagnosticSeverity.Warning);
                continue;
            }

            padstacks[name] = definition;
        }

        return padstacks;
    }

    private static bool TryReadPadstackShape(
        SExpression shapeNode,
        IReadOnlyDictionary<string, LayerId> layerByName,
        UnitScale units,
        List<Diagnostic> losses,
        out PadstackDefinition definition)
    {
        definition = default!;
        var primitive = shapeNode.Children.FirstOrDefault();
        var kind = primitive?.AtomAt(0) ?? shapeNode.AtomAt(1);
        var layerName = primitive is null ? shapeNode.AtomAt(2) : primitive.AtomAt(1);
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(layerName))
        {
            return false;
        }

        var values = (primitive is null ? shapeNode.Items.Skip(3) : primitive.Items.Skip(2))
            .Where(i => i.IsAtom)
            .Select(i => Unit(i.Value, units).Value)
            .ToArray();
        var layerIds = LayerIdsForPadstackLayer(layerName, layerByName, losses);
        if (layerIds.Count == 0)
        {
            return false;
        }

        switch (kind.ToLowerInvariant())
        {
            case "rect" when values.Length >= 4:
                definition = new PadstackDefinition("RECT", new LengthUnits(Math.Abs(values[2] - values[0])), new LengthUnits(Math.Abs(values[3] - values[1])), PadTypeFor(layerName, layerIds), layerIds);
                return true;
            case "circle" when values.Length >= 1:
                definition = new PadstackDefinition("CIRCLE", new LengthUnits(Math.Abs(values[0])), new LengthUnits(Math.Abs(values[0])), PadTypeFor(layerName, layerIds), layerIds);
                return true;
            case "oval" when values.Length >= 2:
                definition = new PadstackDefinition("OVAL", new LengthUnits(Math.Abs(values[0])), new LengthUnits(Math.Abs(values[1])), PadTypeFor(layerName, layerIds), layerIds);
                return true;
            default:
                AddLoss(losses, "pads", $"Padstack shape '{kind}' is not supported.", DiagnosticSeverity.Warning);
                return false;
        }
    }

    private static IReadOnlyList<LayerId> LayerIdsForPadstackLayer(string layerName, IReadOnlyDictionary<string, LayerId> layerByName, List<Diagnostic> losses)
    {
        if (layerName.Equals("signal", StringComparison.OrdinalIgnoreCase) ||
            layerName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return layerByName.Values.ToArray();
        }

        if (layerByName.TryGetValue(layerName, out var layerId))
        {
            return [layerId];
        }

        AddLoss(losses, "layers", $"Padstack references undeclared layer '{layerName}'; shape was skipped.", DiagnosticSeverity.Warning);
        return [];
    }

    private static string PadTypeFor(string layerName, IReadOnlyList<LayerId> layerIds) =>
        layerName.Equals("signal", StringComparison.OrdinalIgnoreCase) || layerIds.Count > 1 ? "THROUGH_HOLE" : "SMD";

    private static IEnumerable<Pad> ReadInlinePads(SExpression componentNode, FootprintId footprintId, IReadOnlyDictionary<string, LayerId> layerByName, UnitScale units, List<Diagnostic> losses)
    {
        foreach (var pad in componentNode.Children.Where(c => c.IsList("pad")))
        {
            var number = pad.AtomAt(1) ?? throw new InvalidDataException("Pad without pin number.");
            var padTypeAtom = pad.AtomAt(2);
            if (padTypeAtom is null)
            {
                AddLoss(losses, "pads", $"Pad '{number}' did not provide a type; pad was not imported.", DiagnosticSeverity.Warning);
                continue;
            }

            var shapeAtom = pad.AtomAt(3);
            if (shapeAtom is null)
            {
                AddLoss(losses, "pads", $"Pad '{number}' did not provide a shape; pad was not imported.", DiagnosticSeverity.Warning);
                continue;
            }

            var padType = padTypeAtom.ToLowerInvariant() switch
            {
                "smd" => "SMD",
                "thru" or "through" or "through_hole" => "THROUGH_HOLE",
                _ => null
            };
            if (padType is null)
            {
                AddLoss(losses, "pads", $"Pad '{number}' uses unsupported type '{padTypeAtom}'; pad was not imported.", DiagnosticSeverity.Warning);
                continue;
            }

            string shape;
            switch (shapeAtom.ToUpperInvariant())
            {
                case "CIRCLE":
                case "ROUND":
                    shape = "CIRCLE";
                    break;
                case "OVAL":
                    shape = "OVAL";
                    break;
                case "RECT":
                    shape = "RECT";
                    break;
                default:
                    AddLoss(losses, "pads", $"Pad '{number}' uses unsupported shape '{shapeAtom}'; pad was not imported.", DiagnosticSeverity.Warning);
                    continue;
            }

            if (!TryRequiredUnit(pad, 4, units, $"Pad '{number}' position X is missing; pad was not imported.", losses, "pads", out var x) ||
                !TryRequiredUnit(pad, 5, units, $"Pad '{number}' position Y is missing; pad was not imported.", losses, "pads", out var y))
            {
                continue;
            }

            var sizeX = OptionalUnit(pad.AtomAt(6), units);
            if (sizeX is null)
            {
                AddLoss(losses, "pads", $"Pad '{number}' size X is missing; pad was not imported.", DiagnosticSeverity.Warning);
                continue;
            }

            var sizeY = OptionalUnit(pad.AtomAt(7), units);
            if (sizeY is null && shape is not "CIRCLE")
            {
                AddLoss(losses, "pads", $"Pad '{number}' size Y is missing; pad was not imported.", DiagnosticSeverity.Warning);
                continue;
            }

            sizeY ??= sizeX;
            var layerName = pad.AtomAt(8);
            var layerIds = Array.Empty<LayerId>();
            if (layerName is null)
            {
                AddLoss(losses, "pads", $"Pad '{number}' did not provide a layer; pad layerIds remain empty.", DiagnosticSeverity.Warning);
            }
            else if (layerByName.TryGetValue(layerName, out var existingLayer))
            {
                layerIds = [existingLayer];
            }
            else
            {
                AddLoss(losses, "layers", $"Pad '{number}' references undeclared layer '{layerName}'; pad layerIds remain empty.", DiagnosticSeverity.Warning);
            }

            yield return new Pad(
                new PadId("pad_" + StableToken(footprintId.Value + "_" + number)),
                number,
                null,
                number,
                new Point2(x, y),
                AngleDegrees.Zero,
                shape,
                sizeX,
                sizeY,
                null,
                padType,
                layerIds,
                null,
                null,
                null);
        }
    }

    private static IReadOnlyList<Net> ReadNets(SExpression root, IReadOnlyDictionary<string, Dictionary<string, PadId>> componentPadIds)
    {
        return root.Descendants("net")
            .Select(net =>
            {
                var name = net.AtomAt(1) ?? "unnamed";
                var pins = net.Child("pins")?.Items.Skip(1).Where(i => i.IsAtom).Select(i => i.Value) ?? [];
                var endpoints = pins.Select(pin =>
                {
                    var parts = SplitPinRef(pin);
                    var reference = parts[0];
                    var number = parts.Length == 2 ? parts[1] : null;
                    componentPadIds.TryGetValue(reference, out var pads);
                    PadId? padId = number is not null && pads is not null && pads.TryGetValue(number, out var found) ? found : null;
                    return new NetEndpoint(new ComponentId("cmp_" + StableToken(reference)), padId, number);
                }).ToArray();
                return new Net(
                    new NetId("net_" + StableToken(name)),
                    name,
                    endpoints,
                    null,
                    new Dictionary<string, SourcedValue>(StringComparer.Ordinal),
                    new Dictionary<string, SourcedValue>(StringComparer.Ordinal),
                    Provenance.Unknown);
            })
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitPinRef(string pin)
    {
        foreach (var separator in new[] { '-', '.', ':' })
        {
            var parts = pin.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return parts;
            }
        }

        return [pin];
    }

    private static IReadOnlyDictionary<string, SourcedValue> ReadRules(SExpression root, UnitScale units)
    {
        var rules = root.Descendants("rules").FirstOrDefault();
        var values = new Dictionary<string, SourcedValue>(StringComparer.Ordinal);
        if (rules is null)
        {
            return values;
        }

        Add("width", "minimumTrackWidth");
        Add("clearance", "minimumClearance");
        Add("drill", "minimumDrill");
        Add("via", "minimumViaDiameter");
        return values;

        void Add(string dsnKey, string capabilityKey)
        {
            var node = rules.Child(dsnKey);
            var raw = node?.AtomAt(1);
            if (raw is null)
            {
                return;
            }

            values[capabilityKey] = KnownNumber(Unit(raw, units).Value);
        }
    }

    private static Polygon2? BodyFromPads(IReadOnlyList<Pad> pads)
    {
        if (pads.Count == 0)
        {
            return null;
        }

        var minX = pads.Min(p => p.Position.X.Value - (p.SizeX?.Value ?? 0) / 2);
        var maxX = pads.Max(p => p.Position.X.Value + (p.SizeX?.Value ?? 0) / 2);
        var minY = pads.Min(p => p.Position.Y.Value - (p.SizeY?.Value ?? 0) / 2);
        var maxY = pads.Max(p => p.Position.Y.Value + (p.SizeY?.Value ?? 0) / 2);
        return new Polygon2([
            new Point2(new LengthUnits(minX), new LengthUnits(minY)),
            new Point2(new LengthUnits(maxX), new LengthUnits(minY)),
            new Point2(new LengthUnits(maxX), new LengthUnits(maxY)),
            new Point2(new LengthUnits(minX), new LengthUnits(maxY))
        ], []);
    }

    private static LayerId LayerIdFor(string name)
    {
        if (name.Equals("Top", StringComparison.OrdinalIgnoreCase))
        {
            return new LayerId("layer_top_cu");
        }

        if (name.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
        {
            return new LayerId("layer_bottom_cu");
        }

        return new LayerId("layer_" + StableToken(name) + "_cu");
    }

    private static string StableToken(string value)
    {
        var token = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(token) ? "unnamed" : token;
    }

    private static LengthUnits Unit(string value, UnitScale units)
    {
        var raw = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var micrometers = decimal.ToInt64(decimal.Round(raw * units.MicrometersPerUnit, 0, MidpointRounding.AwayFromZero));
        return new LengthUnits(micrometers);
    }

    private static bool TryRequiredUnit(
        SExpression node,
        int atomIndex,
        UnitScale units,
        string message,
        List<Diagnostic> losses,
        string capability,
        out LengthUnits value)
    {
        if (node.AtomAt(atomIndex) is { } raw)
        {
            value = Unit(raw, units);
            return true;
        }

        value = LengthUnits.FromMicrometers(0);
        AddLoss(losses, capability, message, DiagnosticSeverity.Warning);
        return false;
    }

    private static LengthUnits? OptionalUnit(string? value, UnitScale units) =>
        value is null ? null : Unit(value, units);

    private sealed record UnitScale(string Name, decimal MicrometersPerUnit);

    private static Point2 ZeroPoint() => new(LengthUnits.FromMicrometers(0), LengthUnits.FromMicrometers(0));

    private static SourcedValue KnownNumber(long value) =>
        new(JsonSerializer.SerializeToElement(value), "um", "KNOWN", 1.0, Provenance.UserDefined);

    private static Provenance ImportedProvenance(SourceImportId sourceImportId) =>
        new("IMPORTED", sourceImportId.Value, null, "dsn-import", null, null);

    private static JsonElement JsonObject(params (string Key, string Value)[] values)
    {
        var dictionary = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        return JsonSerializer.SerializeToElement(dictionary);
    }

    private static void SetCapabilities(
        IDictionary<string, string> capabilities,
        Polygon2? outline,
        IReadOnlyList<BoardLayer> layers,
        IReadOnlyList<Component> components,
        IReadOnlyList<Footprint> footprints,
        IReadOnlyList<ComponentPose> poses,
        IReadOnlyList<Net> nets,
        IReadOnlyDictionary<string, SourcedValue> rules,
        List<Diagnostic> losses)
    {
        capabilities["boardOutline"] = outline is null ? "MISSING" : "COMPLETE";
        capabilities["layers"] = layers.Count == 0 ? "MISSING" : "COMPLETE";
        capabilities["components"] = components.Count == 0 ? "MISSING" : "COMPLETE";
        capabilities["footprints"] = components.Count == 0 ? "NOT_APPLICABLE" : components.All(c => c.FootprintId is not null) ? "COMPLETE" : components.All(c => c.FootprintId is null) ? "MISSING" : "PARTIAL";
        capabilities["componentPlacement"] = components.Count == 0 ? "NOT_APPLICABLE" : poses.Count == components.Count ? "COMPLETE" : poses.Count == 0 ? "MISSING" : "PARTIAL";
        capabilities["pads"] = footprints.Count == 0 ? "MISSING" : footprints.All(f => f.Pads.Count > 0) ? "COMPLETE" : "PARTIAL";
        capabilities["nets"] = nets.Count == 0 ? "MISSING" : "COMPLETE";
        capabilities["rules"] = rules.Count == 0 ? "MISSING" : "PARTIAL";
        capabilities["stackupMaterials"] = "MISSING";
        capabilities["existingRoutes"] = "NOT_AVAILABLE";

        if (outline is null) AddLoss(losses, "boardOutline", "DSN source did not provide a board outline.", DiagnosticSeverity.Warning);
        if (layers.Count == 0) AddLoss(losses, "layers", "DSN source did not provide layer definitions; no layers were invented.", DiagnosticSeverity.Warning);
        if (rules.Count == 0) AddLoss(losses, "rules", "DSN source did not provide manufacturing routing rules.", DiagnosticSeverity.Warning);
        AddLoss(losses, "stackupMaterials", "DSN source did not provide stackup material/thickness data; values remain Unknown.", DiagnosticSeverity.Warning);
        AddLoss(losses, "existingRoutes", "Initial DSN import preserves logical/placement handoff and does not import existing route geometry.", DiagnosticSeverity.Info);
    }

    private static void AddLoss(List<Diagnostic> losses, string capability, string message, DiagnosticSeverity severity)
    {
        if (losses.Any(d => Equals(d.Evidence?.GetValueOrDefault("capability"), capability) && d.Message == message))
        {
            return;
        }

        losses.Add(new Diagnostic(
            DiagnosticCodes.ImportLoss,
            severity,
            "Import",
            message,
            Evidence: new Dictionary<string, object?> { ["capability"] = capability },
            Source: "placerouter.specctra-dsn",
            Blocking: false));
    }

    private sealed record LibraryImage(
        Footprint Footprint,
        IReadOnlyDictionary<string, PadId> PadIdsByPinName,
        UnitScale Units,
        SourceImportId SourceImportId);

    private sealed record PadstackDefinition(
        string Shape,
        LengthUnits SizeX,
        LengthUnits SizeY,
        string PadType,
        IReadOnlyList<LayerId> LayerIds);

    private sealed class SExpression
    {
        private SExpression(string value, IReadOnlyList<SExpression> items)
        {
            Value = value;
            Items = items;
        }

        public string Value { get; }

        public IReadOnlyList<SExpression> Items { get; }

        public IReadOnlyList<SExpression> Children => Items.Skip(1).Where(i => !i.IsAtom).ToArray();

        public bool IsAtom => Items.Count == 0;

        public static SExpression Atom(string value) => new(value, []);

        public static SExpression List(IReadOnlyList<SExpression> items) => new(string.Empty, items);

        public static SExpression Parse(string source)
        {
            var tokens = Tokenize(source).ToArray();
            var index = 0;
            var expression = ParseExpression(tokens, ref index);
            if (index != tokens.Length)
            {
                throw new InvalidDataException("Unexpected tokens after DSN root expression.");
            }

            return expression;
        }

        public bool IsList(string head) =>
            !IsAtom && Items.Count > 0 && Items[0].IsAtom && Items[0].Value.Equals(head, StringComparison.OrdinalIgnoreCase);

        public string? AtomAt(int index) =>
            Items.Count > index && Items[index].IsAtom ? Items[index].Value : null;

        public SExpression? Child(string head) =>
            Children.FirstOrDefault(c => c.IsList(head));

        public IEnumerable<SExpression> Descendants(string head)
        {
            foreach (var child in Children)
            {
                if (child.IsList(head))
                {
                    yield return child;
                }

                foreach (var descendant in child.Descendants(head))
                {
                    yield return descendant;
                }
            }
        }

        private static SExpression ParseExpression(IReadOnlyList<string> tokens, ref int index)
        {
            if (index >= tokens.Count)
            {
                throw new InvalidDataException("Unexpected end of DSN.");
            }

            var token = tokens[index++];
            if (token == "(")
            {
                var items = new List<SExpression>();
                while (index < tokens.Count && tokens[index] != ")")
                {
                    items.Add(ParseExpression(tokens, ref index));
                }

                if (index >= tokens.Count || tokens[index] != ")")
                {
                    throw new InvalidDataException("Unclosed DSN list.");
                }

                index++;
                return List(items);
            }

            if (token == ")")
            {
                throw new InvalidDataException("Unexpected ')' in DSN.");
            }

            return Atom(token);
        }

        private static IEnumerable<string> Tokenize(string source)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        quoted = false;
                        tokens.Add(token.ToString());
                        token.Clear();
                    }
                    else
                    {
                        token.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    Flush();
                    quoted = true;
                    continue;
                }

                if (c == ';' || c == '#')
                {
                    Flush();
                    while (i + 1 < source.Length && source[i + 1] is not '\r' and not '\n')
                    {
                        i++;
                    }

                    continue;
                }

                if (c == '(' || c == ')')
                {
                    Flush();
                    tokens.Add(c.ToString());
                    continue;
                }

                if (char.IsWhiteSpace(c) || c == '\uFEFF')
                {
                    Flush();
                    continue;
                }

                token.Append(c);
            }

            if (quoted)
            {
                throw new InvalidDataException("Unclosed quoted DSN atom.");
            }

            Flush();
            return tokens;

            void Flush()
            {
                if (token.Length > 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }
            }
        }
    }
}
