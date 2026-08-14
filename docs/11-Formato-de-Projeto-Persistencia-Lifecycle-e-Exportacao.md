# 11 — Formato de Projeto, Persistência, Lifecycle e Exportação

## 1. Objetivo

Este documento define o contrato persistente do WTK.Place&Router e fecha quatro pontos que passam a ser estruturais para a implementação:

1. qual é o formato de projeto do usuário;
2. como dados importados de EDAs externos são internalizados e preservados;
3. como alterações manuais invalidam e reexecutam apenas as etapas necessárias do physical design;
4. como o estado físico aceito é exportado para EDAs, fabricação industrial, documentação e fabricação artesanal/transferência.

O formato de projeto adotado é **PRDX**, um container ZIP com extensão própria `.prdx`.

A regra central é:

> o arquivo `.prdx` armazena o design canônico e as decisões persistentes do usuário; caches, estruturas algorítmicas temporárias e milhares de candidatos intermediários não fazem parte do estado canônico do projeto.

---

## 2. Decisões v0.1

### Accepted

```text
Project extension       .prdx
Physical container      ZIP
Canonical payload       JSON
JSON Schema             Draft 2020-12
Canonical coordinate    Int64
Canonical length unit   1 µm
Canonical state         PhysicalDesignState
```

O container é comprimido para:

- reduzir tamanho de routes/footprints/metadata;
- permitir assets opcionais;
- preservar arquivos de origem quando solicitado;
- permitir evolução futura sem transformar `project.json` em um formato binário fechado.

---

## 3. Conteúdo de um arquivo `.prdx`

Layout inicial:

```text
MyBoard.prdx
├── manifest.json
├── project.json
├── assets/
│   ├── documents/
│   └── images/
├── source/
│   └── ... optional embedded source files
└── attachments/
    └── ... optional user/project attachments
```

### 3.1 `manifest.json`

Pequeno e rápido de ler.

Contém:

```text
format
formatVersion
projectId
projectRevision
createdAt
modifiedAt
applicationVersion
canonicalPayload
payloadSha256
source fingerprints
optional feature flags
```

Ele permite identificar versão e integridade antes de desserializar o projeto completo.

### 3.2 `project.json`

É a fonte canônica persistente do projeto.

Ele contém:

- origem/importação;
- components;
- footprints/pads;
- netlist/nets;
- board/stackup;
- groups/regions;
- propriedades elétricas;
- constraints;
- semantics;
- manufacturing profile;
- estado físico aceito;
- placement;
- routing;
- vias/copper zones;
- locks/preserve policies;
- decisões explícitas de review;
- perfis de otimização/export definidos pelo usuário.

### 3.3 `source/`

Opcional.

Pode preservar cópia dos arquivos importados:

```text
source/easyeda-export.net
source/original-board.dsn
source/source-metadata.json
```

A incorporação é configurável porque alguns projetos podem conter dados proprietários grandes ou arquivos que o usuário prefira manter externamente.

Mesmo quando a fonte não é incorporada, o projeto guarda fingerprints/hashes e metadata suficientes para auditoria.

### 3.4 `assets/`

Para conteúdo que pertence ao projeto mas não deve ser embutido diretamente como JSON, por exemplo:

- trechos/documentos explicitamente anexados pelo usuário;
- imagens de referência;
- assets necessários a determinada representação visual;
- documentos técnicos associados ao projeto.

API keys, provider secrets e workspace UI **nunca** entram aqui.

---

## 4. O que não pertence ao `.prdx`

Não persistir como estado canônico:

```text
Quadtree/R-tree runtime nodes
RoutingGrid temporário
A* open/closed sets
SA current temperature
LNS temporary neighborhoods
thousands of rejected candidates
render cache
GPU resources
Avalonia controls
Dock layout
window coordinates
cloud API credentials
```

Esses itens são runtime/cache/workspace state.

Persistir apenas quando algo deixa de ser derivado e passa a representar intenção explícita do usuário.

Exemplo:

```text
Derived global-routing corridor     → cache/run artifact
User-created PreferredCorridor rule → project constraint
```

---

## 5. Separação Project / Workspace / Run / Cache

Quatro domínios de persistência distintos:

```text
PROJECT
  .prdx
  design + intent + accepted physical state

WORKSPACE
  AppData/WTK/PlaceRouter/...
  docking, filters, recent files, viewport preferences

RUN
  .prdxrun or local run store
  optimizer/reasoning replay and benchmark data

CACHE
  AppData/WTK/PlaceRouter/cache/...
  fully regenerable data
```

