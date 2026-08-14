# 04 — Physical Design Optimizer: placement + routing conjuntos

## 1. Objetivo

O Physical Design Optimizer é o núcleo que transforma um design eletricamente definido, enriquecido com constraints, em um ou mais estados físicos candidatos.

Ele não deve ser modelado como:

```text
PlacementEngine
      ↓
RoutingEngine
      ↓
Done
```

Mesmo que existam componentes internos especializados em placement e routing, a abstração superior deve ser um **co-optimizer** trabalhando sobre o mesmo `PhysicalDesignState`.

Nomes conceituais úteis:

```text
PhysicalDesignEnvironment
PhysicalDesignState
PhysicalDesignTransaction
PhysicalDesignOptimizer
```

> A estratégia algorítmica local v0.1 — geometry/indexing, HPWL/RMST/RSMT, LNS, Simulated Annealing, global routing por capacidade, negotiated congestion, A* detailed routing e rip-up/reroute — está detalhada em [`10-Processamento-Local-e-Algoritmos-Deterministicos.md`](10-Processamento-Local-e-Algoritmos-Deterministicos.md). Este documento permanece focado na arquitetura conjunta do optimizer.

## 2. Estado otimizado

O optimizer pode modificar, respeitando locks e constraints:

- component poses;
- component rotations;
- board side;
- cluster placement;
- routing corridors;
- layer assignments;
- route topology;
- detailed tracks;
- vias;
- local copper geometry;
- reserved routing capacity.

Futuramente pode também considerar:

- copper pours;
- thermal copper;
- return vias;
- guard structures;
- plane stitching;
- outros elementos físicos.

## 3. Placement routing-aware desde o início

Ao testar um placement, não é suficiente medir somente HPWL ou distância Euclidiana entre componentes.

O sistema deve estimar:

- se as nets relacionadas continuam roteáveis;
- que layers provavelmente serão usadas;
- congestionamento causado;
- corridors consumidos;
- vias estimadas;
- impacto em nets já planejadas;
- acessibilidade dos pads;
- possibilidade de saída do footprint;
- impacto em blocos vizinhos.

Exemplo conceitual:

```text
Candidate: U7 @ (38,27), rot=90°

Geometry                 PASS
Required constraints      PASS
Estimated wire length     184 mm
Estimated vias            8
Local congestion          0.31
USB corridor capacity     73% → 31%
Critical loop area        28 mm²

Decision candidate:
likely poor because it consumes critical USB corridor
```

## 4. Routing provisional e global

Durante placement, não é necessário produzir cobre final a cada movimento.

O sistema deve possuir níveis de fidelidade.

### Level A — fast geometric estimate

Barato:

- pad distances;
- HPWL;
- Manhattan estimates;
- RMST/RSMT approximations quando necessário;
- fanout difficulty;
- local density.

Serve para descartar candidatos evidentemente ruins.

### Level B — global routing / corridor planning

Produz:

- corridors;
- layer preferences;
- congestion/capacity;
- approximate via transitions;
- routing conflicts;
- resource reservation.

Ainda não precisa gerar a geometria final da track.

### Level C — local detailed routing

Ao mover um componente, reroute apenas:

- suas nets;
- nets diretamente afetadas;
- vizinhos próximos que compartilham corridors.

### Level D — global detailed routing

Executado periodicamente ou em candidatos promissores.

### Level E — sign-off / expensive analysis

Executado somente em finalistas:

- exact DRC suportado;
- length/skew;
- impedance checks disponíveis;
- SI/PI/thermal mais detalhados quando existirem;
- manufacturing checks.

A separação existe por custo computacional, não porque placement e routing sejam independentes.

## 5. Routing Reservation Map

Antes de uma net possuir rota definitiva, ela pode consumir recursos previstos.

Exemplo visual:

```text
L1
┌──────────────────────────────────┐
│                                  │
│ U1           ███████████         │
│              █ USB pair █        │
│              █ corridor █        │
│              ███████████         │
│                                  │
│    ▒▒▒▒▒▒▒▒ SPI corridor         │
│                                  │
│                      U7          │
└──────────────────────────────────┘
```

