using System.Text.Json;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;
using PlaceRouter.Geometry;
using PlaceRouter.Application.Projects;
using static PlaceRouter.Application.Lifecycle.PhysicalEditActionHelpers;

namespace PlaceRouter.Application.Lifecycle;

public enum DerivedArtifactStage
{
    None,
    CanonicalIntegrity,
    ConstraintResolution,
    AbsoluteGeometry,
    SpatialIndex,
    ConstraintEvaluation,
    PinAccess,
    FastMetrics,
    GlobalRouteGuides,
    DetailedRoutes,
    Congestion,
    RegressionReview,
    Signoff,
    ExportReadiness
}

public sealed record TransactionDiff(
    long BaseProjectRevision,
    long CandidateProjectRevision,
    long BasePhysicalStateRevision,
    long CandidatePhysicalStateRevision,
    IReadOnlyList<EntityReference> DirectChanges,
    IReadOnlyList<EntityReference> AffectedComponents,
    IReadOnlyList<EntityReference> AffectedNets,
    IReadOnlyList<EntityReference> AffectedConstraints,
    IReadOnlyList<EntityReference> AffectedRoutingResources,
    string Reason,
    string Source);

public sealed record EditImpact(
    DerivedArtifactStage EarliestInvalidStage,
    IReadOnlyList<DerivedArtifactStage> InvalidStages,
    IReadOnlyList<EntityReference> AffectedScope,
    IReadOnlyList<string> RecoverySteps)
{
    public bool RequiresPhysicalGeometryRebuild => InvalidStages.Contains(DerivedArtifactStage.AbsoluteGeometry);
    public bool RequiresRouteRecovery => InvalidStages.Contains(DerivedArtifactStage.DetailedRoutes);
}