### 5.1 `.prdxrun`

Runs longas podem ser persistidas separadamente.

Um run artifact deve registrar:

```text
runId
baseProjectId
baseProjectRevision
baseDesignHash
constraintHash
randomSeed
optimizer profile/version
algorithm strategy versions
provider/model
AgentOperations
accepted/rejected transaction summaries
metrics timeline
candidate summaries
final result
```

O `.prdx` guarda o **resultado aceito**, não o histórico completo de todas as tentativas.

---

## 6. IDs estáveis

Reference designator não é identidade interna.

Exemplo:

```text
ComponentId = cmp_01J...
referenceDesignator = U17
```

IDs persistentes são usados para:

- Component;
- Footprint;
- Pad;
- Net;
- NetClass;
- Group;
- Region;
- Constraint;
- SemanticRelationship;
- Route;
- TrackSegment;
- Via;
- CopperZone;
- ReviewDecision;
- SourceImport.

Regras:

1. IDs não mudam apenas porque o usuário renomeou uma referência;
2. importers tentam preservar identidade em reimport incremental;
3. referências humanas permanecem metadata legível;
4. relações internas nunca dependem de nome como foreign key.

---

## 7. Unidades e coordenadas

Coordenadas físicas canônicas:

```text
Int64
1 unit = 1 µm
```

Ângulos persistidos em unidade explícita, inicialmente graus.

Valores elétricos usam valor + unidade ou unidade canônica documentada.

Nunca depender de locale para serialização:

```text
1.25
```

nunca:

```text
1,25
```

---

## 8. Provenance e conhecimento

Propriedades relevantes precisam distinguir origem e grau de conhecimento.

Taxonomia canônica:

```text
IMPORTED
USER_DEFINED
AI_INFERRED
DETERMINISTIC_INFERENCE
DETERMINISTIC_MEASUREMENT
DERIVED
MANUFACTURING_PROFILE
DEFAULT
UNKNOWN
```

Knowledge status:

```text
KNOWN
INFERRED
UNKNOWN
NOT_APPLICABLE
```

Exemplo:

```json
{
  "value": 3.2,
  "unit": "A",
  "status": "KNOWN",
  "provenance": {
    "kind": "USER_DEFINED"
  }
}
```

Unknown permanece explicitamente unknown; não é preenchido por valor inventado para satisfazer schema.

---

# Parte I — Logical Design

## 9. Source imports

Cada importação gera um `SourceImport`.

Campos mínimos:

```text
id
adapterId
adapterVersion
sourceType
sourceName
sourceHash
importedAt
embeddedPath? 
capabilities
lossDiagnostics
```

Capabilities usam estados como:

```text
COMPLETE
PARTIAL
MISSING
NOT_AVAILABLE
NOT_APPLICABLE
```

Exemplo:

```text
components        COMPLETE
nets              COMPLETE
footprints        COMPLETE
pinNames          PARTIAL
boardOutline      COMPLETE
stackup           MISSING
existingPlacement PARTIAL
existingRoutes    NOT_AVAILABLE
```

---

## 10. Components

Cada componente contém pelo menos:

```text
id
referenceDesignator
value
partNumber
footprintId
properties
semantic role/classification
placement policy
source metadata
provenance
```

Placement policy inicial:

```text
MOVABLE
PRESERVE_PREFERRED
LOCKED
MECHANICAL_FIXED
UNPLACED
```

A pose física não vive na definição lógica do componente; ela vive em `PhysicalDesignState.componentPoses`.

---

## 11. Footprints e pads

O footprint precisa ser rico o suficiente para placement, DRC e fabricação.

Persistir:

```text
body geometry
courtyard geometry
height metadata
pads
holes
reference/orientation origin
graphics by purpose/layer
```

Pad:

```text
id
number
name
connectedPin
relative position
rotation
shape
size
pad type
layers
drill when applicable
mask/paste metadata when available
custom polygon when applicable
```

Tipos de pad iniciais:

```text
SMD
THRU_HOLE
NPTH
CONNECTOR
MECHANICAL
```

Footprint graphics podem representar:

```text
SILKSCREEN
FAB
COURTYARD
ASSEMBLY
USER_GRAPHICS
```

---

## 12. Netlist e Nets

A netlist canônica é a própria coleção de nets com endpoints pad-level.

Não manter duas conectividades independentes que possam divergir.

