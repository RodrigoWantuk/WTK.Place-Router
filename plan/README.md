# Planos de Implementação — WTK.Place&Router

Este diretório contém os **planos aprovados** que governam a implementação do WTK.Place&Router v0.1.

Agentes devem obedecer primeiro ao `/AGENTS.md` e depois ao plano específico aplicável.

## Regra de execução

```text
READ /AGENTS.md
→ READ plan/00-ROADMAP-MESTRE-V0.1.md
→ VERIFY PREREQUISITES
→ READ THE SPECIFIC PLAN COMPLETELY
→ READ REQUIRED DOCS/ADRs/SCHEMAS
→ IMPLEMENT THE WHOLE APPROVED DELIVERY
→ RUN TARGETED VALIDATION
→ REPORT MEASURABLE RESULT
```

A numeração é uma sequência de referência. **As dependências declaradas em cada plano e no plano mestre são a autoridade real**; planos sem dependência direta podem ser executados em paralelo depois que seus pré-requisitos comuns estiverem integrados.

---

## Planos aprovados

| ID | Plano | Pré-requisitos principais | Resultado |
|---|---|---|---|
| MASTER | [`00-ROADMAP-MESTRE-V0.1.md`](00-ROADMAP-MESTRE-V0.1.md) | — | DAG e Definition of Done da v0.1 |
| PLAN-01 | [`01-Bootstrap-Core-e-PRDX-Runtime.md`](01-Bootstrap-Core-e-PRDX-Runtime.md) | — | Solution, Core/Domain, PRDX reader/writer/validator, CLI baseline |
| PLAN-02 | [`02-Geometry-Spatial-Index-e-Constraint-Engine.md`](02-Geometry-Spatial-Index-e-Constraint-Engine.md) | 01 | Geometry kernel, spatial index, manufacturing/constraint engine, readiness |
| PLAN-03 | [`03-Importacao-Project-Lifecycle-Transactions-e-Invalidation.md`](03-Importacao-Project-Lifecycle-Transactions-e-Invalidation.md) | 01, 02 | DSN import, project session, transactions, dependency invalidation, recovery journal |
| PLAN-04 | [`04-Desktop-Shell-e-Experiencia-Basica-de-Projeto.md`](04-Desktop-Shell-e-Experiencia-Basica-de-Projeto.md) | 01, 03 | Avalonia desktop shell, Dock.Avalonia workspace, project UX, board viewport |
| PLAN-05 | [`05-Constraint-Workspace-e-Enriquecimento-Automatico.md`](05-Constraint-Workspace-e-Enriquecimento-Automatico.md) | 02, 03, 04 | Constraint authoring, groups/regions, manufacturing setup, enrichment/readiness UX |
| PLAN-06 | [`06-Fast-Evaluation-e-Global-Routing.md`](06-Fast-Evaluation-e-Global-Routing.md) | 02, 03 | Routability proxies, routing resource grid, global routing, congestion/corridors |
| PLAN-07 | [`07-Detailed-Routing-DRC-e-Ripup-Reroute.md`](07-Detailed-Routing-DRC-e-Ripup-Reroute.md) | 06 | Pin access, A* 2.5D, tracks/vias, exact DRC, rip-up/reroute |
| PLAN-08 | [`08-Placement-Search-Joint-Optimizer-e-Regression.md`](08-Placement-Search-Joint-Optimizer-e-Regression.md) | 06, 07 | Placement seed, LNS/SA, joint place/route repair, regression L0–L2 |
| PLAN-09 | [`09-Edicao-Fisica-Interativa-e-Recovery.md`](09-Edicao-Fisica-Interativa-e-Recovery.md) | 03, 04, 05, 07, 08 | Manual placement/routing editing, selective recovery, undo/redo, review diff |
| PLAN-10 | [`10-Semantics-e-Agent-IA-DeepSeek.md`](10-Semantics-e-Agent-IA-DeepSeek.md) | 05, 08 | Semantic graph, AgentOperation, DeepSeek adapter, suggestions/reviews/repair reasoning |
| PLAN-11 | [`11-Export-Pipeline-e-Artefatos-de-Fabricacao.md`](11-Export-Pipeline-e-Artefatos-de-Fabricacao.md) | 03, 07, 08 | Gerber/drill, SES, PDF/SVG/PNG/TIFF artwork and inspection export |
| PLAN-12 | [`12-Integracao-Produto-V0.1-e-Release-Validation.md`](12-Integracao-Produto-V0.1-e-Release-Validation.md) | 01–11 | Produto integrado, E2E, packaging, release validation |

---

## Paralelismo recomendado

Depois de PLAN-03:

```text
UI TRACK
PLAN-04 → PLAN-05

ENGINE TRACK
PLAN-06 → PLAN-07 → PLAN-08
```

Depois de PLAN-08, podem avançar em paralelo quando seus demais pré-requisitos existirem:

```text
PLAN-09 Interactive Editing
PLAN-10 AI/Semantics
PLAN-11 Export
```

Todos convergem no PLAN-12.

---

## Referência de interface Avalonia

Os planos que implementam ou alteram a interface devem respeitar `docs/07-Arquitetura-da-Interface.md`.

O **PLAN-04 exige explicitamente** que o agente inspecione e use como referência concreta o projeto existente:

**https://github.com/RodrigoWantuk/WTK.MediaForge**

para:

- configuração Avalonia;
- package compatibility;
- `MainWindow`/custom title bar;
- `Dock.Avalonia`;
- ToolDock/DocumentDock;
- floating panels reais;
- layout persistence;
- restauração multi-monitor;
- DataTemplate/ViewModel view resolution;
- patterns de Inspector/Bottom Workbench.

A documentação do Place&Router prevalece sobre qualquer diferença de domínio ou decisão histórica do MediaForge.

---

## Status

Todos os planos listados acima estão marcados **APPROVED** para implementação na ordem/dependências definidas.

Alterações futuras que mudem escopo ou invariantes devem atualizar o plano correspondente antes que um agente execute a nova versão.