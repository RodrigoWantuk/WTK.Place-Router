# 03 — Modelo de domínio e sistema de constraints

## 1. Objetivo

O domínio central deve representar a PCB de forma suficientemente rica para que:

- importers diferentes produzam o mesmo modelo;
- GUI e CLI manipulem as mesmas entidades;
- o optimizer consiga criar candidatos sem depender do EDA;
- constraints sejam avaliadas deterministicamente;
- o agente de IA consulte somente views estruturadas;
- transações possam ser comparadas e revertidas;
- regressões sejam detectáveis.

O modelo não deve ser uma cópia direta de nenhum formato externo.

## 2. Agregados principais

Estrutura conceitual:

```text
Design
 ├── Board
 ├── Stackup
 ├── ManufacturingProfile
 ├── Components
 ├── Footprints
 ├── Nets
 ├── Groups
 ├── Regions
 ├── Constraints
 ├── SemanticRelationships
 ├── PhysicalState
 ├── Findings
 ├── DesignMemory
 └── OptimizationProfile
```

## 3. Board

Responsabilidades:

- outline;
- cutouts;
- holes;
- edge segments;
- dimensions;
- origin/reference system;
- board thickness;
- board material metadata;
- global keepouts;
- mechanical fixed objects.

Geometria deve ser contínua e expressa em unidades físicas, não em pixels.

## 4. Stackup e Layer

Cada layer precisa ter identidade e função.

Exemplos:

```text
CopperLayer
DielectricLayer
PlaneLayer
MechanicalLayer
```

A primeira implementação pode ser simplificada, mas o domínio deve aceitar:

- layer order;
- thickness;
- copper thickness;
- material/dielectric metadata;
- preferred routing directions;
- reference relationships;
- allowed via transitions.

O eixo Z é essencialmente discreto para routing.

## 5. Component

A entidade Component representa uma instância elétrica/física.

Campos conceituais:

```text
id
referenceDesignator
partNumber
value
footprintId
properties
semanticRole
placementPolicy
sourceMetadata
```

Seu estado físico não deve necessariamente morar na definição lógica. É útil separar identidade de `ComponentPose`.

## 6. ComponentPose

```text
ComponentPose
 ├── x
 ├── y
 ├── rotation
 ├── side
 └── placementState
```

Estados/políticas possíveis:

- movable;
- fixed/locked;
- preferred-preserve;
- temporarily frozen;
- unplaced.

O optimizer trabalha sobre poses, não altera a definição do componente.

## 7. Footprint e Pad

Footprint contém:

- body geometry;
- courtyard;
- pads;
- holes;
- orientation reference;
- assembly geometry;
- height quando disponível.

Pad contém:

```text
padId
number
name
relativeX
relativeY
shape
size
layers
padType
connectedPin
```

A transformação `Footprint + ComponentPose` deve produzir a posição absoluta de cada pad.

## 8. Net

Net é uma entidade elétrica, não apenas uma string.

```text
Net
 ├── name
 ├── endpoints
 ├── electricalProperties
 ├── routingProperties
 ├── semanticClass
 ├── provenance
 └── constraints
```

Endpoints referenciam pads/pins reais.

## 9. ElectricalProperties de Net

Propriedades candidatas:

```text
signalType
nominalVoltage
maxVoltage
continuousCurrent
peakCurrent
frequency
bitrate
bandwidth
edgeRate
impedance
aggressorLevel
susceptibilityLevel
highImpedance
isClock
isSwitchingNode
isPowerRail
returnPathCriticality
```

Cada valor deve permitir `Unknown` e provenance.

## 10. RoutingProperties

```text
priority
minWidth
preferredWidth
maxWidth
maxLength
minLength
maxVias
preferredLayers
forbiddenLayers
impedanceTarget
maxSkew
parallelismRules
corridorPreferences
```

Alguns valores podem vir de ManufacturingProfile, NetClass ou constraint específica.

## 11. ComponentGraph, PadGraph e Hypergraph elétrico

Uma net pode conectar mais de dois pads, portanto a conectividade real é naturalmente um hypergraph.

Views úteis:

### ComponentGraph

Ajuda a responder:

- quais componentes se relacionam fortemente;
- quais blocos funcionais existem;
- que componentes têm grande conectividade entre si.

### PadGraph

Necessário para decisões que dependem da geometria local dos pins.

### ElectricalHypergraph

Representa corretamente nets multi-endpoint.

Essas views podem ser derivadas do mesmo modelo canônico.

## 12. SemanticRelationship

Relações semânticas precisam ser entidades explícitas.

Exemplos:

```text
Decouples(C17, U3.VDD)
FeedbackNetworkOf([R17,R18], U7.FB)
SwitchingOutputOf(L3, U7.SW)
SensitiveTo(ADC_REF, SWITCHING_GROUP)
KelvinSenseOf(NET_SENSE, RSHUNT)
```

Cada relação deve carregar:

- source/provenance;
- confidence quando inferida;
- references às entidades;
- optional evidence/notes.

## 13. Group

Group deve ser first-class.

Tipos possíveis:

- ComponentGroup;
- NetGroup;
- FunctionalGroup;
- semantic group;
- hierarchy/group-of-groups.

A implementação deve evitar duplicar regras para cada membro quando uma regra de grupo basta.

## 14. Region

Region representa uma área geométrica com semântica.

Exemplos:

- POWER;
- ANALOG;
- RF;
- CONNECTORS;
- forbidden routing zone;
- component height zone;
- quiet region.

Region pode ter escopo por layer.

## 15. Constraint como entidade

Toda constraint precisa ter identidade e metadata.

Modelo conceitual:

```text
Constraint
 ├── id
 ├── type
 ├── source selector
 ├── target selector
 ├── parameters
 ├── enforcement
 ├── scope
 ├── priority/specificity
 ├── provenance
 ├── reason
 └── enabled
```

Selectors podem apontar para:

- objeto individual;
- grupo;
- classe;
- region;
- wildcard/query.

## 16. Enforcement

Três níveis principais:

```text
Required
Preferred
OptimizationGoal
```

### Required

Se violada, o candidate é inválido.

### Preferred

Produz custo e diagnóstico, mas pode ser quebrada explicitamente.

### OptimizationGoal

Participa de score contínuo.

## 17. Famílias de constraints — Placement

Primeiro conjunto candidato:

```text
Near
Far
MinimumSeparation
MaximumSeparation
InsideRegion
OutsideRegion
NearBoardEdge
FixedPosition
FixedRotation
AllowedRotations
AllowedSide
SameSide
OppositeSide
Cluster
RelativeOrdering
OrientationTowardRegion
```

Distância precisa especificar o que está sendo medido:

- body-to-body;
- courtyard-to-courtyard;
- pad-to-pad;
- object-to-route;
- object-to-net copper;
- nearest geometry;
- electrical path length.

## 18. Famílias de constraints — Routing

```text
MaximumLength
MinimumLength
MatchedLength
MaximumSkew
MaximumVias
PreferredLayer
ForbiddenLayer
MinimumWidth
MaximumWidth
DifferentialPair
PreferredCorridor
ForbiddenRegion
AvoidParallelRouting
MaximumParallelLength
RequiredReferencePlane
ViaSymmetry
ReturnViaRequirement
```

Nem todas precisam existir na primeira release.

## 19. Famílias de constraints — Electrical / EMI

```text
CurrentCapacity
VoltageClearance
FrequencyClass
Impedance
DifferentialImpedance
AggressorLevel
Susceptibility
HighImpedanceExposure
ClockIsolation
SwitchingNodeIsolation
AnalogIsolation
ReturnPathCriticality
```

Algumas são inputs/properties e geram constraints derivadas; outras são regras diretamente avaliáveis.

## 20. Famílias de constraints — Thermal

```text
PowerDissipation
MinimumCopperArea
ThermalViaRequirement
ThermalIsolation
ThermalCouplingPreference
MaximumLocalDensity
Height/Airflow metadata
```

A primeira versão pode trabalhar apenas com proxies, deixando solver térmico completo para evolução posterior.

## 21. Famílias de constraints — Manufacturing

```text
MinimumTraceWidth
MinimumSpacing
MinimumDrill
MinimumViaDiameter
MinimumAnnularRing
CopperToEdge
AllowedLayerCount
AllowedViaTypes
BlindViaAllowed
BuriedViaAllowed
ViaInPadAllowed
MinimumComponentSpacing
AssemblySideRestrictions
```

Essas regras são tipicamente Required.

## 22. Famílias de constraints — Mechanical

```text
BoardOutline
Keepout
MountingHole
FixedConnector
HeightRegion
MaximumComponentHeight
EdgeAlignment
EnclosureRestriction
```

## 23. Regra Component ↔ Net

Um caso central do produto é a separação entre componente e qualquer geometria pertencente a uma net.

Exemplo:

```text
source = Component(U12)
target = Net(SW_NODE)
constraint = MinimumSeparation(8mm)
```

O engine precisa definir com precisão quais geometrias da net contam:

- pads pertencentes à net;
- tracks;
- vias;
- copper zones;
- provisional/reserved corridors, quando avaliando candidates.

O escopo pode ser configurável.

## 24. Regras dependentes de layer

Separação/interferência pode depender de layer.

A constraint pode especificar:

```text
AllLayers
SameLayerOnly
AdjacentLayers
SpecificLayers
ThroughBoardProjection
```

Modelos futuros podem considerar stackup e reference planes ao calcular risco de coupling.

## 25. Inheritance e especificidade

Uma regra pode vir de diferentes níveis:

```text
Global
ManufacturingProfile
NetClass
Group
Object
Transaction override
```

O domínio precisa resolver `EffectiveConstraintSet` para cada objeto/relacionamento.

