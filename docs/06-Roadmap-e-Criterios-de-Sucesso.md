# 06 — Roadmap técnico, experimento inicial e critérios de sucesso

## 1. Objetivo deste roadmap

Este documento define uma sequência de validação técnica, não uma promessa comercial nem uma separação artificial entre “MVP” e “produto final”.

A ordem existe para reduzir risco arquitetural e garantir que cada camada necessária ao agente autônomo seja verificável antes da seguinte.

A tese mais importante a provar é:

> um sistema consegue modificar iterativamente placement e routing como um único estado físico, detectar consequências/regressões e reorganizar decisões anteriores até alcançar um design válido e progressivamente melhor.

## 2. Primeira meta: construir o mundo antes do agente

Antes de conectar um LLM, precisamos conseguir:

- importar um circuito;
- representar board/components/pads/nets;
- definir constraints;
- medir geometria;
- criar e comparar estados;
- simular movimentos;
- fazer rollback;
- estimar routability;
- produzir routing;
- gerar métricas;
- detectar regressões.

Se o LLM for necessário para responder perguntas básicas como “quais nets foram afetadas ao mover U7?”, a base arquitetural está errada.

## 3. Estrutura inicial de módulos

Organização conceitual proposta:

```text
PlaceRouter.Core
PlaceRouter.Geometry
PlaceRouter.DesignModel
PlaceRouter.Semantics
PlaceRouter.Constraints
PlaceRouter.DesignExchange
PlaceRouter.Routing
PlaceRouter.Search
PlaceRouter.Verification
PlaceRouter.Agent
PlaceRouter.Cli
PlaceRouter.App
PlaceRouter.Tests
PlaceRouter.Benchmarks
PlaceRouter.Experiments
```

### PlaceRouter.Core

- IDs;
- units;
- coordinates;
- layer identifiers;
- shared primitives;
- common result/error types.

### PlaceRouter.Geometry

- polygons;
- transformations;
- distances;
- intersections;
- collision;
- spatial indexes;
- board/keepout geometry.

### PlaceRouter.DesignModel

- Design;
- Board;
- Stackup;
- Components;
- Footprints;
- Pads;
- Nets;
- PhysicalState;
- Regions;
- Groups;
- transactions/diffs.

### PlaceRouter.Semantics

- functional roles;
- component/pad relationships;
- semantic graph;
- inference metadata;
- semantic groups.

### PlaceRouter.Constraints

- constraint definitions;
- inheritance;
- effective rules;
- validation;
- conflict diagnostics;
- scoring of preferences/goals.

### PlaceRouter.DesignExchange

- PRDX;
- importers;
- exporters;
- canonicalization;
- loss diagnostics;
- provenance.

### PlaceRouter.Routing

- route representation;
- global routing;
- reservations/corridors;
- detailed routing;
- rip-up/reroute;
- routing diagnostics.

### PlaceRouter.Search

- candidate generation;
- LNS;
- simulated annealing;
- local search;
- candidate ranking;
- future MCTS.

### PlaceRouter.Verification

- required constraint checks;
- regression suite;
- review triggers;
- findings;
- block/global verification.

### PlaceRouter.Agent

- provider abstraction;
- tools;
- orchestration;
- semantic review;
- repair planning;
- explainability;
- memory/retrieval.

### PlaceRouter.Cli

Primeira interface executável para experimentos reproduzíveis.

### PlaceRouter.App

GUI/Constraint Workspace.

A GUI é parte central do produto, mas o core não deve depender dela.

### Tests / Benchmarks / Experiments

Precisam existir cedo. O projeto é um optimizer; sem benchmark e experiment harness, melhorias serão subjetivas.

## 4. Ordem técnica proposta

### Fase 1 — Core geométrico e unidades

Implementar:

- coordinate system;
- units;
- board polygon;
- transformations;
- footprint geometry;
- pad geometry;
- courtyard;
- layer model;
- collision/distance.

Critérios:

- testes determinísticos;
- transformações de footprint por pose corretas;
- distâncias reproduzíveis;
- nenhuma dependência de EDA.

### Fase 2 — Modelo canônico de design

Implementar:

- Component;
- Footprint;
- Pad;
- Net;
- Group;
- Region;
- Board;
- Stackup;
- PhysicalState;
- provenance/Unknown.

Criar schema PRDX inicial.

### Fase 3 — Primeiro importer real

Prioridade inicial: EasyEDA, usando a melhor combinação de netlist/source/footprint data disponível para o caso de teste.

