# 10 — Processamento Local e Algoritmos Determinísticos

## 1. Objetivo

Este documento define **o que o WTK.Place&Router executa localmente, em qual momento do fluxo e com quais famílias de algoritmos/bibliotecas na primeira implementação**.

Ele complementa:

- `03-Modelo-de-Dominio-e-Constraints.md` — entidades e contracts conceituais;
- `04-Physical-Design-Optimizer.md` — joint placement/routing;
- `05-Agente-IA-Revisao-e-Memoria.md` — papel do reasoning agent;
- `08-Protocolo-de-Iteracoes-com-IA.md` — protocolo de chamadas cloud;
- `09-Decisoes-Arquiteturais-e-Terminologia.md` — decisões e nomenclatura vigentes.

A meta é eliminar uma ambiguidade importante:

> não basta saber que uma tarefa é “determinística”; precisamos saber **qual mecanismo local tentará resolvê-la primeiro, que dados ele usa, que resultado produz, quando um algoritmo mais caro entra e quando a IA realmente precisa ser chamada**.

A direção central é aproveitar algoritmos clássicos de EDA, graph search, computational geometry e combinatorial optimization antes de inventar técnicas próprias.

---

## 2. Princípio de produto: automação primeiro, formulário depois

O usuário não deve precisar informar manualmente tudo que o sistema consegue:

1. importar;
2. derivar de maneira determinística;
3. obter de um manufacturing profile;
4. inferir com alta segurança da topologia/nomenclatura;
5. sugerir através da IA;
6. medir diretamente do estado físico.

A UI só deve interromper o usuário quando uma informação desconhecida for **material para a decisão em andamento**.

Exemplo ruim:

```text
Antes de começar, preencha frequência, corrente, impedância e suscetibilidade de todas as 84 nets.
```

Exemplo desejado:

```text
Import concluído.

83/84 nets possuem informação suficiente para o estágio atual.

Precisamos confirmar apenas:
VIN_MOTOR — corrente máxima desconhecida.
Motivo: a largura mínima da rota não pode ser derivada com segurança.

[Informar] [Usar apenas mínimo do fabricante] [Manter desconhecido]
```

Assim, `Unknown` continua válido e a aplicação permanece utilizável por usuários não especialistas.

---

## 3. Pipeline local completo

```text
External EDA
    ↓
Import + Canonicalization
    ↓
PhysicalDesignState
    ↓
Automatic Deterministic Enrichment
    ↓
Readiness Dependency Analysis
    ↓
Geometry + Spatial Indexes
    ↓
Constraint Resolution / Validation
    ↓
Initial Floorplan / Placement Seed
    ↓
Fast Placement Evaluation
    ↓
Global Routing / Capacity Reservation
    ↓
LNS + Simulated Annealing
    ↓
Detailed Routing
    ↓
Negotiated Congestion / Rip-up-Reroute
    ↓
Exact Evaluation + Regression
    ↓
Commit candidate or request repair
```

A IA pode intervir **entre etapas ou quando um diagnóstico exige julgamento semântico**, mas não substitui nenhuma das operações geométricas/numericas acima.

---

## 4. Regra de escalada

Cada problema deve usar o mecanismo mais barato capaz de respondê-lo corretamente.

```text
Level 0  imported fact / direct lookup
Level 1  deterministic rule / graph derivation
Level 2  cheap geometric estimate
Level 3  global routing approximation
Level 4  local detailed routing/search
Level 5  wider LNS / rip-up-reroute
Level 6  expensive deterministic review
Level 7  LLM reasoning, only if semantic/strategic ambiguity remains
```

A IA não deve ser usada porque uma operação é difícil computacionalmente. Ela deve ser usada quando **o problema é de interpretação, priorização ou engenharia semântica**.

---

# Parte I — Fundação geométrica

## 5. Sistema de coordenadas

### Decisão v0.1

A geometria física canônica deve usar **coordenadas inteiras de 64 bits**, com unidade interna pequena e fixa.

Unidade inicial proposta:

```text
1 internal unit = 1 µm
```

Exemplos:

```text
0.10 mm = 100 units
0.20 mm = 200 units
1.00 mm = 1_000 units
100 mm  = 100_000 units
```

Motivos:

- comparações determinísticas;
- redução de erros de arredondamento em DRC;
- offsets/clearances reproduzíveis;
- serialização simples;
- grande margem numérica com `Int64`.

Valores de UI podem continuar em mm/mil e ser convertidos na boundary.

### Não fazer

Não usar pixel como unidade de domínio.

Não depender de `double` como fonte de verdade para regras de contato/clearance.

---

## 6. Polygon engine

### Decisão v0.1

Avaliar **Clipper2** como biblioteca geométrica principal para:

- union;
- intersection;
- difference;
- XOR;
- polygon offset/inflation;
- clipping;
- simplificação geométrica quando apropriada.

