# 09 — Decisões Arquiteturais e Terminologia

## 1. Objetivo

Este documento consolida decisões arquiteturais vigentes e evita que documentos anteriores sejam interpretados como se todas as opções ainda estivessem igualmente abertas.

Estados:

- **Accepted** — direção arquitetural vigente;
- **Provisional** — direção atual, ainda sujeita a benchmark/ADR mais específico;
- **Open** — decisão propositalmente não fechada.

---

## 2. Estado físico canônico

### Accepted

Nome canônico:

```text
PhysicalDesignState
```

Representa placement + routing conjuntos sobre os quais optimizer, transactions, verification, persistence e UI operam.

`BoardState` e `PhysicalState`, encontrados em textos mais antigos, são aliases históricos quando se referirem ao mesmo conceito.

Transaction canônica:

```text
PhysicalDesignTransaction
```

---

## 3. Stack de produto

### Accepted

```text
Primary language       C#
Runtime                .NET
Application type       Desktop-first
UI framework           Avalonia
Presentation pattern   MVVM
MVVM toolkit           CommunityToolkit.Mvvm
Docking                Dock.Avalonia family
Engine                 Headless / platform-agnostic
```

Versões exatas são fixadas no bootstrap usando releases estáveis/suportadas.

---

## 4. Fronteiras arquiteturais

### Accepted

```text
Presentation (Avalonia/MVVM)
          ↓
Application / Coordinators
          ↓
Domain + deterministic engines
          ↓
Infrastructure / adapters
```

- ViewModels não executam geometry/routing/LLM diretamente;
- Domain não referencia Avalonia;
- EDA adapters não contaminam o canonical model;
- AI providers são Infrastructure;
- CLI/tests usam o mesmo Application/Core da GUI.

---

## 5. Padrões de projeto

### Accepted

Usar patterns somente quando resolvem problema concreto.

### Strategy

Para algoritmos substituíveis/benchmarkáveis:

```text
route search strategy
placement search strategy
spatial index strategy
geometry kernel adapter
candidate scoring strategy
```

### Transaction / Command-like actions

Mudanças físicas são actions tipadas em `PhysicalDesignTransaction`, sustentando undo/redo, replay, audit, candidates, regression e explainability.

### Data-driven constraints + evaluator registry

Constraints são dados tipados com evaluators registrados.

### Composite

Groups hierárquicos.

### Domain events

Invalidation/recomputation/review triggers sem framework pesado obrigatório.

---

## 6. Interface

### Accepted

Shell CAD/IDE:

```text
TitleBar
Toolbar
DockControl
StatusBar
```

Workspace default:

```text
Design Navigator | Board Workspace | Constraint Composer + Inspector
                         |
                  Bottom Workbench
```

Tools são dockáveis/flutuantes; board surfaces usam DocumentDock; `PcbViewportControl` usa rendering especializado.

---

## 7. Provider de IA

### Accepted

Provider inicial:

```text
DeepSeek
```

A escolha é operacional. O Agent usa abstraction provider-agnostic e cada run registra provider/model.

Detalhes no ADR-0001.

---

## 8. Protocolo de IA

### Accepted

Toda chamada é `AgentOperation` tipada/versionada:

```text
Stable policy
+ concise operation preamble
+ minimal structured context
+ strict response contract
```

A IA fica fora do numerical inner loop.

Resposta:

```text
schema validation
→ semantic validation
→ action authorization
→ deterministic preconditions
→ candidate transaction
```

A IA nunca declara DRC/final validity.

---

## 9. Processamento local e estratégia algorítmica

### Accepted — direção

Preferir teoria consolidada e bibliotecas maduras antes de custom algorithm e antes de cloud reasoning.

```text
known deterministic algorithm / mature library
        ↓
Place&Router composition/adaptation
        ↓
custom algorithm after benchmark evidence
        ↓
LLM only for semantic/strategic ambiguity
```

### Geometry