O importer deve emitir diagnostics explícitos.

Depois adicionar KiCad/Specctra/IPC-2581 conforme necessidade e disponibilidade.

Critério:

> carregar uma placa de teste e reconstruir corretamente componentes, pads e conectividade no modelo canônico.

### Fase 4 — Constraint Engine

Implementar primeiro conjunto de Required constraints:

- board bounds;
- component overlap/courtyard;
- keepout;
- fixed component;
- allowed rotation;
- allowed side;
- component/component separation;
- component/net separation;
- net width/clearance básicos;
- manufacturing minimums.

Depois:

- groups;
- inheritance;
- preferences;
- optimization goals;
- conflict validation.

### Fase 5 — Transactions e diff

Implementar:

```text
Begin
Move/Rotate
Evaluate
Commit/Rollback
```

Registrar:

- changed objects;
- affected nets;
- metric deltas;
- constraints pass/fail;
- new regressions.

Esse é um requisito estrutural antes do search autônomo.

### Fase 6 — Fast scoring e routability proxies

Começar sem detailed router sofisticado.

Métricas:

- HPWL/pad-distance estimates;
- density;
- pin escape difficulty;
- critical relationship distances;
- basic congestion grid;
- reserved capacity.

Objetivo:

> distinguir rapidamente placements obviamente ruins de plausíveis.

### Fase 7 — Global routing / corridor reservation

Criar um global router capaz de:

- estimar corredores;
- consumir capacidade por layer;
- detectar hotspots;
- estimar vias/layer transitions;
- reportar bloqueadores.

Placement começa a receber routing feedback real.

### Fase 8 — Placement search determinístico

Adicionar movimentos:

- translate;
- rotate;
- swap;
- cluster move;
- local re-placement.

Usar inicialmente:

- LNS;
- Simulated Annealing;
- heurísticas orientadas por conectividade/semântica simples.

Critério principal:

> otimizar um board pequeno reduzindo custo sem violar Required constraints.

### Fase 9 — Detailed routing incremental

Criar primeira versão de route geometry.

Objetivos:

- rotear nets simples;
- respeitar width/clearance;
- via/layer rules;
- rip-up;
- reroute local;
- reportar motivo de failure.

Não é necessário começar com push-and-shove sofisticado.

### Fase 10 — Joint place/route repair loop

Marco arquitetural principal.

Cenário obrigatório:

```text
routing cannot complete N18
      ↓
router reports blocking geometry
      ↓
optimizer reopens placement neighborhood
      ↓
generates component move/rotation candidates
      ↓
routes again
      ↓
regression check
      ↓
accepts valid repair
```

Se isso funcionar sem IA, a fundação está correta.

### Fase 11 — Regression Engine

Formalizar:

- baseline;
- resolved findings;
- new findings;
- degraded metrics;
- unchanged constraints;
- event-driven suites.

Adicionar L0/L1/L2 determinísticos.

### Fase 12 — Constraint Workspace GUI

Construir interface para:

- importar;
- navegar components/nets;
- busca/filtros;
- grouping;
- bulk editing;
- properties;
- board definition;
- stackup;
- manufacturing profile;
- regions;
- constraint authoring;
- conflicts;
- readiness report.

A GUI não deve criar lógica paralela; tudo chama o mesmo core.

### Fase 13 — Semantics

Adicionar:

- component roles;
- pad-level roles;
- semantic relationships;
- functional groups;
- decoupling relationship;
- switching nodes;
- clocks;
- high-impedance nets;
- differential pairs;
- power loops.

Inicialmente parte pode ser informada manualmente.

### Fase 14 — Agent tool layer

Expor tools estruturadas de:

- inspection;
- analysis;
- candidate simulation;
- local optimization;
- routing;
- reviews;
- transaction control.

Criar test harness sem depender da GUI.

### Fase 15 — Primeiro reasoning LLM

Adicionar provider abstraction e um modelo generalista forte.

Primeiras tarefas:

- explicar design context;
- sugerir constraints;
- identificar subproblema;
- escolher neighborhood;
- solicitar optimization;
- interpretar failures;
- sugerir repair.

Não dar ao LLM autoridade de DRC.

### Fase 16 — Reviews L3–L7

Adicionar progressivamente:

- repair review;
- global coherence;
- functional block review;
- whole-board review;
- adversarial review.

Comparar findings do agente com ground truth/engenheiro.

### Fase 17 — Design Memory e Case Retrieval

Persistir:

- successful repairs;
- failed strategies;
- topology signatures;
- metric deltas;
- verified outcomes.