A implementação oficial possui suporte C# e operações com paths inteiros de 64 bits, alinhando-se ao modelo de coordenadas proposto.

### Uso no Place&Router

Exemplo de clearance:

```text
Obstacle polygon
     ↓
Inflate by required clearance + half track width
     ↓
Forbidden route region
```

Exemplo de component collision:

```text
transformed courtyard A
        ∩
transformed courtyard B
        ↓
empty? PASS : collision
```

### Regra

Biblioteca de geometria é uma implementação substituível atrás de `IGeometryKernel`.

A semântica do domínio não deve depender dos tipos da Clipper2.

---

## 7. Spatial indexing

A maior parte das avaliações deve operar em duas fases:

```text
Broad phase
    ↓
possíveis objetos próximos
    ↓
Exact phase
    ↓
polygon/segment evaluation
```

### Decisão v0.1

Usar um índice espacial dinâmico baseado em envelopes/AABB.

Candidato inicial prático em .NET:

- `NetTopologySuite.Index.Quadtree.Quadtree<T>` para objetos mutáveis durante placement/routing.

O Quadtree do NetTopologySuite suporta insert/query/remove e funciona como filtro primário; o teste geométrico exato continua sendo responsabilidade do geometry kernel.

### Não usar como índice principal mutável

`STRtree`/`HPRtree` são adequados para dados predominantemente estáticos e podem ser úteis para snapshots/read-only views, mas não devem ser assumidos como índice central do inner loop de placement, porque o estado físico muda continuamente.

---

# Parte II — Enriquecimento automático e redução de input

## 8. Automatic deterministic enrichment

Após importação, executar uma suíte local de enriquecimento antes de perguntar qualquer coisa ao usuário.

### 8.1 Normalização de nomes

Canonicalizar de maneira conservadora:

```text
GND / DGND / AGND
VCC / VDD / VBAT
D+ / D-
TX+ / TX-
RX+ / RX-
CLK / CLOCK
SCL / SDA
MOSI / MISO / SCK
```

A normalização **não deve fundir nets diferentes**. Ela apenas produz tags/candidates.

### 8.2 Pares diferenciais candidatos

Detectar padrões complementares de nomes e topologia:

```text
USB_D+ / USB_D-
TX_P / TX_N
RX+ / RX-
*_P / *_N
```

Resultado:

```text
candidate relationship
source = DETERMINISTIC_INFERENCE
confidence = ...
```

Não promover automaticamente para hard impedance/skew constraint sem dados suficientes.

### 8.3 Power/ground candidates

Usar:

- nomes;
- pin names importados;
- net classes do EDA;
- conectividade ampla;
- plane/pour metadata quando disponível.

Produzir classificação candidata, não corrente inventada.

### 8.4 Connectivity-strength graph

Construir um grafo de componentes com pesos derivados de:

- número de nets compartilhadas;
- número de conexões pad-to-pad;
- net criticality conhecida;
- relações semânticas confirmadas;
- fanout/degree.

Esse grafo alimenta grouping, placement seed e neighborhood discovery.

### 8.5 Missing-information dependency analysis

Não listar genericamente todos os campos vazios.

Para cada unknown, responder:

```text
Who needs this value?
Which deterministic calculation is blocked/degraded?
Is it needed now?
Can a safe fallback exist?
```

Somente unknowns relevantes para o estágio corrente entram como perguntas ao usuário.

---

## 9. Papel da IA na redução de input

A IA cloud entra depois do enriquecimento local para tarefas como:

- reconhecer functional blocks;
- reconhecer decoupling/feedback relationships;
- sugerir classification quando nomes/topologia não bastam;
- sugerir constraints de engenharia;
- explicar ao usuário por que determinada informação está sendo solicitada.

Fluxo:

```text
Import
  ↓
local deterministic inference
  ↓
AI semantic enrichment for unresolved/high-value cases
  ↓
user asked only for remaining material ambiguities
```

A IA é portanto uma camada de redução de trabalho manual, mas não uma fonte de medições físicas inventadas.

---

# Parte III — Constraint processing

## 10. Constraint resolution pipeline

```text
Imported rules
Manufacturing profile
Global project rules
Net classes
Groups
Entity-specific rules
Temporary transaction restrictions
       ↓
Constraint Resolver
       ↓
EffectiveConstraintSet
       ↓
Evaluators
```

### Ordem conceitual de especificidade

```text
Global
  < Manufacturing / project class
  < NetClass / ComponentClass
  < Group
  < Entity / Relationship
  < Explicit transaction restriction
```

A regra mais específica pode refinar uma regra ampla quando compatível.

Contradições Required não são resolvidas silenciosamente por prioridade: produzem `ConstraintConflict`.

---

## 11. Constraint evaluator registry

Cada família de regra tem evaluator determinístico.

