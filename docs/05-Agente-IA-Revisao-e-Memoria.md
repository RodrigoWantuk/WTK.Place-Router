# 05 — Agente de IA, revisão, memória e explainability

## 1. Objetivo

O agente de IA deve funcionar como **engenheiro de layout e coordenador do processo**, não como motor geométrico de baixo nível.

Ele precisa compreender contexto eletrônico, selecionar o subproblema relevante, consultar tools estruturadas, propor estratégias, interpretar resultados e orientar ciclos de revisão/reparo.

O estado real da PCB permanece fora do prompt.

## 2. BoardState como memória externa

O agente não precisa “manter toda a PCB na cabeça”.

Ele consulta o estado atual conforme a necessidade:

```text
inspect_component(U7)
find_related_components(U7)
get_effective_constraints(U7)
get_candidate_regions(U7)
get_routing_impact(U7)
get_findings(scope=POWER_BUCK)
simulate_placement(...)
```

A placa é a memória persistente.

Isso evita:

- prompts enormes;
- estado obsoleto;
- perda de relações após alterações;
- geometria representada de forma textual imprecisa;
- custo desnecessário de repetir a placa inteira a cada chamada.

## 3. Context view orientada ao problema

Ao selecionar U7, por exemplo, o tool layer pode retornar apenas contexto relevante:

```text
U7 = switching regulator

Relevant relationships:
VIN:
  C14 should be very close
  high-current path

SW:
  connected to L2/D3
  high dv/dt
  keep away from FB/analog

FB:
  feedback network R17/R18
  quiet-region preference

BOOT:
  C15 relates BOOT/SW

Affected routing resources:
  POWER_03
  SPI_07
```

O agente pode então pedir simulações específicas.

## 4. Tool calling como contrato

O agente não altera structures diretamente.

Toda alteração deve passar por tools com schema claro.

Famílias de tools candidatas:

### Inspection

```text
inspect_component
inspect_net
inspect_group
inspect_region
inspect_constraint
find_related_components
find_related_nets
find_nearby_objects
```

### Analysis

```text
get_effective_constraints
get_routing_impact
get_congestion
get_findings
get_metric_breakdown
analyze_dependency_impact
compare_candidates
```

### Candidate generation

```text
simulate_placement
simulate_group_move
simulate_rotation
request_local_optimization
request_reroute
request_corridor_replan
```

### Transaction control

```text
begin_transaction
apply_candidate
commit_transaction
rollback_transaction
```

### Review

```text
run_local_review
run_dependency_review
run_block_review
run_global_review
open_finding
resolve_finding
```

O schema real deve ser pequeno, tipado e determinístico.

## 5. LLM não escolhe coordenadas finas por padrão

Exemplo ruim:

> “Coloque U17 em X=42.183947, Y=18.72519.”

Exemplo melhor:

> “Reotimize o cluster U17/C41/C42/L3/D7 na região POWER_TOP_RIGHT, mantendo o feedback afastado do switching corridor e minimizando o input loop.”

O optimizer numérico explora centenas/milhares de combinações e retorna as melhores.

## 6. Semantic reasoning

O agente deve ser particularmente útil para combinar regras eletrônicas.

Exemplo:

```text
C17 must be close to U3.VDD
but C17 cannot block the USB corridor
and U3 must remain far from U8
and rotating U3 changes FB pad accessibility
```

A função do LLM é perceber conflitos, dependências e hipóteses de solução, não medir a distância final.

## 7. Uso de conhecimento prévio do LLM

Um foundation model já possui conhecimento amplo sobre:

- decoupling;
- switching regulators;
- ADCs;
- clocks/crystals;
- differential pairs;
- op-amp feedback;
- current shunts/Kelvin;
- power distribution;
- EMI/EMC heuristics;
- datasheets/application notes;
- práticas de PCB.

O projeto pode aproveitar esse prior sem assumir que o modelo viu uma PCB específica durante treinamento.

A tese é:

> o LLM possui conhecimento geral de engenharia; o Place&Router fornece estado físico explícito, constraints, documentos e verificadores para que esse conhecimento possa ser aplicado de maneira controlada.

## 8. Datasheets e application notes

Quando necessário, o agente pode ser enriquecido com fontes específicas do projeto:

- datasheets;
- application notes;
- layout guidelines do fabricante;
- reference designs;
- user-defined notes.

Regras derivadas dessas fontes devem registrar provenance.

## 9. Sugestão de constraints antes da otimização

O agente também trabalha no Constraint Workspace.

Exemplo:

```text
Suggest constraints for U7
```

Ele pode sugerir:

- decoupling proximity;
- feedback isolation;
- switching-node reduction;
- inductor proximity;
- quiet return;
- thermal considerations.

O usuário aceita, modifica ou rejeita.

Sugestão de IA não deve ser indistinguível de regra humana.

## 10. Review como parte formal do algoritmo

Cada mudança pode provocar revisão.

Fluxo:

```text
Current State
    ↓
Proposed transaction
    ↓
Apply in candidate state
    ↓
Incremental rerouting
    ↓
Local verification
    ↓
Dependency review
    ↓
Regression check
    ↓
Global impact estimate
    ↓
accept / repair / rollback
```

## 11. Níveis de review

### L0 — Precondition

Antes de aplicar ação.

Determinístico.

Verifica:

- board boundary;
- keepouts;
- side;
- rotation;
- locked state;
- mechanical region;
- impossibilidades óbvias.

Falhou: ação não acontece.

### L1 — Local Review

Depois da alteração.

Analisa:

- collisions;
- courtyard;
- pad accessibility;
- local routing;
- local clearance;
- vias;
- local congestion;
- decoupling distances;
- hard constraints diretamente afetadas.

Executado praticamente a cada ação.

### L2 — Dependency Review

Analisa efeitos de segunda ordem.

Exemplo:

```text
C17 move = locally legal
but rerouting N17 causes ADC_REF separation violation
```

O sistema percorre relações e recursos dependentes.

### L3 — Repair Review

Tenta resolver finding sem desfazer tudo.

```text
1. reroute N17 on another layer
2. move C17 1.4 mm
3. rotate U3
4. reopen whole analog block
```

Escolhe a alternativa com menor disruption compatível com constraints.

### L4 — Global Coherence Review

Executado periodicamente.

Perguntas:

- corredores suficientes continuam existindo?
- algum componente ficou encurralado?
- grupos funcionais se dispersaram?
- alguma net ainda não roteada ficou quase impossível?
- existem hotspots de congestionamento?
- layer plan ainda é coerente?
- via count está crescendo sem necessidade?
- quiet regions continuam íntegras?
- current loops se degradaram?

Aqui o LLM pode ser particularmente útil interpretando diagnósticos estruturados.

### L5 — Functional Block Review

Executado ao estabilizar um bloco.

Exemplo:

```text
BUCK BLOCK REVIEW
Input loop             PASS
Switching loop         PASS
Bootstrap              PASS
Feedback isolation     WARNING
Thermal copper         PASS
Power return           PASS
```

O bloco não se torna eternamente imutável; pode ser reaberto posteriormente.

### L6 — Whole-board Place/Route Review

Quando a placa está quase roteada, o routing revisa o placement.

Perguntas:

```text
Why does N173 need four vias?
Why does SPI_CLK cross half the board?
Why are 11 routes squeezed between U31/U32?
Why does ADC_REF make a large detour?
```

A conclusão pode ser mover componentes considerados estáveis.

### L7 — Independent / Adversarial Review

Um contexto/agente independente recebe o candidate sem histórico decisório e tenta encontrar problemas.

Papel:

> não assumir que decisões anteriores estão corretas.

Categorias:

- electrical coupling;
- routing traps;
- return paths;
- thermal problems;
- questionable placement;
- unnecessary vias;
- high-current loops;
- high-impedance exposure;
- manufacturing risks.

Esse agente abre findings, mas não declara validade física.

## 12. Revisão orientada a eventos

Nem toda regra deve ser reevaluada integralmente a cada ação.

Eventos podem disparar suítes específicas.

Exemplo:

```text
EVENT: switching node routed
TRIGGER:
  EMI review
  analog proximity review
  return path review
```

```text
EVENT: differential pair routed
TRIGGER:
  pair skew
  reference plane
  via symmetry
  return vias
  crosstalk proximity
```

```text
EVENT: ADC moved
TRIGGER:
  analog region
  reference path
  decoupling
  clock distance
  ground return
```

Isso reduz custo de avaliação.

## 13. Regression suite

Uma alteração que resolve um problema pode quebrar uma regra já satisfeita.

Exemplo:

```text
State 312 → 313

Resolved:
✓ ROUTING_CONGESTION_17

Regression:
✗ ANALOG_ISOLATION_04

Unchanged:
✓ 1,372 constraints

Decision:
REJECT
```

A regression suite deve ser parte estrutural do optimizer.

## 14. Baselines de constraints

O sistema deve saber quais constraints estavam:

- passing;
- failing;
- unknown;

antes da transação.

Isso permite distinguir:

- novo problema;
- problema preexistente;
- problema resolvido;
- problema piorado;
- melhoria sem mudança de pass/fail.

## 15. Findings e lifecycle

Findings podem passar por estados:

```text
OPEN
ACKNOWLEDGED
UNDER_REPAIR
RESOLVED
ACCEPTED_RISK
INVALIDATED
```

`ACCEPTED_RISK` deve exigir ação explícita do usuário para issues relevantes; a IA não deve silenciosamente aceitar risco de hard constraint.

## 16. Blackboard / Design Memory

O projeto pode manter uma memória de decisões globais estruturadas.

Exemplo:

```text
POWER_03
Switching area assigned to top-right.

USB_02
Reserved east L1 corridor for USB differential pair.

ADC_04
Keep REFIN network south of ADC.
```

Essa memória não substitui constraints. Ela registra planejamento, hipóteses e decisões relevantes.

## 17. Memória de casos de engenharia

Após acumular uso, o sistema pode armazenar casos verificados.

Exemplo:

```text
CASE BUCK-0172

Topology:
synchronous buck

Problem:
FB routing trapped after inductor placement

Failed strategy:
inductor toward connector

Successful repair:
rotate controller 90°
move feedback divider
reserve quiet FB corridor

Outcome:
loop area -31%
routing length -14%
```

Ao encontrar circuito semelhante, o agente pode recuperar casos relevantes.

Isso permite aprendizado sem alterar pesos do foundation model.

## 18. Casos de sucesso e de falha

Não devemos armazenar apenas soluções finais.

Falhas são particularmente úteis:

```text
state signature
attempted strategy
why it failed
which constraints regressed
repair that succeeded
```

Essa base pode se tornar um ativo técnico importante do produto.

## 19. Retrieval

O agente pode consultar:

```text
retrieve_similar_cases(
  topology="buck",
  layerCount=2,
  package="QFN",
  problem="feedback corridor"
)
```

O retrieval deve retornar poucos casos de alta relevância, não inundar o prompt.

## 20. Explainability de cada decisão

Transações devem carregar reason/evidence.

Exemplo:

```text
Transaction #1842

Actions:
- MOVE U7
- ROTATE C18
- RIP N37
- ROUTE N37

Reason:
N37 was blocked by U7/C18 geometry.
Rotation releases local corridor without degrading feedback loop.

Fixed:
✓ ROUTING_CONGESTION_018
✓ NET_UNROUTABLE_N37

Regressions:
none

Metrics:
wire length           -4.2 mm
estimated vias        -1
critical congestion   -13%
decoupling loop       unchanged

Decision:
ACCEPT
```

## 21. Model/provider abstraction

O projeto não deve ser acoplado a um único fornecedor de LLM.

A abstração precisa considerar:

- model identifier;
- provider;
- structured output;
- tool calling;
- reasoning quality;
- context capacity;
- cost;
- latency;
- privacy/local options futuramente.

Runs devem registrar exatamente qual modelo foi usado.

## 22. Falha ou indisponibilidade do LLM

O core determinístico deve continuar funcional sem IA.

Sem LLM ainda deve ser possível:

- importar design;
- editar constraints;
- validar hard rules;
- executar heurísticas/optimizer determinístico;
- rotear;
- gerar reports.

A IA aumenta inteligência estratégica e semântica; não pode ser dependência para ler ou preservar uma PCB.

## 23. Segurança operacional do agente

O agente deve operar em candidate states e nunca receber uma tool irrestrita do tipo:

```text
overwrite_board(arbitrary_data)
```

Tools precisam ter escopo, validação e autorização interna.

Mudanças estruturais devem sempre produzir transaction diff.

## 24. Loops e limites

O agente precisa de budgets para evitar exploração infinita:

- maximum iterations;
- tool-call budget;
- time/compute budget;
- candidate budget;
- no-improvement threshold;
- maximum repair depth;
- escalation policy.

Ao atingir budget, ele retorna o melhor candidate atual e findings abertos.

## 25. Critério de sucesso do agente

Não basta produzir texto de engenharia plausível.

O agente só agrega valor quando consegue, através de tools:

1. identificar um problema real no estado;
2. selecionar um neighborhood relevante;
3. propor ou solicitar mudanças válidas;
4. melhorar métricas ou resolver findings;
5. não introduzir regressões não detectadas;
6. explicar o que ocorreu.

Esse comportamento precisa ser benchmarkado.