public sealed record RecoveryResult(
    ConstraintEvaluationReport ConstraintReport,
    EditImpact Impact,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class ProjectSession(ProjectDocument document, IRecoveryJournal? journal = null)
{
    private readonly Stack<TransactionDiff> _undo = new();
    private readonly Stack<TransactionDiff> _redo = new();

    public ProjectDocument Document { get; private set; } = document;

    public bool IsDirty { get; private set; }

    public long ProjectRevision => Document.Project.ProjectRevision;

    public long PhysicalStateRevision => Document.Project.PhysicalDesignState.StateRevision;

    public IReadOnlyList<TransactionDiff> UndoHistory => _undo.ToArray();

    public IReadOnlyList<TransactionDiff> RedoHistory => _redo.ToArray();

    public PhysicalDesignTransaction BeginTransaction(string reason, string source = "user") =>
        new(this, reason, source);

    public RecoveryResult Apply(PhysicalDesignTransaction transaction, IEditImpactPlanner? impactPlanner = null, IRecoveryPlanner? recoveryPlanner = null)
    {
        var committed = transaction.Commit();
        Commit(committed.Document, committed.Diff, transaction.Actions);
        var planner = impactPlanner ?? new EditImpactPlanner();
        var impact = planner.Plan(committed.Diff, DependencyGraph.From(Document.Project));
        return (recoveryPlanner ?? new RecoveryPlanner()).Recover(Document.Project, impact);
    }

    public ProjectSaveResult Save(IProjectStore store, string path)
    {
        var result = store.Save(Document, path);
        if (result.Success)
        {
            IsDirty = false;
            journal?.Clear(Document.Project.ProjectId);
        }

        return result;
    }

    internal void Commit(ProjectDocument document, TransactionDiff diff, IReadOnlyList<IPhysicalEditAction> actions)
    {
        Document = document;
        IsDirty = true;
        _undo.Push(diff);
        _redo.Clear();
        journal?.Append(document.Project.ProjectId, diff.BaseProjectRevision, actions);
    }
}

public sealed class PhysicalDesignTransaction
{
    private readonly ProjectSession _session;
    private readonly string _reason;
    private readonly string _source;
    private readonly List<IPhysicalEditAction> _actions = [];
    private CanonicalProject _candidate;
    private bool _committed;
    private bool _rolledBack;

    internal PhysicalDesignTransaction(ProjectSession session, string reason, string source)
    {
        _session = session;
        _reason = reason;
        _source = source;
        _candidate = session.Document.Project;
    }

    public IReadOnlyList<IPhysicalEditAction> Actions => _actions;

    public TransactionDiff Diff => BuildDiff(_session.Document.Project, _candidate, _reason, _source);

    public void Apply(IPhysicalEditAction action)
    {
        if (_committed || _rolledBack)
        {
            throw new InvalidOperationException("Transaction is no longer active.");
        }

        _candidate = action.Apply(_candidate).Project;
        _actions.Add(action);
    }

    public (ProjectDocument Document, TransactionDiff Diff) Commit()
    {
        if (_committed || _rolledBack)
        {
            throw new InvalidOperationException("Transaction is no longer active.");
        }

        _committed = true;
        var baseProject = _session.Document.Project;
        var physicalChanged = _actions.Any(a => a.ChangesPhysicalState);
        var candidateRevision = baseProject.ProjectRevision + 1;
        var now = DateTimeOffset.UtcNow;
        var project = _candidate with
        {
            ProjectRevision = candidateRevision,
            Metadata = _candidate.Metadata with { ModifiedAt = now }
        };

        if (physicalChanged)
        {
            project = project with
            {
                PhysicalDesignState = project.PhysicalDesignState with
                {
                    StateRevision = baseProject.PhysicalDesignState.StateRevision + 1,
                    BasedOnProjectRevision = candidateRevision,
                    LastModifiedAt = now,
                    LastModifiedBy = _source
                }
            };
        }

        var document = _session.Document with { Project = project };
        return (document, BuildDiff(baseProject, project, _reason, _source));
    }

    public ProjectDocument Rollback()
    {
        _rolledBack = true;
        _candidate = _session.Document.Project;
        _actions.Clear();
        return _session.Document;
    }

    private static TransactionDiff BuildDiff(CanonicalProject before, CanonicalProject after, string reason, string source)
    {
        var affectedComponents = ChangedComponents(before, after);
        var affectedNets = AffectedNets(after, affectedComponents);
        var affectedConstraints = ChangedConstraints(before, after);
        var affectedRoutes = AffectedRoutingResources(after, affectedNets);
        var directChanges = new List<EntityReference>();
        directChanges.AddRange(affectedComponents);
        directChanges.AddRange(affectedConstraints);
        if (!Equals(before.Metadata, after.Metadata))
        {
            directChanges.Add(new EntityReference("PROJECT_METADATA", after.ProjectId.Value));
        }

        if (!Equals(before.ManufacturingProfile, after.ManufacturingProfile))
        {
            directChanges.Add(new EntityReference("MANUFACTURING_PROFILE", after.ManufacturingProfile.Id.Value));
        }

        return new TransactionDiff(
            before.ProjectRevision,
            after.ProjectRevision,
            before.PhysicalDesignState.StateRevision,
            after.PhysicalDesignState.StateRevision,
            DistinctRefs(directChanges),
            affectedComponents,
            affectedNets,
            affectedConstraints,
            affectedRoutes,
            reason,
            source);
    }

    private static IReadOnlyList<EntityReference> ChangedComponents(CanonicalProject before, CanonicalProject after)
    {
        var beforePoses = before.PhysicalDesignState.ComponentPoses.ToDictionary(p => p.ComponentId.Value, StringComparer.Ordinal);
        return after.PhysicalDesignState.ComponentPoses
            .Where(p => !beforePoses.TryGetValue(p.ComponentId.Value, out var previous) || !Equals(previous, p))
            .Select(p => new EntityReference("COMPONENT", p.ComponentId.Value))
            .ToArray();
    }

    private static IReadOnlyList<EntityReference> AffectedNets(CanonicalProject project, IReadOnlyList<EntityReference> componentRefs)
    {
        var components = componentRefs.Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        return project.LogicalDesign.Nets
            .Where(n => n.Endpoints.Any(e => components.Contains(e.ComponentId.Value)))
            .Select(n => new EntityReference("NET", n.Id.Value))
            .DistinctBy(r => r.EntityId)
            .ToArray();
    }

    private static IReadOnlyList<EntityReference> ChangedConstraints(CanonicalProject before, CanonicalProject after)
    {
        var beforeConstraints = before.Constraints.ToDictionary(c => c.Id.Value, StringComparer.Ordinal);
        return after.Constraints
            .Where(c => !beforeConstraints.TryGetValue(c.Id.Value, out var previous) || !Equals(previous, c))
            .Select(c => new EntityReference("CONSTRAINT", c.Id.Value))
            .ToArray();
    }

    private static IReadOnlyList<EntityReference> AffectedRoutingResources(CanonicalProject project, IReadOnlyList<EntityReference> netRefs)
    {
        var nets = netRefs.Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        return project.PhysicalDesignState.Routes
            .Where(r => nets.Contains(r.NetId.Value))
            .Select(r => new EntityReference("ROUTE", r.Id.Value))
            .ToArray();
    }

    private static IReadOnlyList<EntityReference> DistinctRefs(IEnumerable<EntityReference> refs) =>
        refs.DistinctBy(r => r.EntityType + "\u001f" + r.EntityId).ToArray();
}

public sealed record AppliedProjectChange(
    CanonicalProject Project,
    IReadOnlyList<EntityReference> DirectChanges);

public interface IPhysicalEditAction
{
    bool ChangesPhysicalState { get; }

    bool ChangesManufacturingRules { get; }

    bool ChangesConstraints { get; }

    AppliedProjectChange Apply(CanonicalProject project);

    JournalAction ToJournalAction();
}

public sealed record MoveComponentAction(ComponentId ComponentId, Point2 Position, string ModifiedBy = "user") : IPhysicalEditAction
{
    public bool ChangesPhysicalState => true;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var pose = RequirePose(project, ComponentId);
        RejectIfLocked(project, pose, ComponentId);
        var poses = project.PhysicalDesignState.ComponentPoses
            .Select(p => p.ComponentId == ComponentId ? p with { Position = Position, LastModifiedBy = ModifiedBy } : p)
            .ToArray();
        return ChangePhysical(project, poses, [new EntityReference("COMPONENT", ComponentId.Value)]);
    }

    public JournalAction ToJournalAction() => new("MoveComponent", ComponentId.Value, Position.X.Value, Position.Y.Value, null, null, null);
}

