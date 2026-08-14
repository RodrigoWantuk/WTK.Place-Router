# PLAN-03 — Importação, Project Lifecycle, Transactions e Invalidation

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-01 e PLAN-02 concluídos  
**Desbloqueia:** PLAN-04 e sustenta PLAN-05/06/09/11

---

## 1. Instrução ao agente

Você está implementando a passagem entre arquivos externos e o projeto real do Place&Router, além do lifecycle que permitirá alterações incrementais, undo/recovery e runs sem sobrescrever estado incorreto.

Antes de codificar:

1. leia `/AGENTS.md`;
2. leia `/plan/00-ROADMAP-MESTRE-V0.1.md`;
3. confirme que PLAN-01 carrega/salva PRDX e PLAN-02 fornece geometry/constraints funcionais;
4. leia este plano inteiro;
5. leia os documentos obrigatórios;
6. execute toda a cadeia import → project session → transaction → invalidation → save/recovery.

### Documentos obrigatórios

- `docs/01-Interoperabilidade-e-Modelo-Canonico.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0006-EasyEDA-DSN-como-Handoff-Inicial.md` ou ADR equivalente vigente
- schemas PRDX v0.1
- PLAN-01 e PLAN-02

---

## 2. Objetivo mensurável

Ao final deve ser possível:

```text
Import DSN supported source
→ produce canonical design + ImportResult
→ inspect loss/capability diagnostics
→ create PRDX project/session
→ apply typed physical/domain transactions
→ compute TransactionDiff
→ determine affected dependencies/stages
→ invalidate only derived state that became stale
→ commit/rollback
→ save/reopen project
→ recover from interrupted editing journal
```

O sistema deve distinguir project revision de optimization run baseline.

---

## 3. Import architecture

Implementar ports/contracts coarse-grained:

```text
ImportRequest
IDesignImporter
ImportResult
ImportCapabilities
ImportLossReport
SourceFingerprint
```

`ImportResult` deve separar:

- canonical project/design data;
- diagnostics;
- data completeness/capabilities;
- loss report;
- source hashes/metadata;
- blocking missing requirements;
- warnings/non-blocking unknowns.

O Domain não deve conhecer parser DSN.

---

## 4. Primeiro adapter: Specctra DSN / EasyEDA baseline

Implementar o baseline documentado:

```text
EasyEDA Pro PCB
→ Export Autoroute DSN
→ Place&Router DSN importer
→ PRDX
```

O parser deve recuperar, na medida em que o formato fornecer:

- board outline;
- layer information;
- component instances/references;
- footprint/image geometry;
- pins/pads;
- placement existente;
- nets/connectivity;
- via/routing rules disponíveis;
- keepouts/regions relevantes;
- source metadata.

### Regra

Não inventar informações não presentes no arquivo.

Toda perda/falta vira `ImportCapability`/diagnostic/provenance apropriado.

Exemplo:

```text
components       COMPLETE
nets             COMPLETE
footprints       COMPLETE
pinNames         PARTIAL
stackupMaterial  MISSING
routes           NONE
```

---

## 5. Parser strategy

Criar parser robusto o suficiente para fixtures reais suportadas, mas não tentar implementar cada variação histórica de Specctra neste plano.

Separar:

```text
lex/parse source
→ source AST/model
→ canonicalization
→ integrity validation
→ ImportResult
```

Isso evita misturar sintaxe externa com entidades Domain.

Preservar source location em diagnostics quando possível.

---

## 6. Source preservation/fingerprint

Calcular hash dos arquivos importados.

Suportar policy de:

- embed source file em `/source/` do PRDX;
- reference-only + metadata/hash;
- no source retention quando solicitado.

Nunca depender do source externo para conseguir reabrir o PRDX depois de importado.

---

## 7. ProjectSession e revisions

Implementar application-level session/lifecycle com conceitos equivalentes a:

```text
ProjectId
ProjectRevision
AcceptedPhysicalStateRevision
DirtyState
SourceRevision/Fingerprint
```

Cada mudança persistente relevante incrementa/redefine revision conforme policy consistente.

O session service deve ser a boundary usada futuramente por UI/CLI para:

- Create;
- Import;
- Load;
- Save;
- SaveAs;
- Close;
- inspect dirty/revision state.

---

## 8. PhysicalDesignTransaction

Implementar transaction framework real, não apenas interface.

Actions iniciais:

- MoveComponent;
- RotateComponent;
- ChangeComponentSide se permitido;
- Lock/Unlock component;
- Create/Edit/Delete constraint;
- Create/Edit/Delete region/group quando necessário ao lifecycle;
- route-related actions podem existir como extensible contract, mas serão concretizadas no PLAN-07.

Transaction deve suportar:

```text
Begin
Apply one or more typed actions
Validate preconditions
Produce candidate state
Evaluate affected hard rules available
Produce TransactionDiff
Commit OR Rollback
```

---

## 9. TransactionDiff

O diff precisa registrar mais do que properties alteradas.

Mínimo:

```text
directChanges
affectedComponents
affectedNets
affectedRegions
affectedConstraints
affectedRoutingResources (quando conhecido)
resolvedFindings
newFindings
metricDeltas disponíveis
baseRevision
candidateRevision
reason/source
```

Esse contract será usado por optimizer, UI, review e replay.

---

## 10. DependencyGraph

Construir grafo/índice de dependências derivado do canonical model.

Relações mínimas:

- Component → Footprint/Pads;
- Component → connected Nets;
- Net → endpoints/components;
- Entity → Groups;
- Entity/Group → Constraints;
- Region → assigned entities/constraints;
- Component/Net → spatial neighborhood queries;
- Physical state → derived artifacts registry.

Não precisa ser um graph database; use estruturas simples e incrementais apropriadas.