Exemplos:

```text
MinimumSeparationEvaluator
InsideRegionEvaluator
AllowedRotationEvaluator
AllowedSideEvaluator
MaximumViaCountEvaluator
MaximumLengthEvaluator
ClearanceEvaluator
```

Interface conceitual:

```text
Constraint + PhysicalDesignState + EvaluationContext
        ↓
ConstraintEvaluation
```

Saída:

```text
PASS
FAIL
UNKNOWN
NOT_APPLICABLE
```

com evidence estruturada.

---

## 12. Constraint pre-solving

Antes do search, resolver deterministicamente tudo que não exige geometria complexa.

Exemplos:

- allowed side impossível;
- allowed rotations vazias;
- componente fixed fora do board;
- region intersection vazia;
- layer inexistente;
- footprint ausente;
- manufacturer rule incompatível.

### CP-SAT opcional

Avaliar **Google OR-Tools CP-SAT** para subproblemas discretos pequenos, por exemplo:

- assignment de grupos a regiões discretas;
- escolha de side;
- escolha de rotação entre opções discretas;
- ordering obrigatório;
- seleção de variantes mutuamente exclusivas.

CP-SAT **não será o geometry/placement engine principal**. Ele entra apenas quando um subproblema discreto é naturalmente modelável e uma solução exata/feasible é valiosa.

---

# Parte IV — Placement

## 13. Initial placement seed

Não iniciar Simulated Annealing a partir de posições puramente aleatórias quando há informação útil.

Sequência v0.1:

```text
1. preserve fixed/mechanical objects
2. respect required regions
3. assign major functional groups/regions
4. create coarse graph-based placement
5. legalize overlaps
6. run fast routability estimate
7. enter LNS/SA refinement
```

### 13.1 Fixed-first

Primeiro:

- connectors fixos;
- mounting holes;
- switches/LEDs mecanicamente definidos;
- imported LOCKED components;
- keepouts.

### 13.2 Group/region assignment

Usar:

- explicit user regions;
- semantic groups;
- weighted connectivity;
- required/preferred separation.

Quando a assignment for predominantemente discreta, CP-SAT pode ser testado como solver auxiliar.

### 13.3 Graph-based coarse placement

Para grupos/objetos livres, usar um seed orientado por conectividade, aproximando objetos fortemente conectados e respeitando fixed anchors.

A primeira versão pode usar uma heurística barycentric/force-inspired própria pequena sobre o weighted component graph.

Ela serve apenas para produzir um seed plausível; não é autoridade de qualidade final.

---

## 14. Legalization

Após qualquer coarse placement:

```text
candidate poses
    ↓
find overlaps / board violations
    ↓
local displacement / nearest legal candidate
    ↓
constraint check
```

Legalization nunca promove uma solução inválida silenciosamente. Se não houver deslocamento local viável, retorna failure e amplia o neighborhood.

---

## 15. Fast placement evaluator

Antes de global/detailed routing, usar métricas baratas.

### v0.1

```text
weighted HPWL
pad Manhattan distances
RSMT estimate for multi-pin nets
component density
pin escape pressure
critical relationship distances
region occupancy
coarse congestion demand
reserved corridor consumption
```

### Steiner estimate

Para nets multi-terminal, usar:

1. HPWL como filtro ultrabarato;
2. RMST/RSMT estimate para candidatos sobreviventes;
3. avaliar FLUTE ou implementação equivalente para obter RSMT rápido e mais informativo.

FLUTE é uma técnica clássica de Rectilinear Steiner Minimal Tree baseada em lookup tables, criada especificamente para estimativa/topologia de interconexões em physical design.

### Regra

O fast evaluator não precisa prever perfeitamente detailed routing.

Seu benchmark é:

> candidatos classificados como melhores por ele precisam ter correlação útil com routability/qualidade observadas nos estágios mais caros.

Essa correlação deve ser medida continuamente.

---

## 16. Large Neighborhood Search como estrutura principal

### Decisão v0.1

Usar **Large Neighborhood Search (LNS)** como mecanismo de reabertura controlada do physical design.

Conceito:

```text
current solution
    ↓
select related neighborhood
    ↓
relax/remove part of solution
    ↓
reoptimize that part
    ↓
reinsert / reroute
    ↓
evaluate
```

Isso se encaixa naturalmente no Place&Router porque routing failure, congestion hotspot ou finding já definem neighborhoods relacionados.

### Neighborhood selectors v0.1

```text
FunctionalBlockNeighborhood
RoutingFailureNeighborhood
CongestionHotspotNeighborhood
CriticalNetNeighborhood
ConnectivityNeighborhood
SpatialNeighborhood
FindingNeighborhood
```

A seleção pode ser determinística ou solicitada pela IA em macro-iterations.

---

## 17. Simulated Annealing dentro dos neighborhoods