public sealed record RotateComponentAction(ComponentId ComponentId, AngleDegrees Rotation, string ModifiedBy = "user") : IPhysicalEditAction
{
    public bool ChangesPhysicalState => true;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var pose = RequirePose(project, ComponentId);
        RejectIfLocked(project, pose, ComponentId);
        var poses = project.PhysicalDesignState.ComponentPoses
            .Select(p => p.ComponentId == ComponentId ? p with { Rotation = Rotation, LastModifiedBy = ModifiedBy } : p)
            .ToArray();
        return ChangePhysical(project, poses, [new EntityReference("COMPONENT", ComponentId.Value)]);
    }

    public JournalAction ToJournalAction() => new("RotateComponent", ComponentId.Value, null, null, decimal.ToDouble(Rotation.Value), null, null);
}

public sealed record ChangeComponentSideAction(ComponentId ComponentId, string Side, string ModifiedBy = "user") : IPhysicalEditAction
{
    public bool ChangesPhysicalState => true;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var pose = RequirePose(project, ComponentId);
        RejectIfLocked(project, pose, ComponentId);
        var poses = project.PhysicalDesignState.ComponentPoses
            .Select(p => p.ComponentId == ComponentId ? p with { Side = Side, LastModifiedBy = ModifiedBy } : p)
            .ToArray();
        return ChangePhysical(project, poses, [new EntityReference("COMPONENT", ComponentId.Value)]);
    }

    public JournalAction ToJournalAction() => new("ChangeComponentSide", ComponentId.Value, null, null, null, Side, null);
}

public sealed record SetComponentLockAction(ComponentId ComponentId, bool Locked, string ModifiedBy = "user") : IPhysicalEditAction
{
    public bool ChangesPhysicalState => true;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        RequirePose(project, ComponentId);
        var poses = project.PhysicalDesignState.ComponentPoses
            .Select(p => p.ComponentId == ComponentId ? p with { PlacementState = Locked ? "LOCKED" : "PLACED", LastModifiedBy = ModifiedBy } : p)
            .ToArray();
        return ChangePhysical(project, poses, [new EntityReference("COMPONENT", ComponentId.Value)]);
    }

    public JournalAction ToJournalAction() => new("SetComponentLock", ComponentId.Value, null, null, null, Locked ? "LOCKED" : "PLACED", null);
}

public sealed record UpsertConstraintAction(ConstraintDefinition Constraint) : IPhysicalEditAction
{
    public bool ChangesPhysicalState => false;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => true;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var constraints = project.Constraints.Where(c => c.Id != Constraint.Id).Append(Constraint).OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray();
        return new AppliedProjectChange(project with { Constraints = constraints }, [new EntityReference("CONSTRAINT", Constraint.Id.Value)]);
    }

    public JournalAction ToJournalAction() => new("UpsertConstraint", Constraint.Id.Value, null, null, null, null, JsonSerializer.Serialize(Constraint));
}