```text
Netlist
 └── Nets[]
      ├── NetId
      ├── Name
      ├── Endpoints[] → PadId
      ├── NetClassId?
      ├── ElectricalProperties
      ├── RoutingProperties
      └── Provenance
```

Uma net multi-terminal continua sendo naturalmente um hyperedge.

Electrical properties podem incluir:

```text
signal type
nominal/max voltage
continuous/peak current
frequency
bitrate
bandwidth
edge rate
impedance target
high impedance
clock
switching node
power rail
aggressor level
susceptibility level
return path criticality
```

Routing properties podem incluir:

```text
priority
min/preferred/max width
max/min length
max vias
preferred/forbidden layers
max skew
parallelism rules
corridor preferences
```

---

## 13. Groups

Persistir groups como entidades first-class e hierárquicas.

```text
ComponentGroup
NetGroup
FunctionalGroup
MixedSemanticGroup
```

Um grupo pode conter IDs de entidades ou outros grupos.

Constraints referenciam grupo, não precisam ser expandidas e duplicadas para todos os membros no arquivo.

---

# Parte II — Board e regras

## 14. Board

Persistir:

```text
origin
outline
cutouts
holes
edge metadata
thickness
material metadata
layers
stackup
regions
keepouts
fixed mechanical objects
```

Board outline e outras geometrias usam paths/polygons em unidades canônicas.

---

## 15. Stackup

Cada layer possui ID estável e tipo.

Exemplos:

```text
COPPER_SIGNAL
COPPER_PLANE
DIELECTRIC
SOLDER_MASK
SILKSCREEN
MECHANICAL
```

Para copper/dielectric, quando conhecidos:

```text
thickness
material
dielectric constant
loss tangent
copper weight/thickness
reference relationships
preferred direction
```

Unknown é permitido.

---

## 16. Regions e Keepouts

Region é intenção espacial persistente.

Pode carregar:

```text
geometry
layer scope
semantic type
Required/Preferred/Forbidden assignment relations
```

Keepout é regra física explícita e pode valer para:

```text
components
tracks
vias
copper zones
specific layers
all physical objects
```

---

## 17. ManufacturingProfile

O perfil persistido deve registrar **uma cópia/snapshot efetiva** das capacidades usadas pelo projeto, não apenas o nome `JLCPCB Standard`.

Isso garante reproducibilidade se o template global mudar depois.

Campos incluem:

```text
minimum trace width
minimum spacing
minimum drill
minimum via diameter
minimum annular ring
copper to edge
supported layer count
allowed via types
blind/buried policy
via-in-pad policy
minimum component spacing
assembly restrictions
copper thickness
other process limits
```

Também persistir:

```text
profileName
profileVersion
source/template provenance
lastValidatedAt?
```

---

## 18. Constraints

Constraint é persistida como dado tipado.

```text
Constraint
 ├── id
 ├── type
 ├── source selector
 ├── target selector?
 ├── parameters
 ├── enforcement
 ├── scope
 ├── provenance
 ├── reason
 ├── enabled
 └── user metadata
```

Enforcement:

```text
REQUIRED
PREFERRED
OPTIMIZATION_GOAL
```

Selectors suportam:

```text
ENTITY
GROUP
REGION
CLASS
QUERY
ALL
```

O schema base permite `parameters` extensível, enquanto schemas específicos por constraint type validam contratos concretos durante implementação.

---

## 19. Semantics

Persistir somente semântica relevante à intenção/reasoning, com provenance.

Exemplos:

```text
DecouplingRelationship
FeedbackNetwork
DifferentialPair
PowerRail
SwitchingNode
HighImpedanceNode
Clock
KelvinSense
GuardRing
PowerLoop
AnalogIsland
```

Inference de IA sempre mantém confidence/evidence e não sobrescreve silenciosamente informação explícita do usuário.

---

# Parte III — Physical Design State

## 20. Estado físico aceito

O projeto persiste um `PhysicalDesignState` aceito/atual.

Campos principais:

```text
stateId
stateRevision
status
basedOnProjectRevision
componentPoses
routes
vias
copperZones
locks/preserve policies
manual edit metadata
```

Status:

```text
VALID
VALID_WITH_WARNINGS
INVALID_REQUIRES_REVIEW
PARTIALLY_ROUTED
UNROUTED
STALE_DERIVED_ANALYSIS
```

Um estado editado manualmente pode temporariamente ser inválido e continuar sendo editável. O sistema abre findings e impede sign-off/export de fabricação quando houver violations bloqueantes.