### Decisão v0.1

Usar **Simulated Annealing (SA)** como mecanismo inicial para escapar de mínimos locais durante placement/refinement.

Moves:

```text
TranslateComponent
RotateComponent
SwapComponents
TranslateCluster
RotateCluster
RepackNeighborhood
```

Hard-invalid moves são rejeitados antes da avaliação completa.

### Acceptance

```text
if Δcost <= 0
    accept
else
    accept with probability exp(-Δcost / T)
```

### Schedule

A temperatura/schedule inicial não deve ser fixada por dogma. Deve ser calibrada por benchmark.

Guardar:

- initial temperature;
- cooling factor;
- moves per temperature;
- reheats;
- no-improvement threshold;
- random seed.

---

## 18. Multi-fidelity candidate funnel

Não detailed-route todos os candidatos.

Exemplo:

```text
10,000 generated poses
      ↓ hard filters
 4,100 legal
      ↓ cheap score
   500 promising
      ↓ global routing
    50 promising
      ↓ local detailed routing
     5 finalists
      ↓ expensive review/regression
```

As quantidades são ilustrativas e devem ser configuráveis/adaptativas.

Essa estrutura é essencial para manter o produto rápido em hardware comum.

---

# Parte V — Global routing

## 19. Separar global e detailed routing

A arquitetura deve seguir a prática consolidada de physical design:

- **global router** decide aproximadamente por onde/layer a net deve passar e mede capacidade/congestionamento;
- **detailed router** produz geometria legal de tracks/vias respeitando regras finas.

O global router entrega `RouteGuide`/`ReservedCorridor`, não cobre final.

---

## 20. Routing resource grid

### Decisão v0.1

Criar uma representação coarse por layer:

```text
RoutingGrid
 └── LayerGrid[]
      └── Cell/Edge
           ├── totalCapacity
           ├── reservedCapacity
           ├── committedDemand
           ├── historicalCongestionCost
           ├── obstacleFraction
           └── localPenalty
```

A geometria real permanece contínua. O grid é uma view computacional.

### Grid pitch

Não expor ao usuário leigo.

Derivar automaticamente de:

- board dimensions;
- manufacturing minimums;
- typical routing pitch;
- layer count;
- performance target.

Permitir override avançado apenas para debugging/benchmark.

---

## 21. Global route topology

Para uma net:

```text
pads
 ↓
Steiner/RMST topology estimate
 ↓
route each branch through coarse resource graph
 ↓
RouteGuide / reservation
```

Two-pin net:

- A*/Dijkstra-like search sobre coarse graph.

Multi-pin net:

- RSMT/RMST topology primeiro;
- branches roteadas com capacidade compartilhada.

---

## 22. Negotiated congestion

### Decisão v0.1

Adotar uma estratégia **PathFinder-like** de negociação de recursos para global routing/rip-up-reroute.

Ideia:

```text
route nets
   ↓
find overused resources
   ↓
increase present/historical cost of congestion
   ↓
rip conflicting routes/guides
   ↓
route again
   ↓
repeat until no overflow or budget exhausted
```

Custos podem considerar:

```text
base distance
present congestion
historical congestion
via transition
criticality
preferred/undesired layer direction
reserved critical corridors
```

Isso evita uma estratégia greedy em que as primeiras nets monopolizam os melhores corredores.

---

## 23. Net ordering

A ordem inicial deve ser derivada localmente.

Score de constrainedness pode considerar:

```text
explicit priority
number of legal layers
pin escape difficulty
current/width demand
number of endpoints
differential-pair status
length/skew constraints
available corridor alternatives
```

A IA pode alterar prioridade em uma macro-decisão, mas existe sempre um baseline determinístico.

---

# Parte VI — Detailed routing

## 24. Pin-access analysis

Antes de tentar rotear uma net, gerar/avaliar access points de pads.

Para cada pad:

```text
possible exits
allowed directions
local clearance
nearby obstacles
possible via access
neckdown requirement
```

Isso evita encontrar um caminho global bom que seja impossível de conectar fisicamente ao pad.

Pin access deve alimentar também o placement evaluator.

---

## 25. Detailed routing graph

### Decisão v0.1

Usar search graph 2.5D com estado semelhante a:

```text
RouteNode
 ├── x
 ├── y
 ├── layer
 └── incomingDirection
```

Ações:

```text
horizontal
vertical
45-degree diagonal
layer transition via
```

O grid fino/pontos navegáveis são derivados das regras de fabricação e das geometrias relevantes do neighborhood.

### Observação

Não assumir um grid uniforme gigante cobrindo toda a PCB em resolução mínima.

Preferir geração local/adaptativa do search space para evitar explosão de memória.

---

## 26. A* como search principal

### Decisão v0.1

Usar **A*** como shortest-path search primário do detailed router.

Heurística base:

```text
Manhattan/octile distance to nearest target
+ minimum required layer-change estimate
```

Custos reais incluem:

```text
length
bend cost
via cost
congestion/history cost
undesired layer/direction cost
proximity preference cost
corridor deviation cost
```

Hard obstacles simplesmente removem estados/edges possíveis.

### Hadlock como benchmark/alternative strategy

Hadlock é uma alternativa clássica específica para grid routing e deve ser mantida como Strategy/benchmark possível.

Não há necessidade de implementar Lee, Hadlock e A* simultaneamente no primeiro commit. A arquitetura `IRouteSearchStrategy` deve permitir comparação posterior.

---

## 27. Clearance por obstacle inflation

Em vez de checar cada ponto contra cada shape durante o search:

```text
obstacle
  ↓ inflate(clearance + route half-width)
forbidden region for route centerline
```

Vias usam inflation compatível com seu annular geometry/clearance.

Exact DRC ainda roda depois da geração da rota.

---

## 28. Route cleanup

Após A* produzir um path discreto:

```text
remove collinear points
merge compatible segments
pull corners/tighten where safe
validate 45° geometry
exact DRC
```

O cleanup não pode alterar topologia sem revalidação.

---

## 29. Differential pair routing

Não tratar P/N como duas nets independentes.

Estratégia inicial:

```text
pair object
  ↓
route a center corridor / coupled search
  ↓
generate paired tracks with required gap
  ↓
exact clearance + pair gap validation
  ↓
skew measurement
```

Tuning/meanders entram somente se necessário.

Global routing reserva capacidade para o par como conjunto.

---

## 30. Rip-up / reroute escalation

Quando detailed routing falhar:

```text
1. alternate local path
2. alternate access point
3. alternate legal layer
4. local rip-up of low-cost blockers
5. negotiated reroute of local net set
6. larger routing neighborhood
7. declare placement-related blockage
8. open PhysicalDesign repair neighborhood
```

### Rip-up cost

Evitar arrancar tudo indiscriminadamente.

Custo de rip-up considera:

- net criticality;
- number of dependent routes;
- how recently route stabilized;
- previous failure history;
- disruption caused;
- user lock/preserve policy.

Required/locked routes não são ripadas sem autorização explícita compatível.

---

# Parte VII — Avaliação, score e regressão

## 31. Validity separada de quality

Primeiro:

```text
Is candidate valid?
```

Depois:

```text
How good is the valid candidate?
```

Nunca:

```text
invalid candidate + large penalty = maybe acceptable
```

---

## 32. Normalização de métricas

Métricas têm escalas diferentes.

Exemplo:

```text
wirelength = 1,850 mm
vias = 47
max congestion = 0.82
loop area = 31 mm²
```

Antes do weighted score, transformar em valores comparáveis.

Estratégias iniciais:

- baseline-relative delta;
- bounded target normalization;
- design-size normalization;
- per-net/per-area normalization.

Exemplo:

```text
normalizedWirelength = wirelength / baselineWirelength
normalizedVias       = viaCount / max(1, baselineViaCount)
```

Pesos e normalização precisam aparecer no run metadata.

---

## 33. Fast versus exact metrics

Cada métrica deve declarar fidelity:

```text
ESTIMATE
GLOBAL_ROUTE_DERIVED
DETAILED_ROUTE_DERIVED
EXACT_GEOMETRY
SIGNOFF_PROXY
```

A IA e a UI não devem apresentar um estimate como medição exata.

---

## 34. Regression engine incremental

Uma `PhysicalDesignTransaction` produz:

```text
changed objects
     ↓
dependency closure
     ↓
affected constraints
nets
regions
routing cells
semantic relations
     ↓
recompute affected evaluations
     ↓
compare baseline
```

Saída:

```text
Resolved
NewRegression
Degraded
Improved
Unchanged
Unknown
```

Global suites entram periodicamente para detectar erros de dependency tracking.

---

# Parte VIII — Quando a IA é chamada

## 35. IA não participa da execução local normal

Não chamar IA para:

```text
polygon clipping
collision
clearance
R-tree/quadtree query
HPWL/RSMT estimate
A*
via placement search
congestion update
LNS candidate generation
SA acceptance
DRC
constraint inheritance
score normalization
regression diff
```

---

## 36. Eventos que justificam macro-iteration cloud

Exemplos:

```text
semantic classification unresolved
functional grouping uncertain
constraint suggestion requested
repeated deterministic failure with multiple repair classes
optimizer stalled despite valid search
trade-off cannot be resolved by configured objective/policy
functional block stabilized and semantic review is useful
whole-board candidate ready for adversarial review
```

A chamada recebe fatos produzidos pelas etapas locais, nunca a placa crua inteira por padrão.

---

# Parte IX — Usabilidade e zero-tuning

## 37. Perfis operacionais