Quando duas regras são compatíveis, a mais específica pode prevalecer. Quando são contraditórias, deve existir diagnóstico explícito.

## 26. Constraint Validation

Antes de otimizar, o engine deve detectar:

- contradiction;
- impossible region intersections;
- fixed-object conflicts;
- impossible distance triangles simples;
- manufacturing incompatibility;
- selector sem target;
- invalid layer reference;
- impossible allowed rotation/side;
- missing footprint geometry necessária.

Nem toda impossibilidade global será detectável de forma barata antes do search, mas conflitos óbvios devem ser removidos cedo.

## 27. Score

Score é separado de validade.

Modelo conceitual:

```text
CandidateEvaluation
 ├── validity
 ├── requiredViolations
 ├── preferenceCosts
 ├── objectiveMetrics
 ├── routingMetrics
 ├── electricalMetrics
 └── explanations
```

Possíveis objetivos:

```text
- total wire length
- weighted critical-net length
- congestion
- via count
- thermal penalty
- EMI proxy
- crosstalk proxy
- board area usage
- loop area
- routing difficulty
+ functional grouping quality
+ spare routing capacity
```

Pesos precisam ser configuráveis e auditáveis.

## 28. PhysicalState

O estado físico corrente deve conter simultaneamente:

```text
component poses
routes
provisional routes
vias
layer assignments
reserved corridors
copper regions
occupancy maps
congestion maps
findings
locks
```

Isso evita uma arquitetura em que placement e routing vivem em mundos separados.

## 29. Routing Reservation Map

Durante placement, rotas ainda não detalhadas podem reservar recursos.

```text
ReservedCorridor
 ├── nets/group
 ├── candidate layers
 ├── geometry/corridor
 ├── capacity demand
 ├── priority
 └── confidence
```

Ao testar colocar um componente sobre um corredor, o evaluator pode medir impacto sem precisar finalizar toda a rota.

## 30. Findings

Problemas detectados devem ser objetos persistíveis.

```text
Finding
 ├── id
 ├── severity
 ├── category
 ├── affected entities
 ├── evidence/metrics
 ├── source
 ├── status
 └── proposed repairs
```

Exemplos:

- DRC violation;
- routing trap;
- excessive via count;
- poor return path;
- semantic concern;
- congestion hotspot;
- analog/switching proximity;
- manufacturing risk.

## 31. DesignTransaction

Toda modificação relevante deve poder ser representada como transação.

Exemplo:

```text
Transaction #1842

MOVE U7
ROTATE C18
RIP N37
ROUTE N37
```

A transação gera um candidate state e um diff.

Ela precisa suportar:

- begin;
- apply actions;
- incremental recomputation;
- evaluate;
- compare;
- commit;
- rollback;
- repair into a new candidate.

## 32. TransactionDiff

O diff deve registrar não apenas objetos modificados, mas consequências.

```text
Direct changes
Affected nets
Affected corridors
Changed constraints
Resolved findings
New regressions
Metric deltas
```

Isso será usado tanto pelo optimizer quanto pela UI e pelo agente.

## 33. Imutabilidade versus performance

Do ponto de vista conceitual, candidates devem se comportar como estados isolados. A implementação pode usar:

- copy-on-write;
- persistent data structures;
- delta layers;
- transactional mutation;
- spatial indexes incrementais.

Não é necessário copiar uma board inteira a cada movimento.

## 34. Spatial indexes

Para manter avaliação barata, o geometry engine provavelmente precisará de indexes espaciais para:

- nearest objects;
- collision candidates;
- courtyard overlap;
- route proximity;
- region lookup;
- affected neighborhood discovery.

A estrutura específica será decidida na implementação.

## 35. Unidades

O domínio deve usar unidades explícitas e evitar `double` sem semântica quando isso puder provocar erro.

Categorias:

- length;
- angle;
- voltage;
- current;
- frequency;
- time;
- impedance;
- power.

Serialização PRDX deve guardar unidade ou usar uma unidade canônica documentada.

## 36. Determinismo e reproducibilidade

Uma run precisa registrar:

- design version/hash;
- constraint version/hash;
- optimizer settings;
- model/provider/version quando houver IA;
- seed dos algoritmos estocásticos;
- manufacturing profile;
- stackup;
- timestamps;
- tool outputs importantes.

Isso permite reproduzir e comparar resultados.

## 37. Princípio final do domínio

O modelo de domínio deve permitir responder deterministicamente a perguntas como:

```text
Onde está U7?
Quais pads pertencem a U7?
Quais nets esses pads conectam?
Que componentes dependem semanticamente de U7?
Quais constraints são efetivas para U7 e SW_NODE?
Que rotas/corridors seriam afetadas se U7 mover 1 mm?
Que regressões surgiram depois da transação?
```

Se essas perguntas exigirem que o LLM “lembre” da placa, o domínio está incompleto.