Adicionar retrieval sem fine-tuning.

### Fase 18 — Search avançado

Somente depois de termos evaluator barato e confiável:

- MCTS;
- beam search mais amplo;
- multi-candidate search;
- learned candidate ranking;
- ML routability prediction.

## 5. Experimento inicial

A primeira prova séria deve usar várias placas, não apenas um caso feito para favorecer o algoritmo.

Escopo alvo inicial discutido:

- aproximadamente 30–50 componentes;
- 2 copper layers;
- outline conhecido;
- schematic/netlist já definidos;
- footprints resolvidos;
- circuitos relativamente convencionais;
- mechanical constraints simples;
- sem exigir RF/DDR/high-speed extremo na primeira validação.

Categorias úteis de circuitos de teste:

- MCU + decoupling + connectors;
- ADC/analog frontend simples;
- buck converter;
- USB simples;
- SPI peripherals;
- op-amp stage;
- sensor board.

O conjunto deve conter dependências reais e não apenas nets aleatórias.

## 6. Critérios de sucesso da prova inicial

### Importação

- design entra sem perda estrutural crítica;
- footprints e pads são resolvidos;
- net connectivity é preservada.

### Constraint authoring

- usuário consegue definir grupos e relações visualmente;
- Required/Preferred/Goals são distintos;
- conflicts óbvios são detectados antes da run.

### Placement

- componentes móveis são reorganizados;
- fixed components são preservados;
- orientation/pad accessibility entra no score.

### Routing-awareness

- placement considera corridors/congestion antes do routing final;
- uma posição pode ser rejeitada por impacto futuro de routing.

### Routing

- nets suportadas são roteadas com hard constraints válidas;
- failures possuem diagnostics úteis.

### Co-optimization

- routing consegue provocar mudança de placement;
- placement change provoca rerouting localizado;
- o loop converge ou termina com diagnóstico/budget claro.

### Regression

- uma alteração que quebra uma regra anteriormente satisfeita é detectada;
- repair pode ser rejeitado/rollbackado.

### Validity

- zero violações das hard constraints suportadas no candidate aceito.

### Explainability

- mudanças relevantes possuem reason e metric delta;
- o usuário consegue saber por que algo mudou.

### Reproducibility

- seed/config/model version permitem reproduzir runs dentro das limitações dos componentes não determinísticos.

## 7. Critério qualitativo indispensável

DRC zero não é suficiente.

A placa precisa também ser considerada **eletricamente sensata** por revisão humana.

A primeira validação deve comparar:

- regras conhecidas do circuito;
- layout gerado;
- findings do sistema;
- avaliação de um engenheiro.

O objetivo é evitar um sistema que descubra apenas maneiras geometricamente legais de produzir layouts ruins.

## 8. Cenário mínimo que prova a tese

Um teste particularmente importante:

```text
1. optimizer places U7/C17/C18
2. routing later finds N18 trapped
3. router identifies U7/C17 as blockers
4. optimizer tests multiple repairs
5. repair A routes N18 but breaks decoupling
6. regression rejects A
7. repair B rotates/moves another component
8. local rerouting succeeds
9. no required regression remains
10. B is committed
```

Se esse comportamento for consistente, já existe evidência concreta de joint physical design, mesmo que os algoritmos individuais ainda sejam simples.

## 9. Benchmarking desde cedo

Cada test design deve registrar baseline e candidates.

Métricas candidatas:

```text
hard constraint violations
route completion rate
total trace length
weighted critical trace length
via count
congestion hotspots
max congestion
reserved capacity
critical loop area
sensitive/aggressor proximity
number of repairs
number of rollbacks
optimizer iterations
compute time
LLM calls/cost when applicable
human review findings
```

Não existe uma única métrica suficiente.

## 10. Baselines

Comparações úteis:

- imported/manual initial placement;
- simple greedy placement;
- optimizer without routing awareness;
- optimizer with provisional routing;
- optimizer with joint repair;
- optimizer + LLM agent.

Isso permite medir exatamente qual camada agrega valor.

## 11. Evolução funcional do produto

A discussão inicial identificou uma progressão possível de capacidade:

1. **PCB Review** — analisar board humana e abrir findings;
2. **Placement Optimizer** — melhorar placement existente;
3. **Global Routing Planner** — reservar corredores/layers e estimar congestionamento;
4. **Detailed Auto-Router** — produzir tracks/vias;
5. **Autonomous Physical Design** — ciclos completos de placement/routing/review/repair.