O usuário comum não deve configurar pesos de A*, temperaturas de annealing ou grid pitch.

Oferecer perfis de intenção:

```text
Balanced
Routing-first
Compact
Low-via
EMI-conscious
Manufacturing-conservative
```

Esses perfis mapeiam internamente para parâmetros técnicos versionados.

Advanced Settings pode expor tuning somente para usuários experientes e benchmarking.

---

## 38. Manufacturing profile como fonte de defaults

Ao selecionar um perfil de fabricação, derivar automaticamente:

- min trace;
- min spacing;
- via/drill sizes;
- annular constraints;
- copper-edge;
- allowed via types;
- preferred conservative defaults.

O usuário não deve copiar manualmente essas regras para cada net.

---

## 39. Automatic questions policy

Quando informação realmente faltar, a UI gera uma pergunta com quatro elementos:

```text
Question
Why it is needed
What changes depending on the answer
Safe/default alternatives, if any
```

Exemplo:

```text
Qual a corrente máxima aproximada de VIN_MOTOR?

Precisamos disso apenas para dimensionar a largura da rota.
Não afeta a conectividade nem o placement atual.

[  A ]
[Usar largura da net class importada]
[Usar mínimo conservador do fabricante]
[Deixar desconhecido]
```

---

## 40. Explain advanced decisions in engineering terms

Não mostrar ao usuário comum:

```text
presentFactor = 1.37
historyFactor = 0.82
annealing T = 14.7
```

Mostrar:

```text
Routing congestion is still high in the east corridor.
The optimizer is temporarily increasing the cost of reusing this region and rerouting competing nets.
```

Raw algorithm parameters ficam em diagnostics/benchmark mode.

---

# Parte X — Bibliotecas e reutilização

## 41. Dependências candidatas v0.1

### Clipper2

Uso pretendido:

- polygon boolean operations;
- offsetting/inflation;
- clipping.

Status:

```text
STRONG CANDIDATE
```

Validar performance e edge cases antes de ADR final.

### NetTopologySuite

Uso pretendido inicialmente:

- spatial indexing (`Quadtree<T>`), não como geometry source of truth.

Status:

```text
CANDIDATE
```

Se profiling mostrar overhead, substituir atrás da abstração de spatial index.

### Google OR-Tools CP-SAT

Uso pretendido:

- pequenos subproblemas discretos de assignment/feasibility.

Status:

```text
OPTIONAL CANDIDATE
```

Não introduzir se os primeiros casos não justificarem a dependência.

---

## 42. Referências externas e código de terceiros

Projetos existentes devem ser usados de três maneiras distintas:

### A. Biblioteca incorporável

Somente se:

- licença compatível;
- boundary arquitetural clara;
- benefício concreto;
- tests próprios verificarem comportamento.

### B. Referência algorítmica

Estudar técnicas e papers sem copiar implementação incompatível.

### C. Benchmark externo

Executar o mesmo design em outra ferramenta para comparar:

- completion rate;
- vias;
- length;
- runtime;
- qualitative layout.

### Freerouting

Freerouting é uma referência/benchmark útil de autorouting PCB e suporta DSN/SES, mas seu repositório é GPL-3.0.

Até uma política de licenciamento do Place&Router dizer o contrário:

```text
Freerouting code = NO direct incorporation
Freerouting behavior/results = useful benchmark/reference
```

---

# Parte XI — Algoritmo por momento do fluxo

## 43. Matriz operacional v0.1

| Momento | Algoritmo/mecanismo principal | Fallback/escalada | IA cloud? |
|---|---|---|---|
| Import | parser/adapter específico | diagnostics + user resolve | não |
| Canonicalização | rules determinísticas | Unknown/provenance | não |
| Semantic obvious tags | name/topology heuristics | semantic AI | quando ambíguo |
| Missing-data resolution | dependency analysis | user question | opcional para explicar |
| Geometry | integer polygons + Clipper2 candidate | alternate kernel | não |
| Spatial lookup | dynamic Quadtree candidate | custom/dynamic R-tree | não |
| Constraint resolution | deterministic specificity + evaluators | conflict diagnostic | não |
| Discrete assignments | heuristics / optional CP-SAT | LNS | não |
| Initial placement | fixed-first + graph-based seed | multiple seeds | não |
| Placement refinement | LNS + SA | larger neighborhood / alternate seed | IA só escolhe strategy/focus |
| Fast net estimate | HPWL | RMST/RSMT/FLUTE | não |
| Routability estimate | coarse routing grid | finer grid | não |
| Global routing | Steiner topology + A*/Dijkstra | negotiated congestion | não |
| Congestion repair | PathFinder-like cost negotiation | placement repair | IA quando diagnóstico é ambíguo |
| Pin access | deterministic access-point generation | move/rotate component | não |
| Detailed routing | A* 2.5D | Hadlock/alternate strategy later | não |
| Detailed route conflict | local alternate/rip-up | negotiated local reroute | não inicialmente |
| Persistent routing failure | deterministic RouteFailure | LNS placement repair | IA pode selecionar repair class |
| DRC | exact geometry/rules | reject | nunca |
| Score | normalized deterministic metrics | policy/weights | IA só em trade-off não codificado |
| Regression | dependency-driven incremental evaluation | global suite | nunca para autoridade |
| Functional review | deterministic checks first | semantic review | sim, quando útil |
| Final validity | deterministic signoff-supported checks | fail/open finding | nunca |