---

## 11. Derived artifact validity model

Definir enum/state de estágios/artefatos derivados. Algo equivalente a:

```text
CanonicalIntegrity
AbsoluteGeometry
SpatialIndex
ConstraintEvaluation
PinAccess
FastMetrics
GlobalRouteGuides
DetailedRoutes
Congestion
RegressionReview
Signoff
ExportReadiness
```

Cada artifact/cache deve carregar ao menos:

- base project/state revision;
- validity/stale status;
- scope/owners quando aplicável.

Não persistir caches no PRDX.

---

## 12. EditImpactPlanner

Implementar algoritmo que recebe `TransactionDiff` e determina:

```text
AffectedScope
EarliestInvalidStage
ArtifactsToInvalidate
RecoveryStepsSuggested
```

Exemplos obrigatórios:

### Move component

```text
Move U17
→ absolute geometry stale for U17
→ spatial index update U17
→ relevant constraints stale
→ pin access stale on U17 nets
→ routes touching U17 endpoints stale
→ congestion cells intersecting neighborhood stale
→ regression/signoff stale
```

### Rename project metadata

```text
Change project title
→ no physical artifact invalidation
```

### Change manufacturing minimum spacing

```text
→ effective constraints stale globally
→ route/DRC validity potentially stale globally
→ placement geometry itself remains canonical
```

Não usar “rebuild everything” como comportamento padrão.

Pode existir fallback global explícito quando dependency information for insuficiente, registrando diagnostic/telemetry para futura melhoria.

---

## 13. RecoveryPlanner

Criar planner que transforma invalidation em pipeline mínimo disponível.

Neste plano, antes do router existir, ele deve pelo menos conseguir:

- recomputar geometry/spatial index;
- reavaliar constraints/readiness;
- marcar routing-related stages como `STALE/NOT_AVAILABLE` em vez de fingir recuperação;
- produzir findings/diagnostics atualizados.

PLAN-07/08 expandirão o recovery com reroute/reoptimization.

---

## 14. Undo/Redo foundation

Usar as mesmas transactions/actions para undo/redo.

Não manter um segundo modelo de edição específico da UI.

Pode usar inverse actions ou snapshots/deltas, desde que:

- preserve stable IDs;
- seja determinístico;
- integre revision/dirty state;
- não dependa de Avalonia.

---

## 15. Recovery journal e checkpoints

Implementar session recovery local separado do PRDX.

Requisitos:

- registrar transactions committed desde último save/checkpoint;
- permitir recuperação depois de crash simulado;
- journal não precisa ser um banco sofisticado;
- após save bem-sucedido, compactar/limpar conforme policy;
- nunca guardar secrets;
- recovery deve validar base ProjectId/revision antes de replay.

---

## 16. Optimization run baseline semantics

Mesmo antes do optimizer existir, implementar contract de run baseline:

```text
runId
baseProjectId
baseProjectRevision
basePhysicalStateRevision
status
```

Se project/state mudar depois de iniciar uma run:

```text
RUNNING/RESULT
→ STALE_BASELINE
```

Resultado stale nunca pode ser commitado sobre state atual sem operação explícita de compare/rebase futura.

---

## 17. Diagnostics comuns

Consolidar modelo de diagnostics esperado para importer/lifecycle/transactions:

```text
Code
Severity
Category
Message
EntityRefs
Evidence
Remediation
Source
```

Não duplicar classes incompatíveis em cada módulo.

---

## 18. CLI útil desta fase

Adicionar comandos headless equivalentes a:

```text
placerouter import-dsn source.dsn --out board.prdx
placerouter inspect board.prdx
placerouter project-check board.prdx
```

`import-dsn` deve imprimir capability/loss summary.

---

## 19. Fixtures e testes mínimos

Incluir ao menos um DSN realista/pequeno permitido no repositório ou fixture sintético representativo.

Testes:

1. DSN válido importa board/components/pads/nets esperados;
2. unsupported/missing source field vira diagnostic, não valor inventado;
3. source fingerprint está correto;
4. move transaction produz diff/affected nets corretos;
5. EditImpactPlanner distingue metadata edit de physical edit;
6. manufacturing rule change invalida constraint/route validity apropriada;
7. rollback restaura semanticamente o estado base;
8. journal replay recupera uma sessão simples;
9. run baseline torna-se stale depois de edição relevante;
10. import → save PRDX → reopen preserva design.

---

## 20. Fora de escopo

- Avalonia;
- Constraint Workspace visual;
- global/detailed routing;
- LNS/SA;
- DeepSeek;
- Gerber;
- reimport/rebase complexo de source alterado;
- full SES round-trip.

---

## 21. Critérios de aceitação

Plano concluído quando:

- DSN importer gera canonical design utilizável;
- ImportResult informa capabilities/losses;
- project session/revisions funcionam;
- PhysicalDesignTransaction executa e reverte alterações reais;
- TransactionDiff aponta dependências relevantes;
- EditImpactPlanner retorna menor estágio inválido em casos-chave;
- recovery journal funciona;
- stale run baseline é detectado;
- CLI importa e salva `.prdx`;
- testes alvo passam.

### Demonstração mensurável

```text
placerouter import-dsn sample.dsn --out sample.prdx
→ components: 34
→ nets: 48
→ footprint mapping: 34/34
→ capabilities/loss warnings: ...

apply MoveComponent(U17)
→ affected nets: [...]
→ earliest invalid stage: ConstraintEvaluation/PinAccess
→ stale artifacts listed
```

---

## 22. Relatório final

Informar DSN coverage efetivamente suportada, fixture usada, capabilities/losses, transactions/actions implementadas, invalidation examples, recovery journal behavior e validações executadas.