public sealed record DeleteConstraintAction(ConstraintId ConstraintId) : IPhysicalEditAction
{
    public bool ChangesPhysicalState => false;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => true;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var constraints = project.Constraints.Where(c => c.Id != ConstraintId).ToArray();
        return new AppliedProjectChange(project with { Constraints = constraints }, [new EntityReference("CONSTRAINT", ConstraintId.Value)]);
    }

    public JournalAction ToJournalAction() => new("DeleteConstraint", ConstraintId.Value, null, null, null, null, null);
}

public sealed record UpdateProjectMetadataAction(string Name, string? Description) : IPhysicalEditAction
{
    public bool ChangesPhysicalState => false;
    public bool ChangesManufacturingRules => false;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project) =>
        new(
            project with { Metadata = project.Metadata with { Name = Name, Description = Description } },
            [new EntityReference("PROJECT_METADATA", project.ProjectId.Value)]);

    public JournalAction ToJournalAction() => new("UpdateProjectMetadata", Name, null, null, null, Description, null);
}

public sealed record UpdateManufacturingCapabilityAction(string CapabilityKey, SourcedValue Value) : IPhysicalEditAction
{
    public bool ChangesPhysicalState => false;
    public bool ChangesManufacturingRules => true;
    public bool ChangesConstraints => false;

    public AppliedProjectChange Apply(CanonicalProject project)
    {
        var capabilities = new Dictionary<string, SourcedValue>(project.ManufacturingProfile.Capabilities, StringComparer.Ordinal)
        {
            [CapabilityKey] = Value
        };
        return new AppliedProjectChange(
            project with { ManufacturingProfile = project.ManufacturingProfile with { Capabilities = capabilities } },
            [new EntityReference("MANUFACTURING_PROFILE", project.ManufacturingProfile.Id.Value)]);
    }

    public JournalAction ToJournalAction() => new("UpdateManufacturingCapability", CapabilityKey, null, null, null, null, JsonSerializer.Serialize(Value));
}

public sealed record DependencyGraph(
    IReadOnlyDictionary<string, IReadOnlyList<EntityReference>> ComponentNets,
    IReadOnlyDictionary<string, IReadOnlyList<EntityReference>> NetComponents,
    IReadOnlyDictionary<string, IReadOnlyList<EntityReference>> NetRoutes)
{
    public static DependencyGraph From(CanonicalProject project)
    {
        var componentNets = new Dictionary<string, List<EntityReference>>(StringComparer.Ordinal);
        var netComponents = new Dictionary<string, List<EntityReference>>(StringComparer.Ordinal);
        foreach (var net in project.LogicalDesign.Nets)
        {
            var netRef = new EntityReference("NET", net.Id.Value);
            foreach (var endpoint in net.Endpoints)
            {
                Add(componentNets, endpoint.ComponentId.Value, netRef);
                Add(netComponents, net.Id.Value, new EntityReference("COMPONENT", endpoint.ComponentId.Value));
            }
        }

        var netRoutes = new Dictionary<string, List<EntityReference>>(StringComparer.Ordinal);
        foreach (var route in project.PhysicalDesignState.Routes)
        {
            Add(netRoutes, route.NetId.Value, new EntityReference("ROUTE", route.Id.Value));
        }

        return new DependencyGraph(
            componentNets.ToDictionary(k => k.Key, v => (IReadOnlyList<EntityReference>)v.Value.ToArray(), StringComparer.Ordinal),
            netComponents.ToDictionary(k => k.Key, v => (IReadOnlyList<EntityReference>)v.Value.ToArray(), StringComparer.Ordinal),
            netRoutes.ToDictionary(k => k.Key, v => (IReadOnlyList<EntityReference>)v.Value.ToArray(), StringComparer.Ordinal));

        static void Add(Dictionary<string, List<EntityReference>> map, string key, EntityReference value)
        {
            if (!map.TryGetValue(key, out var values))
            {
                values = [];
                map[key] = values;
            }

            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }
    }
}

public interface IEditImpactPlanner
{
    EditImpact Plan(TransactionDiff diff, DependencyGraph graph);
}