---

# Parte XII — Sequência de implementação

## 44. Local Engine LE-01 — Units + Geometry Kernel

Implementar:

- `Length`, `Angle` e coordinate types;
- micron-based Int64 coordinate;
- polygon/path primitives;
- transforms;
- Clipper2 adapter candidate;
- exact overlap/distance/offset tests;
- test vectors de edge cases.

Aceite:

- resultados determinísticos;
- transform/rotation sem drift;
- clearance inflation reproduzível.

---

## 45. LE-02 — Spatial Index + affected-neighborhood queries

Implementar:

- `ISpatialIndex<T>` interno;
- Quadtree adapter candidate;
- insert/remove/query;
- broad + exact phase;
- queries por layer;
- invalidation após transaction.

Aceite:

- mover um componente atualiza index sem rebuild global obrigatório;
- query nunca substitui exact test.

---

## 46. LE-03 — Constraint Resolution/Evaluation

Implementar:

- effective constraint resolution;
- evaluator registry;
- hard validity;
- conflict detection;
- provenance/evidence.

Aceite:

- nenhuma hard rule vira mera penalidade;
- contradictions explícitas produzem diagnostics.

---

## 47. LE-04 — Automatic enrichment + readiness dependencies

Implementar:

- naming candidates;
- net/group graph;
- differential-pair candidates;
- power/ground candidates;
- missing-information dependency graph;
- ranked user questions.

Aceite:

- import comum não gera formulário gigantesco de propriedades desconhecidas.

---

## 48. LE-05 — Fast placement metrics

Implementar:

- HPWL;
- weighted pad distance;
- RMST baseline;
- density;
- pin escape pressure;
- critical relations;
- early congestion grid.

Depois avaliar FLUTE/RSMT.

Aceite:

- evaluator roda ordens de grandeza mais rápido que detailed route;
- métricas são decomponíveis.

---

## 49. LE-06 — Placement Seed + Legalizer

Implementar:

- fixed-first;
- region assignments;
- graph-oriented coarse seed;
- multiple deterministic/random seeded alternatives;
- overlap legalization.

Aceite:

- board pequeno recebe placement inicial legal sem IA.

---

## 50. LE-07 — Global Router

Implementar:

- coarse routing resource grid;
- route guide;
- capacity/demand;
- two-pin A*/Dijkstra;
- multi-pin RMST/RSMT topology;
- historical congestion costs;
- reservations.

Aceite:

- produz hotspot/corridor diagnostics úteis para placement.

---

## 51. LE-08 — LNS + Simulated Annealing

Implementar:

- neighborhood interface;
- standard selectors;
- move operators;
- SA acceptance/schedule;
- multi-fidelity candidate funnel;
- reproducible random seeds.

Aceite:

- melhora placements sem LLM e sem hard regressions.

---

## 52. LE-09 — Detailed Router v0.1

Implementar:

- pin-access candidates;
- local/adaptive search graph;
- 2.5D A*;
- horizontal/vertical/45° moves;
- vias;
- obstacle inflation;
- path cleanup;
- exact post-route DRC.

Aceite:

- rotas simples e multilayer suportadas respeitam as hard rules implementadas.

---

## 53. LE-10 — Negotiated rip-up/reroute

Implementar:

- present/history congestion cost;
- blocker selection;
- rip-up cost;
- local net-set reroute;
- failure budgets;
- escalation para placement repair.

Aceite:

- ordem greedy inicial não decide permanentemente o resultado.

---

## 54. LE-11 — Regression + incremental dependency engine

Implementar:

- affected-entity discovery;
- constraint dependency closure;
- metrics invalidation;
- baseline compare;
- periodic global verification.

Aceite:

- local transaction não exige global recomputation para todo caso comum;
- global suite detecta inconsistências.

---

## 55. LE-12 — IA sobre engine já funcional

Somente então conectar AgentOperations a capacidades locais reais:

```text
semantic.classify
constraint.suggest
optimization.focus.select
routing.failure.diagnose
repair.plan
block.review
global.review
```

A IA solicita operações locais e interpreta resultados; não implementa geometria/routing via texto.

---

# Parte XIII — Benchmarks obrigatórios

## 56. Comparar algoritmo, não opinião

Cada Strategy candidata precisa de benchmark reproduzível.