Colocar C32 sobre o corredor pode gerar:

```text
valid geometry: yes
USB corridor capacity before: 73%
capacity after: 31%
estimated reroute: +18.3 mm
estimated vias: +2
recommendation: reject candidate
```

Essa estrutura é uma das principais ligações entre placement e routing futuro.

## 6. Router determinístico

A primeira versão não deve depender de RL para desenhar cada segmento.

A direção v0.1 é:

- global routing sobre resource grid coarse;
- A*/Dijkstra-like search para guides;
- negotiated congestion/rip-up-reroute;
- pin-access analysis;
- A* 2.5D como detailed route search inicial;
- Hadlock como Strategy/benchmark possível;
- exact geometry/DRC depois da geração da rota.

Outras técnicas podem entrar futuramente:

- Lee/maze variants;
- visibility graph onde fizer sentido;
- push-and-shove;
- specialized differential-pair search.

A IA pode decidir estratégia; o router calcula geometria exata.

## 7. O que o reasoning agent pode delegar ao router

Exemplo:

```text
Route USB pair first
Prefer L1
Avoid power switching region
Use east corridor
Minimize vias
Preserve pair symmetry
```

O router resolve:

```text
segment coordinates
exact clearances
via coordinates
layer changes
```

Essa divisão evita usar LLM para path finding numérico de baixo nível.

## 8. Routing pode alterar placement

Esse requisito é obrigatório.

Exemplo:

```text
ROUTING FAILURE
N137 cannot be routed

Cause:
required passage between U17 and J3 = 1.46 mm
available = 0.91 mm

Potential repairs:
A. move U17 +0.8 mm X
B. rotate C42 90°
C. move J3 — forbidden by mechanical constraint
```

O optimizer pode testar B e, se necessário, A.

Portanto, nenhum componente deve ser considerado “finalizado” apenas porque foi colocado anteriormente, exceto quando explicitamente locked/frozen.

## 9. Local repair primeiro

Quando uma alteração cria problema, o sistema não deve reorganizar a placa inteira imediatamente.

Estratégia:

1. identificar causa provável;
2. encontrar neighborhood afetado;
3. gerar pequenos repairs;
4. avaliar impacto;
5. ampliar neighborhood somente se necessário.

Exemplo:

```text
Issue:
ADC_REF too close to switching corridor

Repair candidates:
1. reroute N17 on L3
2. move C17 +1.4 mm north
3. rotate U3 90°
4. move whole ADC block

Estimated disruption:
1 = low
2 = low/medium
3 = high
4 = very high
```

## 10. Large Neighborhood Search

LNS é a estrutura principal v0.1 de reabertura/reotimização controlada porque permite destruir/reotimizar partes relacionadas do design.

Neighborhoods possíveis:

- um componente e seus passivos;
- um bloco funcional;
- componentes em volta de um hotspot;
- uma região de congestionamento;
- uma interface e seus endpoints;
- um conjunto de nets críticas;
- todos os componentes relacionados a um finding.

Fluxo:

```text
choose neighborhood
      ↓
release selected objects/routes
      ↓
generate many candidate arrangements
      ↓
fast score
      ↓
global routing estimate
      ↓
detailed evaluation of promising candidates
      ↓
accept best improvement or controlled uphill move
```

## 11. Simulated Annealing

Simulated Annealing complementa LNS na estratégia v0.1 para evitar ficar preso em mínimos locais.

Movimentos candidatos:

- small translation;
- rotation;
- swap;
- cluster translation;
- cluster rotation;
- local route topology change;
- corridor change;
- layer reassignment.

Todas as ações passam por hard-constraint filtering.

O temperature schedule exato é benchmark-gated, não uma decisão arquitetural fixa.

## 12. MCTS como evolução, não pré-requisito

MCTS pode ser útil quando o valor de uma alteração só aparece após várias decisões futuras.

Exemplo:

> mover U7 piora wirelength agora, mas libera corredor que permite reduzir várias nets críticas depois.

Isso é um caso clássico em que greedy search falha.

Porém, MCTS não deve bloquear o primeiro protótipo. Primeiro devemos validar:

- state model;
- fast evaluator;
- transactions;
- local optimization;
- global routing feedback.

Depois MCTS pode explorar árvores de ações propostas por heurísticas ou pelo LLM.

## 13. Candidate action masking / filtering

A geração de candidatos deve remover ações inválidas cedo.

Exemplos:

```text
component outside board    → do not evaluate
courtyard collision        → invalid
forbidden side             → invalid
forbidden rotation         → invalid
mechanical keepout         → invalid
manufacturer violation     → invalid
```

Isso reduz enormemente o search space e evita gastar IA/CPU em soluções impossíveis.

## 14. Pin/pad accessibility

Ao posicionar um componente, o evaluator deve considerar não só o centro do footprint, mas como seus pads ficam expostos.

Exemplo:

```text
U7 rotation 0°   score 67
U7 rotation 90°  score 81
U7 rotation 180° score 43
U7 rotation 270° score 92
```

Diferenças podem resultar de:

- VIN voltado para input capacitor;
- SW voltado para inductor;
- FB voltado para quiet region;
- pads digitais voltados para o MCU;
- pad fanout sem obstruções.

## 15. Functional cluster placement

Certas relações são mais fortes do que conexão de net genérica.

Exemplos:

### Decoupling

```text
U1.VDD → C17 → GND return/via
```

O objetivo pode ser minimizar path/loop inductance, não apenas distância entre centros.

### Buck

```text
CIN → switching devices/controller → return to CIN
```

O loop pode ser otimizado como entidade.

### Op-amp feedback

Feedback components e input paths podem constituir cluster semântico.

### Crystal

Crystal, load capacitors e relevant MCU pins devem ser considerados juntos.

## 16. Differential pairs

Differential pair não deve ser tratado como duas nets independentes durante detailed routing.

Objeto conceitual:

```text
DifferentialPair
 ├── P
 ├── N
 ├── target impedance
 ├── gap constraints
 ├── max skew
 ├── via policy
 └── reference plane requirements
```

Global routing deve reservar corridor suficiente para o par como conjunto.

## 17. Power, high-current e switching nets

Essas nets podem exigir objetivos específicos:

- width/copper capacity;
- loop area;
- minimal high-dv/dt area;
- proximity to decoupling;
- thermal copper;
- separation from sensitive nodes;
- return paths.

A função de custo global não pode considerar todas as nets equivalentes.

## 18. Congestion model

Congestion deve existir por layer e por região.

Conceitos:

```text
available capacity
reserved capacity
committed tracks
historical congestion cost
via density
pin escape demand
critical corridor occupancy
```

O modelo inicial usa resource grid como view derivada, enquanto a geometria final permanece contínua.

## 19. Score multiobjetivo

Exemplo conceitual:

```text
Score =
  normalized weighted_wirelength
+ normalized congestion_penalty
+ normalized via_cost
+ normalized critical_loop_cost
+ normalized EMI_proxy_cost
+ normalized crosstalk_proxy_cost
+ normalized thermal_proxy_cost
+ preference_violations
+ manufacturing_cost_preferences
- normalized spare_routing_capacity_bonus
```

Required violations ficam fora do score normal: tornam o state inválido.

A UI deve conseguir decompor o score por categoria.

A normalização concreta é benchmark-gated e registrada no run metadata.

## 20. Trade-offs explícitos

Se dois objetivos conflitarem, o sistema deve registrar o compromisso.

Exemplo:

```text
Candidate B selected over A

B adds:
+1 via on non-critical SPI net

B improves:
- feedback loop length 31%
- SW/ADC separation 4.2 mm
- critical congestion 17%
```

Isso é melhor do que apenas exibir `Score 92.7`.

## 21. Transações durante otimização

Fluxo básico:

```text
Current State A
      ↓
Begin transaction
      ↓
move/rotate/rip/reroute
      ↓
Candidate State B
      ↓
local recomputation
      ↓
dependency impact
      ↓
regression evaluation
      ↓
score comparison
      ↓
commit / repair / rollback
```

A placa principal nunca deve ser alterada irreversivelmente por uma ação experimental.

## 22. Incremental recomputation

Mover U37 não deve forçar recalcular absolutamente tudo.

O engine deve descobrir:

```text
affected components
affected nets
affected regions
affected corridors
affected constraints
affected congestion cells
```

E recalcular localmente o máximo possível.

Global reviews continuam existindo periodicamente.

## 23. Freeze e thaw

Para reduzir churn, o optimizer pode congelar temporariamente regiões estáveis.

Estados possíveis:

- active;
- stable/frozen;
- locked by user;
- reopened because of routing failure.

Freeze automático nunca pode impedir repair quando uma hard constraint exigir alteração.

## 24. Multi-candidate design

O produto pode manter diversos candidatos vivos.

```text
Candidate #12
Candidate #18
Candidate #23
```

Isso permite explorar diferentes estratégias de floorplanning sem destruir alternativas.

Comparação:

- routability;
- score breakdown;
- vias;
- congestion;
- critical constraints;
- area utilization;
- review findings.

## 25. Floorplanning hierárquico

Antes de placement fino, o optimizer pode criar regiões aproximadas:

```text
POWER
ANALOG
DIGITAL
RF
CONNECTORS
MEMORY
```

Essas regiões podem vir:

- explicitamente do usuário;
- de semantic groups;
- de sugestões do agente;
- de otimização.

O floorplan não precisa ser rígido. Ele fornece estrutura inicial ao search.

## 26. Iteração completa conceitual

```text
1. choose current problem/hotspot
2. determine affected neighborhood
3. generate candidate actions
4. reject invalid actions
5. run fast geometric/routing estimate
6. keep promising candidates
7. provisional/global route affected nets
8. run local review
9. run dependency review
10. attempt local repair if needed
11. compare metric deltas
12. accept/reject candidate
13. periodically run global coherence review
14. reopen earlier placement when routing evidence requires it
```

## 27. Router feedback como first-class output

O router deve retornar diagnósticos, não apenas sucesso/falha.

Exemplo:

```text
RouteFailure
 ├── net = N137
 ├── blockedRegion = ...
 ├── requiredWidth = 1.46mm
 ├── availableWidth = 0.91mm
 ├── blockingObjects = [U17,C42]
 ├── possibleLayerAlternative = L3
 └── suggestedRepairTargets = [U17,C42]
```

Esse feedback alimenta o search e o reasoning agent.

## 28. Objetivo da primeira implementação

A primeira versão não precisa possuir o router mais sofisticado do mercado.

Ela precisa demonstrar a propriedade arquitetural correta:

> um placement pode ser alterado em resposta ao routing, e o novo estado é reavaliado por constraints/regressão antes de ser aceito.

Se isso funcionar de forma reproduzível, o motor de routing pode evoluir sem alterar a tese central.

## 29. Possível evolução para ML especializado

Somente após acumular runs suficientes faz sentido considerar modelos que aprendam:

- quais neighborhoods normalmente produzem melhoria;
- quais moves têm alta probabilidade de sucesso;
- quais candidates tendem a routing failure;
- quais net orderings reduzem congestionamento;
- quais placements resultam em menor custo final.

Isso pode produzir:

- learned candidate ranker;
- routability predictor;
- move proposal model;
- failure classifier.

Esses modelos continuam subordinados ao evaluator determinístico.

## 30. Princípio final

O optimizer não deve tentar substituir décadas de teoria de CAD/EDA por chamadas de LLM.

A direção é:

```text
classical geometry/search/routing
+ multi-fidelity evaluation
+ transactional joint optimization
+ semantic reasoning at macro decision points
```

A IA melhora **o que explorar, como interpretar e quando reparar**; o engine local continua responsável por **como executar e validar fisicamente**.