public sealed class EditImpactPlanner : IEditImpactPlanner
{
    public EditImpact Plan(TransactionDiff diff, DependencyGraph graph)
    {
        if (diff.DirectChanges.All(r => r.EntityType == "PROJECT_METADATA"))
        {
            return new EditImpact(DerivedArtifactStage.None, [], diff.DirectChanges, []);
        }

        var stages = new List<DerivedArtifactStage>();
        if (diff.DirectChanges.Any(r => r.EntityType == "COMPONENT"))
        {
            stages.AddRange([
                DerivedArtifactStage.AbsoluteGeometry,
                DerivedArtifactStage.SpatialIndex,
                DerivedArtifactStage.ConstraintEvaluation,
                DerivedArtifactStage.PinAccess,
                DerivedArtifactStage.FastMetrics,
                DerivedArtifactStage.GlobalRouteGuides,
                DerivedArtifactStage.DetailedRoutes,
                DerivedArtifactStage.Congestion,
                DerivedArtifactStage.RegressionReview,
                DerivedArtifactStage.Signoff,
                DerivedArtifactStage.ExportReadiness
            ]);
        }

        if (diff.DirectChanges.Any(r => r.EntityType == "MANUFACTURING_PROFILE" || r.EntityType == "CONSTRAINT"))
        {
            stages.AddRange([
                DerivedArtifactStage.ConstraintResolution,
                DerivedArtifactStage.ConstraintEvaluation,
                DerivedArtifactStage.GlobalRouteGuides,
                DerivedArtifactStage.DetailedRoutes,
                DerivedArtifactStage.RegressionReview,
                DerivedArtifactStage.Signoff,
                DerivedArtifactStage.ExportReadiness
            ]);
        }

        var distinctStages = stages.Distinct().ToArray();
        var earliest = distinctStages.Length == 0 ? DerivedArtifactStage.CanonicalIntegrity : distinctStages.Min();
        var scope = diff.DirectChanges.Concat(diff.AffectedNets).Concat(diff.AffectedRoutingResources)
            .DistinctBy(r => r.EntityType + "\u001f" + r.EntityId)
            .ToArray();
        var steps = distinctStages
            .Select(s => s switch
            {
                DerivedArtifactStage.AbsoluteGeometry => "rebuild absolute geometry for affected physical objects",
                DerivedArtifactStage.ConstraintEvaluation => "re-evaluate required/preferred constraints in affected scope",
                DerivedArtifactStage.GlobalRouteGuides => "mark global route guides stale for affected nets",
                DerivedArtifactStage.DetailedRoutes => "invalidate route artifacts for affected nets",
                _ => "invalidate " + s
            })
            .ToArray();

        return new EditImpact(earliest, distinctStages, scope, steps);
    }
}

public interface IRecoveryPlanner
{
    RecoveryResult Recover(CanonicalProject project, EditImpact impact);
}

public sealed class RecoveryPlanner : IRecoveryPlanner
{
    private readonly ConstraintEvaluationService _evaluationService = new();

    public RecoveryResult Recover(CanonicalProject project, EditImpact impact)
    {
        var report = _evaluationService.Evaluate(project);
        var diagnostics = report.CandidateValid
            ? Array.Empty<Diagnostic>()
            : report.Findings.Select(f => new Diagnostic(
                f.Id,
                f.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                f.Category,
                f.Message,
                f.AffectedEntities,
                f.Evidence.Values,
                Blocking: f.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase))).ToArray();
        return new RecoveryResult(report, impact, diagnostics);
    }
}

public enum OptimizationRunStatus
{
    Running,
    Completed,
    Stale
}

public sealed record OptimizationRun(
    string RunId,
    ProjectId ProjectId,
    long BaseProjectRevision,
    long BasePhysicalStateRevision,
    OptimizationRunStatus Status);

public sealed record RunBaselineCheck(bool CanCommit, Diagnostic? Diagnostic);

public sealed class RunBaselineService
{
    public OptimizationRun StartRun(ProjectSession session) =>
        new("run_" + Guid.NewGuid().ToString("N"), session.Document.Project.ProjectId, session.ProjectRevision, session.PhysicalStateRevision, OptimizationRunStatus.Running);