- coordinates `Int64`;
- 1 µm como unidade canônica inicial;
- `IGeometryKernel` abstrato;
- Clipper2 strong candidate;
- broad phase separado de exact geometry;
- NetTopologySuite Quadtree candidate inicial para índice mutável.

### Placement

```text
fixed/mechanical-first
→ graph/region-oriented seed
→ legalization
→ fast multi-fidelity evaluation
→ LNS
→ Simulated Annealing refinement
```

### Routing

```text
pin access
→ global routing/resource reservation
→ detailed routing
→ negotiated rip-up/reroute
→ placement repair when evidence requires it
```

- coarse capacity grid;
- HPWL → RMST/RSMT conforme fidelidade;
- FLUTE/equivalente candidate;
- A*/Dijkstra global;
- PathFinder-like negotiated congestion;
- A* 2.5D detailed router;
- Hadlock como future Strategy/benchmark;
- obstacle inflation;
- exact DRC pós-rota.

### Constraint pre-solving

OR-Tools CP-SAT é opcional para pequenos subproblemas discretos, não geometry/placement engine principal.

### Benchmark-gated

- grid resolution;
- spatial index definitivo;
- FLUTE definitivo;
- SA schedule;
- LNS sizes;
- score normalization/weights;
- routing→placement escalation threshold;
- native acceleration;
- CP-SAT production use.

Detalhes em `10-Processamento-Local-e-Algoritmos-Deterministicos.md` e ADR-0003.

---

## 10. Redução de input do usuário

### Accepted

```text
IMPORT
→ DERIVE DETERMINISTICALLY
→ APPLY PROFILE DEFAULTS
→ INFER/SUGGEST
→ ASK USER ONLY IF MATERIAL
```

`Unknown` é válido. A UI pergunta somente quando dependency analysis mostrar impacto material na decisão atual.

---

## 11. Credenciais, privacidade e cloud

### Accepted

Credenciais:

- não entram em PRDX;
- não entram em project JSON;
- não aparecem em logs normais;
- ficam em configuração segura/local apropriada.

Cloud context é minimizado por operation. UI deixa claro provider/model e quando uma operação é cloud/local.

Provider local futuro pode substituir DeepSeek sem alterar Domain/AgentOperation contracts.

---

## 12. Licenciamento de dependências algorítmicas

### Accepted

Separar:

```text
incorporated library
algorithmic/reference material
external benchmark
```

Toda biblioteca incorporada passa por gate de licença, versionamento, boundary e testes.

Detalhes no ADR-0004.

---

## 13. Interoperabilidade

### Accepted

- canonical model desacoplado do EDA;
- EasyEDA como primeiro adapter prático provável;
- arquitetura preparada para KiCad/Specctra/IPC-2581/outros;
- round-trip export é objetivo arquitetural;
- import/export deve produzir capabilities/loss diagnostics.

---

## 14. PRDX — formato de projeto

### Accepted

PRDX deixa de ser apenas nome provisório conceitual e passa a ser o **formato nativo de projeto da primeira implementação**.

```text
extension              .prdx
container              ZIP
canonical payload      JSON
schema                  JSON Schema Draft 2020-12
manifest                manifest.json
project payload         project.json
```

O `.prdx` contém:

```text
logical design
components/footprints/pads
netlist/nets
board/stackup
manufacturing snapshot
constraints
semantics
groups/regions
accepted PhysicalDesignState
placement
routing/vias/copper zones
persistent user decisions
project export/optimization profiles
```

Schemas iniciais:

- `schemas/prdx/0.1/prdx-manifest.schema.json`;
- `schemas/prdx/0.1/prdx-project.schema.json`.

Detalhes no documento `11` e ADR-0005.

---

## 15. Separação Project / Workspace / Run / Cache

### Accepted

```text
PROJECT   → .prdx
WORKSPACE → local UI state
RUN       → .prdxrun/run store
CACHE     → regenerable local data
```