Exemplos:

```text
A* vs Hadlock
HPWL vs RMST/RSMT correlation
with/without negotiated congestion
SA schedules
LNS neighborhood selectors
Quadtree vs alternative spatial index
Clipper2 C# vs native bridge only if profiling justifies
```

Não adotar complexidade adicional sem evidência de melhoria relevante.

---

## 57. Benchmark externo de routing

Quando possível, comparar boards compatíveis com ferramentas externas como Freerouting, sem exigir equivalência de objetivos.

Registrar:

- routed/unrouted;
- hard violations suportadas;
- via count;
- route length;
- runtime;
- visual/engineering review.

A comparação serve como referência de maturidade do router, não como especificação de comportamento.

---

## 58. Critério de sucesso da estratégia local

A arquitetura local está correta quando uma run consegue, **sem IA cloud**:

1. importar um design suportado;
2. construir estado físico;
3. validar regras básicas;
4. gerar placement inicial;
5. estimar routability;
6. melhorar placement por search;
7. global-route;
8. detailed-route nets suportadas;
9. detectar failures com evidence;
10. rip-up/reroute quando apropriado;
11. reabrir placement quando routing demonstrar necessidade;
12. rejeitar regressões Required;
13. produzir candidate reproduzível.

A IA deve melhorar estratégia, semântica e qualidade de review. Ela não deve ser necessária para o motor local saber **como mover, medir, rotear, validar e voltar atrás**.

---

# Parte XIV — Referências técnicas iniciais

Estas referências orientam algoritmos e benchmarking; não significam incorporação de código de terceiros.

- Hart, Nilsson, Raphael — **A Formal Basis for the Heuristic Determination of Minimum Cost Paths**, IEEE, 1968. DOI `10.1109/TSSC.1968.300136` — A*.
- Hadlock — **A shortest path algorithm for grid graphs**, Networks, 1977. DOI `10.1002/net.3230070404`.
- Kirkpatrick, Gelatt, Vecchi — **Optimization by Simulated Annealing**, Science, 1983. DOI `10.1126/science.220.4598.671`.
- Guttman — **R-trees: A Dynamic Index Structure for Spatial Searching**, SIGMOD, 1984. DOI `10.1145/602259.602266`.
- Shaw — **Using Constraint Programming and Local Search Methods to Solve Vehicle Routing Problems**, CP 1998. DOI `10.1007/3-540-49481-2_30` — origem do conceito de LNS usado aqui como estrutura geral de relax/reoptimize.
- McMurchie, Ebeling — **PathFinder: A Negotiation-Based Performance-Driven Router for FPGAs**, FPGA 1995 — referência para negotiated congestion.
- Chu, Wong — **FLUTE: Fast Lookup Table Based Rectilinear Steiner Minimal Tree Algorithm for VLSI Design**, IEEE TCAD, 2008. DOI `10.1109/TCAD.2007.907068`.
- OpenROAD/FastRoute/TritonRoute — referência prática para separação global-route → guides → detailed route, congestion e pin access.
- Clipper2 — biblioteca de clipping/offsetting com implementação C# e paths inteiros.
- NetTopologySuite — biblioteca .NET com índices espaciais, incluindo Quadtree dinâmico.
- Google OR-Tools CP-SAT — solver opcional para subproblemas discretos.
- Freerouting — benchmark/referência de PCB autorouting e interoperabilidade DSN/SES; não incorporar código GPL-3.0 sem decisão explícita de licenciamento.

---

## 59. Decisões que ainda exigem benchmark, não discussão abstrata

Não fixar antecipadamente sem experimento:

- tamanho/resolução ideal do global routing grid;
- estratégia final de dynamic spatial index;
- uso definitivo de FLUTE versus implementação própria/RMST em v0.1;
- SA temperature schedule;
- LNS neighborhood sizes;
- pesos e normalização final de score;
- threshold de escalada routing → placement repair;
- A* versus Hadlock em casos específicos;
- necessidade de native geometry acceleration;
- ganho real de CP-SAT nos subproblemas discretos.

Esses itens devem possuir defaults experimentais e benchmark harness desde a implementação inicial.

---

## 60. Resumo executivo

A primeira implementação local segue esta tese:

```text
Known theory + mature library
        before
custom algorithm
        before
cloud reasoning
```

Mais concretamente:

```text
Integer computational geometry
+ spatial index
+ deterministic constraint engine
+ graph-derived placement seed
+ HPWL/RMST/RSMT estimates
+ LNS / Simulated Annealing
+ coarse capacity-based global routing
+ negotiated congestion
+ A* detailed routing
+ rip-up/reroute
+ incremental regression
```

A IA fica acima dessa base, escolhendo **o que merece atenção e por quê**, enquanto o processamento local decide **como executar geometricamente e se o resultado é válido**.