    public RunBaselineCheck CanCommit(OptimizationRun run, ProjectSession session)
    {
        if (run.BaseProjectRevision == session.ProjectRevision &&
            run.BasePhysicalStateRevision == session.PhysicalStateRevision)
        {
            return new RunBaselineCheck(true, null);
        }

        return new RunBaselineCheck(
            false,
            new Diagnostic(
                DiagnosticCodes.RunBaselineStale,
                DiagnosticSeverity.Error,
                "Lifecycle",
                $"Run '{run.RunId}' started at project revision {run.BaseProjectRevision}/state {run.BasePhysicalStateRevision}, but current revision is {session.ProjectRevision}/state {session.PhysicalStateRevision}.",
                Blocking: true));
    }
}

public sealed record JournalAction(
    string Kind,
    string Id,
    long? X,
    long? Y,
    double? Numeric,
    string? Text,
    string? PayloadJson);

public interface IRecoveryJournal
{
    void Append(ProjectId projectId, long baseProjectRevision, IReadOnlyList<IPhysicalEditAction> actions);

    ProjectDocument Replay(ProjectDocument baseDocument);

    void Clear(ProjectId projectId);
}

public sealed class FileRecoveryJournal(string directory) : IRecoveryJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public void Append(ProjectId projectId, long baseProjectRevision, IReadOnlyList<IPhysicalEditAction> actions)
    {
        Directory.CreateDirectory(directory);
        var record = new JournalRecord(projectId.Value, baseProjectRevision, actions.Select(a => a.ToJournalAction()).ToArray());
        File.AppendAllText(PathFor(projectId), JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
    }

    public ProjectDocument Replay(ProjectDocument baseDocument)
    {
        var path = PathFor(baseDocument.Project.ProjectId);
        if (!File.Exists(path))
        {
            return baseDocument;
        }

        var session = new ProjectSession(baseDocument);
        foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var record = JsonSerializer.Deserialize<JournalRecord>(line) ?? throw new InvalidDataException("Invalid recovery journal record.");
            if (!StringComparer.Ordinal.Equals(record.ProjectId, baseDocument.Project.ProjectId.Value))
            {
                continue;
            }

            var transaction = session.BeginTransaction("journal replay", "journal");
            foreach (var action in record.Actions.Select(FromJournalAction))
            {
                transaction.Apply(action);
            }

            session.Apply(transaction);
        }

        return session.Document;
    }

    public void Clear(ProjectId projectId)
    {
        var path = PathFor(projectId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(ProjectId projectId) =>
        Path.Combine(directory, projectId.Value + ".journal.jsonl");

    private static IPhysicalEditAction FromJournalAction(JournalAction action) =>
        action.Kind switch
        {
            "MoveComponent" => new MoveComponentAction(new ComponentId(action.Id), new Point2(new LengthUnits(action.X ?? 0), new LengthUnits(action.Y ?? 0)), "journal"),
            "RotateComponent" => new RotateComponentAction(new ComponentId(action.Id), new AngleDegrees((decimal)(action.Numeric ?? 0)), "journal"),
            "ChangeComponentSide" => new ChangeComponentSideAction(new ComponentId(action.Id), action.Text ?? "TOP", "journal"),
            "SetComponentLock" => new SetComponentLockAction(new ComponentId(action.Id), string.Equals(action.Text, "LOCKED", StringComparison.Ordinal), "journal"),
            "DeleteConstraint" => new DeleteConstraintAction(new ConstraintId(action.Id)),
            _ => throw new NotSupportedException($"Journal action '{action.Kind}' is not supported for replay.")
        };

    private sealed record JournalRecord(string ProjectId, long BaseProjectRevision, IReadOnlyList<JournalAction> Actions);
}

internal static class PhysicalEditActionHelpers
{
    public static AppliedProjectChange ChangePhysical(CanonicalProject project, IReadOnlyList<ComponentPose> poses, IReadOnlyList<EntityReference> changes) =>
        new(project with { PhysicalDesignState = project.PhysicalDesignState with { ComponentPoses = poses } }, changes);

    public static ComponentPose RequirePose(CanonicalProject project, ComponentId componentId) =>
        project.PhysicalDesignState.ComponentPoses.FirstOrDefault(p => p.ComponentId == componentId)
        ?? throw new InvalidOperationException($"Component '{componentId}' does not have a physical pose.");

    public static void RejectIfLocked(CanonicalProject project, ComponentPose pose, ComponentId componentId)
    {
        var component = project.LogicalDesign.Components.FirstOrDefault(c => c.Id == componentId);
        if (string.Equals(pose.PlacementState, "LOCKED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(component?.PlacementPolicy, "LOCKED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Component '{componentId}' is locked.");
        }
    }
}
