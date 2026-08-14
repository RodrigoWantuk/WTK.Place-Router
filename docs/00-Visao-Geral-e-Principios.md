# 00 — Visão geral, escopo e princípios

## 1. Objetivo do projeto

WTK.Place&Router é uma ferramenta de **PCB physical design** cujo objetivo é automatizar e otimizar, de forma conjunta, iterativa e verificável:

- component placement;
- routing;
- reserva e consumo de recursos de routing;
- organização espacial de blocos funcionais;
- regras de proximidade, separação e isolamento;
- constraints elétricas e de fabricação;
- coerência global da placa;
- ciclos de revisão, regressão e reparo.

A tese central do projeto é que **placement e routing não devem ser tratados como duas decisões independentes em cascata**. Uma posição aparentemente ótima para um componente pode inviabilizar rotas futuras, aumentar congestionamento, piorar retorno de corrente, criar acoplamento indesejado ou obrigar o uso de vias e desvios. Da mesma forma, dificuldades encontradas durante routing podem justificar mover, rotacionar ou reorganizar componentes que já haviam sido considerados posicionados.

Portanto, o objeto de otimização é o **estado físico completo da PCB**, representado pelo `PhysicalDesignState`.

## 2. O que o produto não pretende fazer inicialmente

A primeira fase do produto **não é uma ferramenta de schematic capture nem de criação do design eletrônico**.

O usuário continua usando um EDA externo para:

- criar o esquemático;
- selecionar os componentes;
- definir valores e part numbers;
- associar footprints;
- criar a conectividade elétrica;
- revisar o circuito eletrônico.

Exemplos de ferramentas de origem:

- EasyEDA;
- KiCad;
- Altium Designer;
- OrCAD / Allegro;
- outras ferramentas que forneçam dados suficientes por netlist, formato de intercâmbio ou adapter nativo.

O Place&Router começa **depois que o circuito eletrônico já existe**.

## 3. Fluxo conceitual do produto

```text
EDA externo
    │
    │ schematic/netlist/design export
    ▼
Design Exchange Layer
    │
    ▼
Modelo canônico Place&Router
    │
    ▼
Automatic deterministic enrichment
    │
    ▼
Constraint Workspace
    │
    ├── propriedades de componentes
    ├── propriedades de nets
    ├── grupos
    ├── regiões
    ├── board/stackup
    ├── manufacturing profile
    └── regras/objetivos
    │
    ▼
Readiness Validation
    │
    ▼
Physical Design Optimizer
    │
    ├── placement
    │      ↕
    ├── routing
    │      ↕
    ├── review
    │      ↕
    └── repair/regression
    │
    ▼
Candidate PCB(s)
    │
    ▼
Export de volta ao EDA / formato de intercâmbio
```

A UX deve obedecer a regra:

> **importar, derivar e inferir antes de perguntar ao usuário**.

Campos desconhecidos continuam válidos. O usuário só deve ser interrompido quando uma informação ausente for materialmente necessária para a decisão em andamento.

## 4. Por que placement e routing são um único problema

A posição e a orientação de um componente influenciam simultaneamente:

1. o routing que será possível em seguida;
2. quais componentes relacionados conseguem permanecer próximos;
3. quais componentes, nets ou regiões precisam permanecer afastados;
4. a acessibilidade individual dos pads;
5. a orientação de grupos de pads de um mesmo encapsulamento;
6. as lanes/corridors que já foram reservadas;
7. congestionamento em cada layer;
8. número e localização provável de vias;
9. área de loops críticos;
10. retorno de corrente;
11. exposição de nets sensíveis a aggressors;
12. dissipação e distribuição térmica;
13. montagem e restrições mecânicas.

Exemplo: colocar um regulador perto do indutor pode reduzir comprimento de uma net importante, mas uma determinada rotação pode deixar o pino de feedback voltado para o switching node, bloquear a saída de outros pads ou ocupar um corredor necessário a uma interface crítica.

Por isso, o sistema não deve possuir um pipeline conceitual rígido:

```text
placement completo → routing completo → fim
```

O modelo correto é:

```text
partial placement
      ↓
provisional/global routing
      ↓
routability/congestion feedback
      ↓
component or group moves
      ↓
local rip-up / reroute
      ↓
review / regression
      ↓
accept, repair or rollback
      ↓
next iteration
```

A separação interna de algoritmos permanece útil por custo computacional, mas representa **níveis de fidelidade e responsabilidades**, não independência de decisões.

## 5. Representação física: 2.5D híbrida

A PCB não deve ser tratada como uma imagem nem como um volume voxelizado 3D uniforme.

A fonte de verdade deve combinar:

- geometria contínua de board outline, pads, courtyards, vias, keepouts e copper;
- layers discretas no eixo Z;
- stackup;
- grafos elétricos e físicos;
- mapas derivados de ocupação e congestionamento em resoluções apropriadas.

Conceitualmente:

```text
Board geometry
 ├── outline
 ├── holes
 ├── keepouts
 ├── regions
 ├── stackup
 ├── copper layers
 └── components
      ├── x/y
      ├── rotation
      ├── side
      ├── body/courtyard
      ├── height
      └── pads
```

Occupancy maps rasterizados podem ser usados como **view computacional**, mas não como representação canônica.

A direção v0.1 usa coordenadas físicas inteiras e um geometry kernel determinístico; detalhes estão no documento `10`.

## 6. O papel da IA

A primeira arquitetura não depende de treinamento de um modelo próprio.

O sistema usa um **LLM generalista com reasoning/structured output** como agente de engenharia e coordenação em macro-decisões.

O LLM é responsável por tarefas como:

- compreender blocos funcionais;
- interpretar relações entre componentes, pads e nets;
- identificar subproblemas relevantes;
- propor estratégias de placement/routing;
- escolher o que deve ser reotimizado;
- diagnosticar falhas;
- sugerir constraints;
- revisar coerência semântica;
- interpretar métricas;
- propor classes de reparo;
- explicar decisões.

O LLM **não deve** ser responsável por:

- armazenar todo o PhysicalDesignState no prompt;
- calcular geometria exata como fonte de verdade;
- declarar DRC válido;
- decidir se uma distância física realmente atende uma regra;
- desenhar uma PCB como imagem;
- emitir milhares de coordenadas sem avaliação externa;
- substituir A*/routing/search/DRC local conhecido.

A placa é a memória do sistema. O agente consulta exatamente o contexto relevante através de operações estruturadas.

## 7. Três cérebros, não um

A arquitetura conceitual possui três classes de inteligência:

### 7.1 Reasoning Agent

LLM generalista com structured input/output.

Responsável por estratégia, decomposição, diagnóstico, semântica, revisão e explicação.

DeepSeek é o provider inicial, sem acoplamento arquitetural ao fornecedor.

### 7.2 Search / Optimization Engine

Inicialmente totalmente local e não neural.

Direção v0.1:

- Large Neighborhood Search (LNS);
- Simulated Annealing;
- heurísticas locais;
- candidate funnel multi-fidelity;
- futuramente beam/MCTS/learned heuristics quando benchmarks justificarem.

Responsável por explorar muitas combinações geométricas que não fazem sentido serem decididas uma a uma pelo LLM.

### 7.3 Deterministic Rules / Geometry / Physics Engine

Fonte de verdade do produto.

Responsável por:

- geometria;
- collision/courtyard;
- DRC;
- connectivity;
- clearances;
- width/via rules;
- stackup constraints;
- length/skew;
- manufacturability;
- routability e congestionamento;
- global/detailed routing;
- métricas elétricas determinísticas ou aproximadas;
- validação de hard constraints.

A direção dos algoritmos locais está detalhada em `10-Processamento-Local-e-Algoritmos-Deterministicos.md`.

## 8. Princípio de autoridade

A IA **nunca tem autoridade para declarar uma placa válida**.

Ela pode dizer:

> “Esse posicionamento parece reduzir a exposição do feedback ao switching node.”

Mas somente o engine pode declarar fatos como:

```text
measured separation = 5.84 mm
required separation = 6.00 mm
result = FAIL
```

ou:

```text
differential skew = 0.83 mm
maximum allowed = 0.50 mm
result = FAIL
```

Esse princípio deve permanecer verdadeiro mesmo se no futuro forem adicionados modelos de ML especializados.

## 9. Hard constraints, preferences e goals

Regras físicas obrigatórias não devem existir apenas como penalidades de score.

Exemplo incorreto:

```text
-1000 points for component overlap
```

Um optimizer poderia compensar matematicamente essa penalidade com outros ganhos.

Exemplo correto:

```text
component overlap → candidate invalid
outside board      → candidate invalid
forbidden layer    → candidate invalid
manufacturing rule → candidate invalid
```

Trade-offs ficam para propriedades que realmente aceitam compromisso.

A taxonomia de produto deve distinguir:

- **Required** — obrigatório, hard constraint;
- **Preferred** — desejável, pode ser sacrificado de forma explícita;
- **Optimization Goal** — objetivo contínuo de score.

## 10. Circuit semantics

Uma netlist simples informa conectividade, mas o sistema precisa evoluir para compreender significado.

Não basta:

```text
U1.24 ↔ C17.1
```

É desejável representar:

```text
C17
 role = decoupling capacitor
 decouples = U1.VDD
```

ou:

```text
U7.SW
 role = switching node
 high_dv_dt = true
 aggressor = high
```

ou:

```text
USB_PAIR
 P = USB_D+
 N = USB_D-
 type = differential pair
 target impedance = ...
 max skew = ...
```

A ontologia deve permitir objetos/semânticas como:

- DifferentialPair;
- PowerRail;
- Clock;
- SwitchingNode;
- HighImpedanceNode;
- KelvinSense;
- GuardRing;
- MemoryBus;
- AnalogIsland;
- PowerLoop;
- FeedbackNetwork;
- DecouplingRelationship.

Antes da IA, heurísticas locais podem gerar **candidates** a partir de nomes/topologia. Inferências incertas mantêm provenance/confidence e não viram hard facts silenciosamente.

## 11. Pads importam tanto quanto componentes

O sistema não pode reduzir o problema a um grafo em que cada componente é apenas um ponto.

Em muitos casos a posição relativa dos pads determina a orientação ideal.

Exemplo de um regulador:

```text
VIN  → CIN deve ficar muito próximo
SW   → indutor deve ficar próximo
BOOT → capacitor deve relacionar BOOT/SW
FB   → feedback deve ficar próximo e ao mesmo tempo afastado de SW
```

Portanto, os grafos devem permitir relações em pelo menos dois níveis:

- component-level;
- pin/pad-level.

## 12. Objetos físicos de alta importância

Algumas estruturas devem poder ser tratadas praticamente como objetos compostos, e não como componentes independentes seguidos de routing tardio.

Exemplos:

- loop de entrada de buck;
- switching loop;
- crystal loop;
- op-amp feedback loop;
- current shunt com Kelvin sensing;
- ADC reference network;
- differential pair;
- gate-drive loop.

O optimizer pode modificar simultaneamente posição, rotação, vias e topologia/caminho de cobre desse conjunto.

## 13. Explicabilidade obrigatória

Uma ferramenta EDA com IA precisa explicar alterações relevantes.

Um candidato deve poder produzir relatórios como:

```text
Moved U7
Reason:
- reduced feedback-loop length
- increased distance from SW aggressor
- released routing corridor C12

Metrics relative to previous state:
- estimated via count: -2
- critical congestion: -18%
- feedback exposure: improved
```

Explicabilidade não é apenas UI. Ela é importante para:

- confiança do engenheiro;
- auditoria;
- debugging;
- regressão;
- criação futura de uma base de casos reutilizáveis.

## 14. Contexto de mercado e posicionamento conceitual

A ideia não surge em um vazio. Ferramentas e pesquisas modernas de EDA já exploram automação, optimization, IA, reinforcement learning, placement e routing.

O objetivo do projeto não é provar que automação de PCB é possível. A diferenciação conceitual pretendida é combinar:

- interoperabilidade com EDAs existentes;
- constraint authoring explícito e visual;
- estado externo determinístico;
- joint placement/routing optimization;
- uso de teoria clássica/local antes de cloud reasoning;
- review e regressão contínuos;
- explainability;
- possibilidade de operação local/offline parcial ou total conforme provider/configuração;
- memória de engenharia construída com resultados próprios.

Esse posicionamento ainda é uma hipótese de produto e não uma decisão comercial final.

## 15. Princípios invariantes atuais

1. O circuito eletrônico é criado fora do Place&Router na primeira fase.
2. Placement e routing são codependentes.
3. A fonte de verdade física é determinística e local.
4. Hard constraints não são meras penalidades.
5. O PhysicalDesignState não depende da memória do LLM.
6. A IA age através de AgentOperations/contracts estruturados.
7. O optimizer local explora coordenadas; o LLM não precisa escolher números exatos.
8. Routing pode solicitar mudanças de placement.
9. Placement deve considerar routing futuro desde cedo.
10. Mudanças relevantes devem ser revisáveis, comparáveis e reversíveis.
11. Regressões precisam ser detectadas automaticamente.
12. A interface deve permitir configurar constraints complexas visualmente.
13. Informações desconhecidas são permitidas e devem ser explicitamente marcadas como desconhecidas.
14. Sugestões da IA devem ser distinguíveis de regras definidas pelo usuário.
15. O sistema deve importar/derivar/inferir antes de pedir input manual.
16. Algoritmos clássicos/bibliotecas maduras são preferidos a soluções custom ou LLM para problemas já bem definidos.
17. O primeiro marco técnico é provar o ciclo autônomo completo em casos controlados.