Esses níveis são capacidades acumulativas, não produtos necessariamente separados.

Uma observação importante: PCB Review pode gerar valor cedo e também ajudar a validar a camada semântica antes de toda automação estar pronta.

## 12. AI PCB Review como benchmark intermediário

Antes de confiar no agente para modificar designs, podemos pedir que ele revise placas existentes.

Findings esperados podem incluir:

```text
bypass capacitor too far from VDD pad
feedback route exposed to switching region
crystal loop unnecessarily large
ADC reference crossing digital clock region
gate resistor too far from MOSFET gate
routing corridor likely to trap remaining nets
```

O finding precisa trazer evidence mensurável sempre que possível.

## 13. Dados e treinamento futuro

A primeira geração não precisa de dataset próprio.

Com o tempo, cada run produz dados:

```text
input state
constraints
candidate action
measured metrics
routing result
regressions
repair result
final acceptance
```

Quando houver volume suficiente, isso pode treinar modelos especializados para:

- move proposal;
- candidate ranking;
- routing-order selection;
- congestion prediction;
- placement refinement;
- failure prediction.

Esse aprendizado é otimização futura, não requisito de partida.

## 14. Uso de projetos reais para teste

É desejável testar com placas próprias ou projetos cuja licença permita uso.

Se no futuro designs externos forem usados para treinamento, provenance/licenciamento precisam ser tratados explicitamente.

A arquitetura de casos internos deve registrar origem e autorização de uso.

## 15. Riscos técnicos principais

### 15.1 Search space explosivo

Mitigações:

- hierarchical regions;
- action filtering;
- neighborhoods;
- coarse-to-fine evaluation;
- locks/freeze;
- global routing proxies.

### 15.2 Evaluator barato pouco correlacionado com routing real

Mitigações:

- benchmark fast metrics contra detailed route outcomes;
- melhorar proxy progressivamente;
- usar expensive evaluation nos finalistas.

### 15.3 LLM gera reasoning plausível mas inútil

Mitigações:

- tools;
- structured state;
- measurable outcomes;
- benchmarks por task;
- deterministic authority;
- adversarial review.

### 15.4 Constraint explosion

Mitigações:

- groups;
- inheritance;
- templates/profiles;
- bulk editing;
- AI suggestions;
- provenance.

### 15.5 Interoperabilidade insuficiente

Mitigações:

- canonical model;
- loss diagnostics;
- adapter architecture;
- começar com um EDA e ampliar.

### 15.6 Performance

Mitigações:

- incremental recomputation;
- spatial indexing;
- candidate deltas;
- multi-fidelity evaluation;
- parallel candidate evaluation futuramente.

## 16. Decisões que não precisam ser fechadas agora

Podem permanecer experimentais:

- linguagem principal do engine;
- framework final da GUI;
- algoritmo exato do detailed router;
- grid resolution do global router;
- LLM provider/model;
- MCTS ou outra busca de longo horizonte;
- solver de SI/PI/thermal;
- nome definitivo PRDX;
- estratégia comercial/local-first/open-core.

Essas decisões não devem bloquear o domínio e os contracts centrais.

## 17. Próxima etapa de documentação antes de código pesado

Depois deste plano conceitual, os documentos mais úteis serão:

1. `Architecture Decision Records` para escolhas técnicas concretas;
2. especificação do schema PRDX v0.1;
3. modelo formal de `Constraint` e inheritance;
4. contract de `PhysicalDesignState` e `DesignTransaction`;
5. especificação do primeiro importer EasyEDA;
6. geometry coordinate/unit conventions;
7. benchmark/test-board specification;
8. tool contracts do Agent.

Esses documentos devem nascer conforme as decisões forem sendo validadas, evitando cristalizar prematuramente hipóteses experimentais.

## 18. Definição de sucesso da primeira grande validação

O primeiro resultado realmente convincente não é “a IA colocou os componentes”.

É conseguir observar repetidamente o ciclo:

```text
understand state
      ↓
plan
      ↓
place / reserve / route
      ↓
measure
      ↓
find regression or routing problem
      ↓
reopen previous decision
      ↓
repair
      ↓
verify
      ↓
commit
```

em várias placas pequenas diferentes, chegando a estados:

- geometricamente válidos;
- roteáveis;
- DRC-clean dentro das regras implementadas;
- coerentes com constraints elétricas informadas;
- explicáveis;
- e considerados sensatos por revisão humana.

Essa é a evidência necessária para justificar a evolução do WTK.Place&Router para um sistema de physical design autônomo mais amplo.