O optimizer, por outro lado, não promove automaticamente um candidate com Required violations para estado aceito válido.

---

## 21. ComponentPose

```text
componentId
x
y
rotationDeg
side
placementState
lastModifiedBy
```

Side inicialmente:

```text
TOP
BOTTOM
```

---

## 22. Routing persistente

Routing aceito faz parte do `.prdx`.

Cada route pertence a uma net e contém geometria física explícita.

```text
Route
 ├── id
 ├── netId
 ├── status
 ├── policy
 ├── trackSegments[]
 ├── viaIds[]
 ├── source/provenance
 └── metadata
```

Track segment:

```text
id
layerId
width
start(x,y)
end(x,y)
geometry kind
```

Geometrias iniciais:

```text
LINE
ARC
```

O v0.1 pode produzir apenas LINE/45° em routing automático, mas o formato deve conseguir preservar ARC importado/exportado quando suportado.

Route policy:

```text
REROUTABLE
PRESERVE_PREFERRED
LOCKED
```

---

## 23. Vias

Via persistida independentemente para permitir compartilhamento/branch connectivity.

```text
id
netId
x
y
startLayerId
endLayerId
viaType
drillDiameter
outerDiameter
padstack metadata
policy
```

Via types:

```text
THROUGH
BLIND
BURIED
MICROVIA
```

Somente os tipos permitidos pelo stackup/manufacturing profile são válidos.

---

## 24. Copper zones / pours

O formato aceita:

```text
id
netId?
layerId
polygon(s)
clearance policy
priority
fill settings
locked/preserve policy
```

O algoritmo de repour é derivado; a intenção da zone é persistente.

---

## 25. Findings não são fonte de verdade

Findings são majoritariamente derivados das regras e do estado atual.

O projeto pode persistir decisões explícitas do usuário, por exemplo:

```text
acknowledgement
waiver where policy allows
accepted risk
note
```

Mas, ao abrir o projeto, o engine deve **recalcular findings**.

Uma finding antiga não pode permanecer marcada como resolvida apenas porque foi salva assim.

---

# Parte IV — Lifecycle, invalidação e edição manual

## 26. Project revision

Toda alteração persistente aumenta `projectRevision`.

Toda alteração física aumenta também `PhysicalDesignState.stateRevision`.

Runs registram a revisão base em que começaram.

Se o projeto mudar durante uma run, o resultado não pode sobrescrever silenciosamente o novo estado.

---

## 27. Não usar rollback cronológico cego

Quando o usuário altera uma entidade manualmente, o sistema não executa algo como:

```text
undo 4 optimizer steps
```

Em vez disso:

```text
PhysicalDesignTransaction
        ↓
EditImpactPlanner
        ↓
DependencyGraph
        ↓
AffectedScope + EarliestInvalidStage
        ↓
Invalidate derived artifacts
        ↓
Recompute minimum necessary pipeline
        ↓
Regenerate findings/regressions
```

Isso preserva trabalho válido em outras regiões.

---

## 28. Estágios lógicos de validade

Os estágios são dependências, não um wizard linear rígido.

```text
S0 IMPORT_RESOLUTION
S1 SEMANTIC_ENRICHMENT
S2 CONSTRAINT_RESOLUTION
S3 PLACEMENT_ANALYSIS
S4 GLOBAL_ROUTING
S5 DETAILED_ROUTING
S6 VERIFICATION
S7 FABRICATION_OUTPUT
```

Cada derived artifact registra, em runtime/run cache:

```text
artifactType
scope
basedOnRevision
input dependency hashes
valid/stale
```

---

## 29. EditImpactPlanner

Serviço determinístico responsável por responder:

```text
What changed?
Which entities depend on it?
Which calculations are now stale?
What is the earliest stage that must be re-run?
What is the smallest affected spatial/electrical scope?
Can recovery be automatic within interactive budget?
```

Saída conceitual:

```text
EditImpact
 ├── directChanges
 ├── affectedComponents
 ├── affectedNets
 ├── affectedConstraints
 ├── affectedRegions
 ├── affectedRoutingObjects
 ├── invalidatedArtifacts
 ├── earliestStage
 ├── recoveryPlan
 └── expectedCost
```

---

## 30. Matriz inicial de invalidação

### Move/rotate component

Invalidar/recalcular:

```text
local geometry/collision
component-specific constraints
pad absolute coordinates
pin access
connected-net route geometry
nearby route clearance/DRC
local congestion
related global route guides when necessary
semantic relationship metrics
verification/findings
fabrication outputs
```