O project file não guarda A* sets, RoutingGrid temporário, SA temperature, rejected candidates, dock layout ou credentials.

---

## 16. Lifecycle de edição manual

### Accepted

Manual edits usam:

```text
PhysicalDesignTransaction
→ EditImpactPlanner
→ DependencyGraph
→ AffectedScope + EarliestInvalidStage
→ selective invalidation
→ RecoveryPlanner
→ regenerated diagnostics/findings
```

Não usar rollback cronológico cego.

Mover componente/track invalida somente dependências relevantes quando possível.

Usuário pode produzir state temporariamente inválido durante edição; hard violations geram findings e bloqueiam sign-off/fabrication, mas não precisam impedir todo movimento interativo.

Runs antigas tornam-se `STALE_BASELINE` se project/state revision mudar e nunca sobrescrevem silenciosamente estado atual.

---

## 17. Persistência e recovery

### Accepted

- save `.prdx` é atômico;
- usar temporary archive + validation/hash + replace;
- edits de sessão usam recovery journal/checkpoints locais;
- journal é compactado/limpo após Save;
- Undo/Redo usa as mesmas domain transactions.

---

## 18. Export architecture

### Accepted

Todo output é projection de PRDX/`PhysicalDesignState` através de `ExportProfile` + exporter.

Classes:

```text
Manufacturing
DIY transfer/artwork
Inspection/documentation
EDA round-trip
Rich standardized exchange
Machine-specific future outputs
```

Fabrication export requer ausência de blocking Required violations por padrão.

---

## 19. Export targets

### Accepted — v0.1 direction

Manufacturing inicial:

```text
Gerber Layer Format
+ NC drill
+ Gerber Job/manifest when useful
```

DIY transfer:

```text
PDF
SVG
PNG
TIFF
```

com 1:1 scale, mirror per layer, positive/negative, registration/drill/calibration marks e explicit DPI para raster.

Inspection/documentation:

```text
PNG
SVG
PDF
```

### Planned

```text
IPC-2581 / IPC-DPMX
Specctra DSN/SES round-trip
EasyEDA/KiCad native round-trip
CNC/isolation outputs via MachineProfile
```

Detalhes no documento `11` e ADR-0005.

---

## 20. Joint placement/routing

### Accepted

Não existe top-level `PlacementEngine → RoutingEngine → Done`.

Routing pode reabrir placement; placement consome feedback de routability/corridors. Tudo trabalha sobre `PhysicalDesignState`.

---

## 21. Validade versus qualidade

### Accepted

```text
Hard constraints → validity
Preferences       → cost
Goals             → optimization metric
```

Nenhum score positivo compensa Required violation.

---

## 22. Roadmap como dependências, não waterfall

### Accepted

Fases do documento `06` representam principalmente dependências técnicas. Workstreams podem avançar em paralelo quando contracts mínimos estiverem estáveis.

---

## 23. Open decisions

Ainda abertas ou benchmark-gated:

- versão final de .NET/Avalonia no bootstrap;
- package names finais;
- primeiro handoff EasyEDA concreto;
- score normalization final;
- thermal/SI/PI solver depth;
- provider/model routing pós-benchmarks;
- política comercial/licenciamento geral;
- engine in-process versus out-of-process futuramente;
- search avançado/MCTS/ML;
- conformance/profile exatos de cada exporter além do baseline v0.1;
- políticas finais de reimport/rebase complexos.

O **formato PRDX v0.1 base não é mais uma decisão aberta**; evoluções são versionadas/migradas.

---

## 24. ADRs

- `ADR-0001` — DeepSeek como provider inicial;
- `ADR-0002` — Stack desktop e fronteiras arquiteturais;
- `ADR-0003` — Processamento local e estratégia algorítmica;
- `ADR-0004` — Gate de licenciamento para dependências algorítmicas;
- `ADR-0005` — PRDX, persistência, lifecycle de edição e exportação.

Novas decisões que alterem invariants devem ganhar ADR próprio.
