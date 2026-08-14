# ADR-0005 — PRDX, Persistência, Lifecycle de Edição e Exportação

**Status:** Accepted  
**Date:** 2026-08-14

## Contexto

O Place&Router precisa importar conectividade e geometria de EDAs externos, enriquecer o design com constraints e semântica próprias, persistir placement/routing aceitos, permitir edição manual incremental e produzir diferentes classes de output.

Usar diretamente o formato de um EDA como project model criaria acoplamento, dificultaria provenance, constraints próprias, replay e round-trip entre múltiplas ferramentas.

Também é necessário distinguir:

- estado canônico de projeto;
- workspace UI;
- dados de runs/benchmarks;
- caches regeneráveis.

## Decisão

### 1. Formato de projeto

Adotar `.prdx` como formato portátil nativo do Place&Router.

Fisicamente:

```text
ZIP container
├── manifest.json
├── project.json
├── source/ optional
├── assets/ optional
└── attachments/ optional
```

O payload canônico é JSON validado por JSON Schema.

### 2. Estado canônico

`project.json` persiste:

```text
source/import metadata
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
project-level optimization/export profiles
```

Não persiste estruturas computacionais temporárias.

### 3. Unidades e identidade

```text
coordinates = Int64
1 internal unit = 1 µm
entity IDs = stable internal IDs
reference designators/names = human-readable metadata
```

### 4. Runs

Dados extensos de optimizer/reasoning ficam em `.prdxrun` ou run store separado.

O projeto principal guarda somente o resultado aceito e metadata necessária à reproducibilidade.

### 5. Manual edit lifecycle

Alteração manual usa `PhysicalDesignTransaction` e passa por `EditImpactPlanner`.

Não executar rollback cronológico cego.

O planner encontra:

```text
affected scope
→ earliest invalid stage
→ stale derived artifacts
→ minimum recovery pipeline
```

Mover componente ou track pode invalidar apenas rotas/constraints/congestionamento relacionados, preservando regiões independentes.

### 6. Estado temporariamente inválido

Usuário pode criar state temporariamente inválido durante edição.

O engine:

- abre findings;
- recalcula dependências;
- tenta repair local quando apropriado;
- bloqueia sign-off/fabrication em violations bloqueantes;
- não desfaz silenciosamente a ação do usuário.

### 7. Save/recovery

Save é atômico.

Edição corrente usa recovery journal/checkpoints locais para evitar recompactar ZIP em cada drag.

### 8. Export architecture

Todo export parte de PRDX/`PhysicalDesignState` através de `ExportProfile` + exporter tipado.

Classes iniciais:

```text
Manufacturing
DIY transfer/artwork
Inspection/documentation
EDA round-trip
Rich standardized exchange
Machine-specific future outputs
```

### 9. Manufacturing v0.1

Primeiro pacote industrial:

```text
Gerber Layer Format
+ NC drill
+ Gerber Job/manifest when useful
```

Profiles de compatibilidade são explícitos.

### 10. DIY transfer v0.1

Outputs:

```text
PDF
SVG
PNG
TIFF
```

com:

```text
1:1 scale
mirror control per layer
positive/negative
registration marks
drill center marks
calibration marks
explicit raster DPI
```

### 11. Evolução

Planejar:

```text
IPC-2581 / IPC-DPMX
Specctra DSN/SES round-trip
EasyEDA/KiCad native adapters
CNC/isolation output through MachineProfile
```

## Consequências positivas

- projeto independente de EDA;
- constraints e semântica próprias são preservadas;
- accepted routing faz parte do arquivo;
- caches podem evoluir sem quebrar formato;
- edição manual pode ser incremental;
- export industrial e artesanal compartilham uma única fonte de verdade;
- replay/benchmark não incham o project file;
- migrations podem ser determinísticas.

## Consequências/custos

- precisamos manter JSON Schemas e migrations;
- import/export adapters precisam declarar loss/capabilities;
- save ZIP exige estratégia atômica/journal;
- round-trip para EDAs pode não ser lossless e deve reportar perdas;
- Gerber/IPC/EDA formats exigem conformance testing contínuo.

## Contracts relacionados

- `schemas/prdx/0.1/prdx-manifest.schema.json`
- `schemas/prdx/0.1/prdx-project.schema.json`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`

## Regra de revisão

Qualquer mudança que altere um destes invariants exige novo ADR ou superseding ADR:

- container `.prdx`;
- separação Project/Workspace/Run/Cache;
- accepted physical state como parte do projeto;
- dependency-driven manual edit invalidation;
- canonical export source = PRDX/PhysicalDesignState.