Não invalidar automaticamente routes distantes sem dependência.

### Move/edit track segment

Invalidar/recalcular:

```text
that route/net detailed geometry
local clearance/DRC
length/skew
nearby route interactions
local congestion
route guide only if path escaped/invalidated its corridor assumption
verification/findings
fabrication outputs
```

### Add/remove via

Recalcular:

```text
route connectivity
via manufacturing rules
length/layer transitions
local DRC
congestion
verification
```

### Change constraint

Recalcular somente selectors/dependencies afetados.

Se uma Required rule torna o estado atual inválido, criar finding e plano de repair; não reorganizar silenciosamente toda a board.

### Change manufacturing profile

Pode invalidar em grande escala:

```text
constraint resolution
track/via legality
clearance
routing feasibility
verification
fabrication outputs
```

### Change footprint/pad mapping

Considerado alteração estrutural de alto impacto:

```text
import resolution / canonical logical geometry
placement legality
all nets touching component
routing
verification
```

### Change board outline/keepout

Invalidar todos os objetos que intersectam ou dependem da região alterada e escalar para global quando o impacto espacial for amplo.

---

## 31. RecoveryPlanner

Depois da invalidação, o sistema pode executar automaticamente operações determinísticas baratas.

Exemplo após mover U17:

```text
1. update pose/index
2. run local geometry + constraint checks
3. invalidate U17-connected route primitives
4. recalculate pin access
5. attempt quick local reroute within time budget
6. update congestion
7. rerun local/dependency regression
8. present remaining findings
```

Se quick repair falhar:

```text
NeedsRepair
→ larger local reroute
→ negotiated reroute
→ reopen placement neighborhood
```

A ação do usuário é preservada como authoritative edit; o sistema explica o impacto em vez de silenciosamente desfazê-la.

---

## 32. Manual edits e hard constraints

O usuário pode criar temporariamente uma situação inválida enquanto edita.

Regras:

- integridade estrutural do arquivo nunca pode ser quebrada;
- `LOCKED` exige unlock/override explícito;
- hard-rule violation gera finding imediatamente;
- estado fica `INVALID_REQUIRES_REVIEW` quando necessário;
- fabricação/sign-off ficam bloqueados enquanto violations bloqueantes existirem;
- optimizer não pode aceitar silenciosamente essa violation como solução válida.

Essa política é mais amigável do que impedir cada movimento interativo que temporariamente atravessa uma posição inválida.

---

## 33. Runs concorrentes e stale results

Uma optimization run começa em:

```text
projectRevision = 42
stateRevision = 108
```

Se o usuário editar o projeto e ele avançar para 43/109, a run antiga passa a ser:

```text
STALE_BASELINE
```

O resultado pode ser:

- inspecionado;
- comparado;
- exportado explicitamente como candidate;
- eventualmente rebased através de operação futura;

mas nunca sobrescreve automaticamente o estado atual.

---

# Parte V — Save, autosave e recovery

## 34. Save atômico

Salvar `.prdx` com padrão:

```text
write temporary archive
→ validate manifest/project schema
→ verify hashes
→ fsync/close where applicable
→ atomic replace original
```

Nunca escrever diretamente sobre o único arquivo válido e deixá-lo truncado em crash.

---

## 35. Recovery journal

Não recompactar o ZIP a cada pequeno drag.

Durante sessão:

```text
base .prdx
+ local recovery journal
+ periodic checkpoint
```

O journal contém actions/transactions necessárias para reconstruir alterações não salvas.

Depois de Save bem-sucedido:

```text
compact state into .prdx
clear journal
```

O journal pertence a storage local/recovery, não ao arquivo portátil principal.

---

## 36. Undo/Redo

A mesma `PhysicalDesignTransaction` usada pelo optimizer é usada para edição manual.

```text
MoveComponentAction
RotateComponentAction
EditTrackAction
AddViaAction
DeleteRouteAction
CreateConstraintAction
EditConstraintAction
```

Undo/Redo não manipula ViewModel diretamente; opera sobre Domain/Application e dispara o mesmo EditImpactPlanner.

---

# Parte VI — Import contract

## 37. `IDesignImporter`

Contrato coarse-grained:

```text
ImportRequest
   ↓
IDesignImporter
   ↓
ImportResult
```

`ImportRequest`:

```text
source files/references
adapter options
embed source policy
merge/reimport policy
```

`ImportResult`:

```text
Canonical logical design
Board data when available
Physical data when available
Capabilities report
Loss report
Diagnostics
SourceImport metadata
Suggested follow-up requirements
```

Import nunca altera um projeto existente parcialmente se a operação inteira falhar; usa transaction/staging.

---

## 38. Reimport incremental

No futuro, reimport de schematic/netlist alterado deve tentar preservar:

```text
stable component identity
confirmed semantics
user constraints
placement where still valid
routes that remain electrically/physically valid
```

Diferenças são classificadas:

```text
component added/removed
net changed
footprint changed
pin mapping changed
value/part metadata changed
board changed
```

Cada diff passa pelo mesmo dependency/invalidation system.

---

# Parte VII — Export architecture

## 39. Princípio

Export é uma projection do estado canônico.

```text
PRDX / PhysicalDesignState
        ↓
ExportProfile
        ↓
Exporter
        ↓
ExportResult
```

Nenhum exporter deve ter fonte de verdade paralela.

---

## 40. `IDesignExporter`

Entrada:

```text
ExportRequest
 ├── project/state revision
 ├── target format
 ├── export profile
 ├── selected layers/objects
 └── output destination
```

Saída:

```text
ExportResult
 ├── generatedFiles[]
 ├── warnings[]
 ├── lossReport
 ├── capabilities
 ├── hashes
 └── validationResult
```

---

## 41. Export validity gate

Por padrão:

```text
FABRICATION EXPORT
requires no blocking Required violations
```

O usuário pode exportar candidate/invalid state apenas por fluxo explicitamente marcado como:

```text
DRAFT / DIAGNOSTIC / UNVERIFIED
```

Nunca gerar pacote de fabricação aparentemente válido a partir de state bloqueado sem aviso explícito e persistente.

---

# Parte VIII — Export para fabricação industrial

## 42. Gerber + drill

Primeiro target de fabricação industrial:

```text
Gerber Layer Format
+ Gerber Job metadata when useful
+ NC drill output
```

A implementação deve acompanhar a especificação oficial atual da Ucamco e registrar no export manifest qual revisão/profile foi usada.

Na data desta decisão, a Ucamco publica **Gerber Layer Format Specification revision 2026.05** e mantém o Gerber Job File Schema; também publica a especificação XNC para NC drill.

Para compatibilidade prática com fabricantes, o exporter deve possuir profiles, em vez de assumir que todo CAM aceita a mesma combinação de features.

Exemplo:

```text
GERBER_CURRENT
GERBER_X2_COMPATIBILITY
GERBER_CONSERVATIVE_FAB
```

O primeiro exporter não precisa usar assembly/component features de X3 para produzir copper fabrication layers.

---

## 43. Layers de fabricação

Quando disponíveis no projeto:

```text
Top Copper
Inner Copper...
Bottom Copper
Top Solder Mask
Bottom Solder Mask
Top Paste
Bottom Paste
Top Silkscreen
Bottom Silkscreen
Board Outline/Profile
Fabrication/Documentation layers when profile requests
```

Além disso:

```text
PTH drill
NPTH drill
slot/routing information when supported
```

Se uma layer não existe no canonical design, o exporter não deve inventá-la silenciosamente.

---

## 44. Manufacturing bundle

Comando de alto nível:

```text
Export Manufacturing Package
```

Pode produzir ZIP contendo:

```text
Gerbers
NC drill
Gerber Job
fabrication manifest/readme
layer map
checksums
BOM when available
pick-and-place/centroid when requested
optional IPC-D-356 connectivity output in future
```

BOM/PnP são possíveis porque PRDX já contém components + accepted placement, mas ficam separados do autorouter em si.

---

## 45. IPC-2581 / IPC-DPMX

Suporte planejado como formato de intercâmbio/fabricação rico e bidirecional.

O IPC-2581 Consortium descreve IPC-DPMX/IPC-2581 como padrão aberto e bidirecional para troca de dados de PCB e assembly.

Prioridade:

```text
v0.1 Gerber + drill first
later IPC-2581 exporter/importer after schema/licensing/conformance work
```

Não tentar implementar IPC-2581 superficialmente antes de termos test cases e validação adequada.

---

## 46. Specctra DSN/SES

Quando a entrada utilizar DSN ou quando um EDA suportar esse workflow, o Place&Router deve poder evoluir para:

```text
DSN input
→ Place&Router physical design
→ SES/routing session output
→ EDA
```

Esse é um round-trip particularmente natural para um router externo.

---

