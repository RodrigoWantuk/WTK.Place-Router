# PLAN-08 — Placement Search, Joint Optimizer e Regression

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-06 e PLAN-07 concluídos  
**Desbloqueia:** PLAN-09, PLAN-10 e PLAN-11

---

## 1. Instrução ao agente

Você está implementando a tese central do WTK.Place&Router: placement e routing como um único problema físico iterativo. Esta entrega precisa demonstrar, sem IA, que uma falha de routing pode reabrir placement, testar repairs, rejeitar regressões e aceitar uma alternativa válida.

Antes de codificar:

1. leia `/AGENTS.md` e o plano mestre;
2. confirme fast/global routing e detailed routing funcionais;
3. leia este plano inteiro;
4. leia os documentos obrigatórios;
5. mantenha hard constraints fora do score;
6. execute até o cenário mínimo de joint repair funcionar end-to-end.

### Documentos obrigatórios

- `docs/00-Visao-Geral-e-Principios.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/05-Agente-IA-Revisao-e-Memoria.md` (reviews/regression contracts)
- `docs/06-Roadmap-e-Criterios-de-Sucesso.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- PLAN-03, PLAN-06 e PLAN-07

---

## 2. Objetivo mensurável

Ao final deve existir um `PhysicalDesignOptimizer` capaz de:

```text
build/receive initial placement
→ legalize
→ fast evaluate
→ global route
→ generate/refine candidates with LNS + SA
→ detailed-route promising candidates
→ receive routing failures
→ reopen affected placement neighborhood
→ attempt repairs
→ compare metrics
→ run regression
→ commit valid improvement or rollback
→ stop by convergence/budget with diagnostics
```

---

## 3. Initial placement seed

Implementar seed v0.1:

1. preserve fixed/mechanical objects;
2. respect Required regions/sides/rotations;
3. group/region coarse assignment;
4. connectivity-weighted coarse placement;
5. legalization;
6. fast routability evaluation.

### Coarse placement

Use heurística simples e explicável, por exemplo barycentric/force-inspired sobre weighted component graph, com anchors fixos e region restrictions.

Não tentar resolver qualidade final no seed.

Se projeto já possuir placement importado e policy permitir preservar, ele pode ser usado como seed.

---

## 4. Legalization

Implementar serviço de legalization para candidatos:

```text
poses
→ board/region/overlap checks
→ local displacement / nearest legal alternatives
→ exact Required evaluation
→ legal candidate OR failure
```

Não mover locked/fixed.

Não esconder uma impossibilidade; retornar failure com blockers.

---

## 5. Candidate action model

Moves iniciais:

- TranslateComponent;
- RotateComponent;
- SwapComponents;
- TranslateCluster;
- RotateCluster;
- RepackNeighborhood;
- optional layer/side change somente quando permitido.

Candidate action passa por masking/preconditions antes de avaliação cara.

Movimento inválido por Required rule não entra no score normal.

---

## 6. Neighborhood selectors

Implementar selectors v0.1:

```text
FunctionalBlockNeighborhood
RoutingFailureNeighborhood
CongestionHotspotNeighborhood
CriticalNetNeighborhood
ConnectivityNeighborhood
SpatialNeighborhood
FindingNeighborhood
```

Cada selector retorna stable entity IDs + reason/source + scope.

Baseline de seleção deve ser local/determinístico; PLAN-10 poderá pedir escolha macro via IA.

---

## 7. Large Neighborhood Search

LNS é a estrutura principal:

```text
current accepted/candidate state
→ choose neighborhood
→ relax selected poses/routes
→ generate alternative arrangements
→ legalize/filter
→ multi-fidelity evaluate
→ reroute affected scope
→ regression
→ accept/reject
```

Destruction/release deve respeitar user locks e preserve policies.

Não reabrir placa inteira automaticamente quando local neighborhood ainda tem alternativas.

---

## 8. Simulated Annealing

Dentro dos neighborhoods, usar SA para refinamento/escape de mínimos locais.

Acceptance:

```text
Δcost <= 0 → accept
Δcost > 0  → probability exp(-Δcost/T)
```

Implementar config versionada:

- initial temperature;
- cooling factor;
- moves per temperature;
- no-improvement threshold;
- optional reheat;
- random seed.

Valores iniciais são baseline benchmarkável, não tuning definitivo.

Toda stochastic run registra seed/config.

---

## 9. Multi-fidelity candidate funnel

Implementar pipeline configurável:

```text
candidate generation
→ hard preconditions
→ geometry/constraint cheap checks
→ fast metrics
→ global routing
→ local detailed routing
→ regression/expensive checks
```

Não detailed-route todos os candidatos.

Guardar counts/telemetry por estágio:

```text
generated
hardRejected
fastSurvivors
globalRouteSurvivors
detailedRouteFinalists
accepted
```

---

## 10. Quality score e normalization baseline

Separar:

```text
Validity
PreferenceCosts
ObjectiveMetrics
RoutingMetrics
ElectricalProxyMetrics
```

Criar score profile baseline com normalização explícita por metric.

Métricas candidatas:

- weighted wirelength;
- critical net length;
- congestion;
- via count;
- critical loop proxy;
- EMI proxy disponível;
- preference violations;
- spare routing capacity;
- route completion.

Não permitir compensação de hard violation.

Expor breakdown auditável.

---

## 11. Regression engine L0–L2

Implementar baselines por accepted state:

```text
constraint status: PASS/FAIL/UNKNOWN
findings
metrics
routes/corridors
```

Após transaction candidate, comparar:

- newly failing Required;
- resolved Required;
- worsened/improved preferences;
- new findings;
- resolved findings;
- metric degradation;
- affected dependency status.

### L0 — Precondition

Antes do move: lock/board/side/rotation/impossibilidades óbvias.

### L1 — Local

Geometry, clearance, affected constraints, local routing, local congestion.

### L2 — Dependency

Second-order effects nas nets/corridors/constraints afetadas.

Novo hard regression bloqueia commit salvo policy explícita de estado temporário que não seja optimizer acceptance.

---

## 12. Joint routing→placement repair

Conectar `RouteFailure` do PLAN-07 a `RoutingFailureNeighborhood`.

Fluxo obrigatório:

```text
Detailed router returns PLACEMENT_BLOCKAGE
→ extract blockers/region/required passage
→ release movable blockers + directly related elements
→ generate move/rotate/repack alternatives
→ fast/global evaluate
→ local detailed reroute
→ regression compare
→ commit best valid repair OR escalate neighborhood
```

Escala só amplia quando local attempts/budget falham.

---

## 13. Repair disruption cost

Além do score de qualidade, medir disruption:

- number of moved components;
- total displacement;
- routes ripped;
- stable blocks reopened;
- critical routes affected;
- user-preserve penalties.

Preferir repair menor quando qualidade/validity equivalentes.

---

## 14. Freeze/thaw

Implementar state/policy:

- ACTIVE;
- STABLE/FROZEN;
- USER_LOCKED;
- REOPENED_BY_FAILURE.

Freeze reduz churn, mas não pode impedir repair de hard blockage se objeto não estiver USER_LOCKED.

Registrar why/when thaw ocorreu.

---

## 15. Candidate comparison

Criar service que retorna comparação estruturada:

```text
A vs B
validity
resolved/new findings
metric deltas
routing completion
wire length
vias
congestion
disruption
score breakdown
```

Não retornar somente score agregado.

Isso será usado por UI e IA.

---

## 16. Optimizer run lifecycle

Integrar baseline semantics do PLAN-03:

Statuses mínimos:

```text
CREATED
RUNNING
PAUSED
COMPLETED
CANCELLED
FAILED
STALE_BASELINE
```

Implementar cancellation/safe points entre candidate/neighborhood/global/detail iterations.

Pause deve preservar último estado consistente e config/seed.

Run result só pode ser aplicado se baseline ainda compatível; caso contrário exige compare/rebase explícito futuro.

---

## 17. Optimization diagnostics/telemetry

Registrar:

- current phase;
- neighborhood;
- iteration;
- candidate counts;
- best metric delta;
- route completion;
- regressions rejected;
- repair attempts;
- convergence/budget reason;
- elapsed compute;
- seed/profile versions.

Não persistir milhares de full states no PRDX; run store pode guardar summaries/deltas conforme docs.

---

## 18. Cenário obrigatório de prova

Criar fixture específico com comportamento semelhante a:

```text
1 optimizer places U7/C17/C18
2 routing finds N18 trapped
3 router identifies U7/C17 blockers
4 optimizer tests repairs
5 repair A routes N18 but breaks decoupling Required
6 regression rejects A
7 repair B moves/rotates alternative
8 reroute succeeds
9 no hard regression remains
10 B commits
```

Esse teste/integration scenario é **critério de aceite**, não opcional.

---

## 19. Benchmarks mínimos

Comparar no mesmo fixture:

- simple/initial placement;
- placement search without global routing feedback se facilmente configurável;
- joint optimizer.

Registrar:

- route completion;
- hard violations;
- total/critical length;
- via count;
- max congestion;
- repairs/rollbacks;
- compute time.

Não buscar performance de mercado ainda; validar arquitetura e regressão.

---

## 20. Testes mínimos

1. legalization respeita locked/regions;
2. SA com seed reproduz sequence/result dentro do esperado;
3. hard-invalid moves não entram em score;
4. LNS release respeita USER_LOCKED;
5. candidate funnel não detailed-route hard-rejected candidates;
6. hard regression rejeita repair A;
7. routing failure reabre placement e repair B resolve fixture obrigatório;
8. cancellation termina em safe point;
9. stale baseline impede apply silencioso;
10. candidate comparison fornece deltas corretos.

---

## 21. Fora de escopo

- MCTS/ML;
- learned ranking;
- full thermal/SI solver;
- AI strategy;
- manual interactive editing;
- GUI polish do optimizer;
- multi-machine distributed search.

---

## 22. Critérios de aceitação

Plano concluído quando:

- initial seed/legalization funcionam;
- LNS + SA geram/refinam candidates;
- multi-fidelity funnel usa fast/global/detailed routing;
- score/validity são separados e auditáveis;
- regression L0–L2 funciona;
- routing blockage reabre placement;
- fixture obrigatório conclui com repair válido;
- runs são canceláveis/reproduzíveis por seed;
- benchmark básico registra ganho/resultado;
- build/test alvo passa.

### Demonstração mensurável

Apresentar uma run onde:

```text
N18 initial route: FAIL/PLACEMENT_BLOCKAGE
Repair A: route PASS, regression FAIL → rejected
Repair B: route PASS, regression PASS → committed
Final hard violations: 0
```

---

## 23. Relatório final

Informar seed strategy, LNS neighborhoods, SA baseline parameters, funnel counts, score breakdown, regression coverage, cenário obrigatório e benchmark resultante.