## 47. Native EDA adapters

Meta arquitetural:

```text
EasyEDA → PRDX → EasyEDA-compatible output
KiCad   → PRDX → KiCad-compatible output
... 
```

A fidelidade depende de cada adapter.

Todo export nativo produz `LossReport` quando não puder representar algo do PRDX.

---

# Parte IX — Export para fabricação artesanal / transfer

## 48. Objetivo

Usuário que produz PCB por toner transfer, photoresist, máscara impressa ou processo artesanal não deve precisar abrir Gerber em outro software apenas para gerar arte 1:1.

Criar `DIY Transfer Export` como feature de primeira classe.

---

## 49. Formatos

Targets iniciais:

```text
PDF   — vector, escala física 1:1
SVG   — vector/editable, escala física explícita
PNG   — raster lossless
TIFF  — raster lossless/print workflows
```

BMP pode ser adicionado por compatibilidade, mas não é necessário como target principal.

PDF/SVG são preferíveis quando a cadeia de impressão preserva escala física.

Raster exige DPI explícito e validation marks.

---

## 50. Modos de render para transfer

Profile controla:

```text
selected copper layer(s)
positive / negative
mirror / no mirror per layer
black-on-white / white-on-black
board-outline visibility
drill center marks
hole cutouts
registration marks
calibration ruler/test square
crop/margins
raster DPI
anti-alias policy
```

Para processos face-down, o profile pode aplicar mirror automaticamente, mas **a UI deve sempre mostrar preview e orientação final**, evitando regras ocultas dependentes de top/bottom.

---

## 51. Raster fidelity

Para transfer:

- sem JPEG;
- sem scaling implícito;
- sem interpolation destrutiva;
- opção de 1-bit/monochrome quando adequado;
- DPI persistido no metadata do export;
- incluir marca de calibração opcional, por exemplo 10.00 mm.

Um print dialog deve alertar quando a aplicação externa/driver tentar `Fit to page` em vez de 100%/actual size.

---

## 52. Inspection/documentation renders

Separados de transfer técnico.

Podem gerar:

```text
Top board render
Bottom board render
layer-by-layer render
routing-only
component placement
net-highlighted views
constraint overlays
before/after candidate comparisons
```

Targets:

```text
PNG
SVG
PDF
```

Essas imagens são documentação, não fabricação authoritative.

---

# Parte X — Other machine outputs

## 53. Drill maps

Gerar:

```text
NC drill file
human-readable drill map PDF/SVG
hole table
```

Útil tanto para fabricante quanto para produção manual.

---

## 54. CNC / isolation routing

Arquitetura deve permitir futuro exporter para:

```text
isolation contours
DXF/SVG intermediate geometry
G-code through machine profile
```

G-code não entra em v0.1 como formato universal porque depende fortemente de máquina, ferramenta, zero/origin, feeds/speeds e estratégia de isolamento.

Quando implementado, será governado por `MachineProfile` explícito.

---

# Parte XI — Export profiles

## 55. Perfil de export

O usuário não deve configurar dezenas de flags toda vez.

Profiles persistíveis:

```text
JLCPCB Fabrication
Generic Gerber Conservative
Toner Transfer — Bottom Copper
Toner Transfer — Top Copper
Photoresist Film
Inspection PDF
CNC Isolation — Machine X (future)
```

Cada profile contém parâmetros de rendering/format, não estado da PCB.

Profiles globais podem ser templates; quando salvos no projeto, guardar snapshot/override necessário para reproducibilidade.

---

# Parte XII — Application contracts

## 56. Use cases coarse-grained

Presentation/CLI chamam Application Layer, não routers diretamente.

Contracts iniciais:

```text
CreateProject
OpenProject
SaveProject
SaveProjectAs
ImportDesign
ReimportDesign
ValidateProject
ValidateReadiness

ApplyManualEdit
Undo
Redo

StartOptimization
PauseOptimization
ResumeOptimization
CancelOptimization
GetRunStatus
CompareCandidates
AcceptCandidate

RunVerification
GetFindings

ExportProjectSnapshot
ExportManufacturingPackage
ExportTransferArtwork
ExportEdaRoundTrip
```

---

## 57. Cancellation e safe points

Cancel não mata estruturas no meio de mutation.

Safe points:

```text
between transactions
between LNS iterations
between global-routing negotiation rounds
between detailed-net routes
between AgentOperations
before candidate commit
```

Cancel preserva último estado consistente.

Pause mantém run state recuperável sem tornar o projeto canônico dependente de um half-applied transaction.

---

## 58. Diagnostics contract

Toda camada usa estrutura comum:

```text
Diagnostic
 ├── code
 ├── severity
 ├── category
 ├── message key/text
 ├── entityRefs[]
 ├── evidence
 ├── remediation
 ├── source
 └── blocking
```

Severities:

```text
INFO
WARNING
ERROR
FATAL
```

Um problema esperado de design/import/routing não deve virar generic exception como mecanismo normal de controle.

---

# Parte XIII — Versionamento e migrations

## 59. Schema version

PRDX usa semantic versioning de formato:

```text
0.1.0
0.2.0
1.0.0
```

Enquanto `<1.0`, mudanças ainda podem ser mais frequentes, mas migrations continuam explícitas.

---

## 60. Migration pipeline

Abrir projeto antigo:

```text
read manifest
→ identify version
→ load through matching schema
→ apply deterministic migrations
→ validate target schema
→ open project
```

Nunca sobrescrever automaticamente o original durante migration.

Primeiro Save após migration deve permitir backup ou Save As quando mudança for significativa.

Projeto criado por versão futura desconhecida abre read-only quando possível ou falha com diagnóstico claro; nunca tenta desserializar silenciosamente ignorando campos críticos.

---

# Parte XIV — Segurança e privacidade

## 61. Secrets

Nunca entram no PRDX:

```text
DeepSeek API key
other provider keys
tokens
credentials
machine-specific secrets
```

---

## 62. Embedded sources

Ao salvar source files dentro do `.prdx`, a UI deve deixar isso claro porque aumenta a quantidade de propriedade intelectual contida no arquivo portátil.

O mesmo vale para datasheets/documentos anexados.

---

# Parte XV — Escopo de implementação inicial

## 63. PRDX v0.1 mínimo utilizável

Primeira implementação deve suportar persistentemente:

```text
manifest
project metadata
source imports
components
footprints/pads
netlist/nets
net classes
groups
board outline/holes
2-layer stackup mínimo
regions/keepouts
manufacturing profile
constraints
semantic relationships
component poses
tracks
vias
basic copper zones
locks/policies
review decisions
optimization/export profile selection
```

---

## 64. Export v0.1

Prioridade:

```text
1. PRDX save/load round-trip lossless
2. SVG/PDF/PNG transfer + inspection
3. Gerber copper/profile + NC drill
4. complete Gerber manufacturing bundle
5. first EDA round-trip adapter
6. IPC-2581
7. additional machine/assembly outputs
```

A ordem pode avançar em paralelo onde contracts já estiverem estáveis.

---

## 65. Critérios de sucesso

### Persistence

```text
Save → close → reopen
```

preserva semanticamente:

- connectivity;
- components/footprints;
- user rules;
- semantic decisions;
- board geometry;
- placement;
- routes/vias;
- manufacturing settings.

### Edit lifecycle

Mover/rotacionar componente ou editar track:

- identifica dependencies;
- invalida apenas scope necessário;
- recalcula diagnostics;
- tenta repair local quando apropriado;
- não perde routes distantes válidas;
- não deixa análise antiga ser tratada como atual.

### Export

- outputs possuem escala/orientação/layers reproduzíveis;
- fabrication package é bloqueado em estado inválido salvo override explícito de draft;
- Gerber/drill são reabertos/validados por tooling de referência durante testes;
- DIY transfer possui teste físico de escala 1:1;
- round-trip EDA reporta qualquer perda.

---

## 66. Referências externas de formato

Referências primárias a acompanhar durante implementação:

- Ucamco — Gerber Layer Format Specification e Gerber Job File;
- Ucamco — XNC Format Specification;
- IPC-DPMX / IPC-2581 Consortium;
- documentação oficial de cada EDA para adapters/round-trip.

A versão efetivamente suportada por cada exporter deve ser registrada no código, no export manifest e nos testes de conformance.

---

## 67. Decisão final

O `.prdx` é o **arquivo portátil de projeto completo do Place&Router**.

Ele não é apenas uma netlist enriquecida e também não é um dump de memória do optimizer.

Ele representa:

```text
logical design
+ physical board definition
+ engineering intent
+ user constraints
+ semantic knowledge
+ manufacturing assumptions
+ accepted placement
+ accepted routing
+ persistent user decisions
```

Tudo que pode ser recalculado de maneira confiável permanece derivado.

Tudo que expressa intenção, identidade ou resultado físico aceito permanece persistente.
