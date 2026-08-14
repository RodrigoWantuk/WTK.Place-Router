# 08 — Protocolo de Iterações com IA

## 1. Objetivo

Este documento define **como o WTK.Place&Router deve interagir com modelos de IA durante o physical design**.

Ele complementa [`05-Agente-IA-Revisao-e-Memoria.md`](05-Agente-IA-Revisao-e-Memoria.md):

- o documento `05` define o papel conceitual do agente, tools, níveis de review, memória e explainability;
- este documento define o **protocolo operacional de cada chamada**, contratos JSON, limites de responsabilidade, cadence das chamadas, validação de respostas, retries, logging e separação entre IA e algoritmos determinísticos;
- [`10-Processamento-Local-e-Algoritmos-Deterministicos.md`](10-Processamento-Local-e-Algoritmos-Deterministicos.md) define **quais algoritmos locais executam placement/routing/geometry/search e em que momento**, para que AgentOperations peçam capacidades reais em vez de compensarem lacunas do engine.

A regra central é:

> cada interação com IA é uma operação tipada, versionada, limitada, auditável e validada; nunca uma conversa livre que altera a PCB diretamente.

---

## 2. Princípio: a IA não vive no inner loop numérico

O Place&Router terá dois ritmos de iteração muito diferentes.

### 2.1 Macro-iteration — reasoning

Executada pela IA em pontos de decisão relevantes.

Exemplos:

- identificar qual bloco deve ser trabalhado agora;
- diagnosticar por que uma região não está roteando;
- escolher um neighborhood para reotimização;
- propor uma estratégia de repair;
- revisar coerência elétrica de um bloco;
- sugerir constraints;
- interpretar métricas conflitantes;
- realizar review global/adversarial.

Uma macro-iteration pode acontecer dezenas ou centenas de vezes numa run.

### 2.2 Inner loop — numerical search

Executado localmente pelo engine.

Exemplos:

- testar milhares de poses para um componente/cluster;
- executar LNS;
- simulated annealing;
- calcular distância entre polygons;
- avaliar collisions;
- executar A*/maze routing;
- testar layers/vias;
- calcular congestionamento;
- avaliar constraints;
- calcular scores;
- comparar candidate states.

Esse loop pode executar milhares ou milhões de operações sem chamar LLM.

### 2.3 Consequência arquitetural

Fluxo desejado:

```text
LLM macro decision
      ↓
structured request to local deterministic engine
      ↓
thousands of numerical/search operations
      ↓
small structured result summary
      ↓
LLM interprets result if another reasoning decision is required
```

Fluxo a evitar:

```text
LLM chooses coordinate
LLM chooses next coordinate
LLM chooses route segment
LLM chooses next segment
...
```

Isso reduz:

- custo;
- latency;
- nondeterminism;
- prompt size;
- risco de erro geométrico;
- dependência de provider.

---

## 3. Três camadas de mensagem

Toda chamada deve ser composta conceitualmente por três camadas.

### 3.1 Stable Agent Policy

Prompt estável, carregado como system/developer policy conforme o provider.

Define invariantes permanentes:

- papel do agente;
- o que ele pode e não pode decidir;
- deterministic engine como autoridade;
- não inventar medições;
- não assumir dados ausentes;
- respeitar provenance;
- usar somente ações/tools permitidas;
- retornar apenas o contrato solicitado;
- não tentar emitir geometria fina quando isso pertence ao engine.

Esse texto não precisa ser repetido como conteúdo de usuário em cada iteração se a API mantiver instruções de sistema.

### 3.2 Operation Preamble

Pré-prompt **curto e específico da iteração**.

Exemplo:

```text
Analyze the routing failure described in the input.
Identify the most plausible physical cause and select the smallest useful repair neighborhood.
Do not propose exact coordinates; request deterministic optimization actions only.
Return only the required structured response.
```

O preamble deve normalmente ter poucas linhas.

Ele responde apenas:

1. o que a IA deve fazer nesta operação;
2. qual o limite dessa decisão;
3. qual tipo de saída é esperado.

Não deve reexplicar toda a arquitetura do produto.

### 3.3 Structured Input

Depois do preamble vem um payload estruturado, preferencialmente JSON.

Exemplo conceitual:

```json
{
  "operation": "repair.plan.v1",
  "requestId": "req-01842",
  "designStateId": "state-0313",
  "objective": "Recover routability of N137 with minimum disruption",
  "focus": {
    "findingIds": ["F-921"],
    "netIds": ["N137"],
    "componentIds": ["U17", "C42"]
  },
  "facts": {},
  "effectiveConstraints": [],
  "metrics": {},
  "availableActions": [],
  "budget": {},
  "responseContract": {
    "id": "repair.plan.response.v1"
  }
}
```

---

## 4. Envelope comum de entrada

Todas as operações de IA devem compartilhar um envelope comum, mesmo quando o payload específico mudar.

Modelo conceitual:

```json
{
  "operation": "string",
  "schemaVersion": "string",
  "requestId": "string",
  "runId": "string",
  "designStateId": "string",
  "transactionId": "string|null",
  "objective": "string",
  "focus": {},
  "facts": {},
  "constraints": {},
  "metrics": {},
  "findings": [],
  "historySummary": {},
  "availableActions": [],
  "budgets": {},
  "responseContract": {
    "id": "string",
    "version": "string"
  }
}
```

Nem todos os campos precisam estar presentes em toda operação. O envelope real deve ser definido formalmente por JSON Schema.

---

## 5. Identidade e versionamento da operação

Toda chamada precisa de um `operation` estável e versionado.

Exemplos:

```text
constraint.suggest.v1
semantic.classify.v1
floorplan.strategy.v1
optimization.focus.select.v1
routing.failure.diagnose.v1
repair.plan.v1
block.review.v1
global.review.v1
adversarial.review.v1
candidate.compare.v1
```

Isso permite:

- prompts diferentes por tarefa;
- schemas específicos;
- testes independentes;
- métricas por operation type;
- migração futura sem quebrar runs antigas;
- troca de modelo por tarefa;
- replay de decisões.

---

## 6. Não enviar o PhysicalDesignState inteiro

O input deve conter apenas a **context view necessária à operação**.

Exemplo para reparar N137:

Enviar:

- N137;
- endpoints;
- routes/corridors próximos;
- blocking components;
- constraints afetadas;
- neighborhood geometry resumida;
- métricas before/after relevantes;
- findings relacionados;
- semantic relationships do neighborhood.

Não enviar automaticamente:

- todos os componentes da placa;
- todas as nets;
- todos os tracks;
- histórico completo de milhares de transactions.

A regra é:

> retrieve context by relevance, not by completeness.

Se a IA precisar de informação adicional, ela deve solicitar uma operação/tool de inspection explicitamente permitida.

---

## 7. Facts versus interpretation

O payload deve separar fatos medidos de interpretações.

Exemplo:

```json
{
  "facts": {
    "availableCorridorWidthMm": 0.91,
    "requiredCorridorWidthMm": 1.46,
    "blockingComponents": ["U17", "C42"]
  },
  "semanticContext": {
    "U17": {
      "role": "switching_regulator",
      "source": "USER_CONFIRMED"
    }
  }
}
```

A IA nunca deve tratar inference como measurement.

Provenance importante:

```text
IMPORTED
USER_DEFINED
AI_INFERRED
DERIVED
DETERMINISTIC_MEASUREMENT
DEFAULT
UNKNOWN
```

---

## 8. Dados desconhecidos permanecem desconhecidos

Ausência de informação não deve ser preenchida por invenção.

Exemplo:

```json
{
  "frequencyHz": null,
  "frequencyStatus": "UNKNOWN"
}
```

A IA pode responder:

```json
{
  "assumptions": [],
  "missingInformation": [
    {
      "field": "frequencyHz",
      "importance": "LOW"
    }
  ]
}
```

Ela não deve fabricar `10 MHz` apenas para completar um campo.

---

## 9. Response Contract obrigatório

Toda operação tem um schema de resposta explícito.

A resposta não deve ser prose livre seguida de JSON.

Ela deve ser **somente o objeto estruturado esperado**.

Exemplo conceitual de `repair.plan.response.v1`:

```json
{
  "status": "PROPOSED",
  "summary": "Routing failure is primarily caused by insufficient corridor width between U17 and C42.",
  "diagnosis": {
    "primaryCause": "LOCAL_GEOMETRIC_BLOCKAGE",
    "confidence": 0.94,
    "evidenceRefs": ["fact:requiredCorridorWidthMm", "fact:availableCorridorWidthMm"]
  },
  "recommendedActions": [
    {
      "action": "REQUEST_LOCAL_OPTIMIZATION",
      "targetIds": ["U17", "C42"],
      "priority": 1,
      "constraintsToPreserve": ["C-18", "C-31"],
      "objective": "Increase routing corridor for N137 with minimum movement"
    }
  ],
  "fallbackActions": [],
  "assumptions": [],
  "missingInformation": [],
  "confidence": 0.91
}
```

`summary` é uma justificativa curta e auditável; **não é chain-of-thought**.

O sistema nunca precisa pedir ou armazenar raciocínio privado passo a passo do modelo.

---

## 10. JSON Schema: contrato lógico versus transporte

O contrato deve existir sempre, mas existem duas formas de entregá-lo ao modelo.

### 10.1 Provider com structured output/schema enforcement

Preferência:

```text
Operation preamble
+ JSON input
+ response schema passed through provider API
```

Nesse caso o schema completo **não precisa ser repetido como texto no prompt**.

O input pode apenas conter:

```json
{
  "responseContract": {
    "id": "repair.plan.response",
    "version": "1"
  }
}
```

Benefícios:

- menos tokens;
- menor chance de output inválido;
- contrato realmente enforceable;
- melhor versionamento.

### 10.2 Provider sem schema enforcement

Fallback:

- enviar schema/forma compacta junto do prompt;
- exigir JSON-only;
- validar localmente;
- rejeitar e retry se inválido.

### 10.3 Regra de arquitetura

O Domain/Application conhece o **contract ID**, não a sintaxe proprietária do provider.

O adapter de provider converte o contract interno para o mecanismo disponível naquela API.

---

## 11. Envelope comum de resposta

Mesmo com responses específicas, é útil padronizar campos comuns.

Modelo conceitual:

```json
{
  "status": "PROPOSED|NO_ACTION|NEEDS_INFORMATION|UNRESOLVED|ERROR",
  "summary": "string",
  "actions": [],
  "findings": [],
  "assumptions": [],
  "missingInformation": [],
  "confidence": 0.0,
  "nextOperationHint": "string|null"
}
```

Campos específicos podem complementar esse envelope.

---

## 12. A IA recomenda ações; o engine decide validade

Um response da IA nunca é aplicado diretamente ao `PhysicalDesignState`.

Fluxo:

```text
AI Response
   ↓
JSON schema validation
   ↓
semantic/application validation
   ↓
action authorization
   ↓
Deterministic preconditions
   ↓
Candidate transaction
   ↓
Deterministic evaluation
   ↓
commit / reject / repair
```

Exemplo:

IA pede:

```json
{
  "action": "REQUEST_LOCAL_OPTIMIZATION",
  "targetIds": ["U17", "C42"]
}
```

O engine pode descobrir que `U17` está locked.

Resultado:

```text
AI proposal ≠ executable action
```

A proposta é traduzida/validada antes de execução.

---

## 13. Responsabilidades da IA

A IA é indicada para problemas em que conhecimento semântico, abstração ou julgamento contextual têm alto valor.

### 13.1 Semantic enrichment

IA pode:

- classificar componentes;
- reconhecer blocos funcionais;
- identificar provável decoupling relationship;
- reconhecer feedback networks;
- reconhecer switching nodes;
- reconhecer differential pairs;
- interpretar nomes de nets/pins;
- associar informações de datasheet/application note.

Toda inferência recebe provenance/confidence.

### 13.2 Suggest constraints

IA pode sugerir:

- relações de proximidade;
- isolamento;
- block placement;
- routing intent;
- EMI concerns;
- thermal concerns;
- critical topologies.

Usuário ou policy decide quando a sugestão vira regra efetiva.

### 13.3 Floorplanning strategy

IA pode propor:

- quais blocos devem ocupar determinadas regiões;
- separação macro de power/analog/digital;
- ordem de placement;
- nets/interfaces prioritárias;
- corridors que merecem reserva antecipada.

### 13.4 Optimization focus selection

IA pode escolher:

- qual finding atacar;
- qual neighborhood reabrir;
- que bloco possui maior risco;
- quando escalar de repair local para reorganização maior.

### 13.5 Failure diagnosis

IA pode interpretar structured diagnostics e responder:

- causa provável;
- objetos envolvidos;
- estratégia de repair;
- trade-offs que merecem exploração.

### 13.6 Functional review

IA pode revisar semanticamente:

- buck;
- ADC;
- op-amp;
- crystal;
- differential interface;
- current sensing;
- power return;
- outros blocos.

### 13.7 Global/adversarial review

IA pode buscar problemas que não estão codificados numa única regra determinística.

### 13.8 Explainability

IA pode transformar métricas/diffs estruturados em explicações curtas e úteis ao usuário.

---

## 14. Responsabilidades do engine local determinístico

A seguir estão responsabilidades que **não devem ser delegadas ao LLM como autoridade**.

### 14.1 Geometria

- polygon intersection;
- distance;
- clearance;
- courtyard collision;
- board boundary;
- keepouts;
- pad coordinates;
- via coordinates;
- route geometry.

### 14.2 DRC e hard constraints

- minimum spacing;
- minimum track width;
- via/drill rules;
- copper-to-edge;
- allowed layers;
- locked objects;
- board limits;
- explicit user constraints.

### 14.3 Routing

- pin-access analysis;
- global routing/corridor reservation;
- pathfinding;
- A*/maze routing;
- negotiated congestion;
- rip-up/reroute;
- exact route geometry;
- exact via placement;
- length/skew measurement.

### 14.4 Numerical optimization

- LNS;
- simulated annealing;
- candidate generation numeric;
- local search;
- score calculation;
- candidate ranking by defined objective function.

### 14.5 Measurements

- distance;
- track length;
- via count;
- overlap;
- congestion values;
- loop-area proxies;
- candidate metric deltas.

### 14.6 State and transactions

- begin transaction;
- apply actions;
- rollback;
- commit;
- diff;
- replay;
- state hashing/versioning.

### 14.7 Regression

- pass/fail baseline;
- newly violated hard constraint;
- resolved violation;
- metric degradation;
- regression classification baseada em regras explícitas.

### 14.8 Persistence/import/export

- PRDX;
- EDA adapters;
- layout persistence;
- provenance persistence.

A implementação algorítmica inicial dessas responsabilidades está especificada no documento `10`.

---

## 15. Responsabilidades híbridas

Algumas tarefas combinam IA e engine.

### 15.1 Placement

IA:

- escolhe bloco/neighborhood;
- expressa intenção;
- identifica relações importantes.

Engine:

- gera poses por LNS/SA e heurísticas locais;
- testa rotations;
- mede constraints;
- estima routing;
- escolhe candidatos numericamente.

### 15.2 Repair

IA:

- diagnostica e propõe classes de repair.

Engine:

- testa os repairs reais;
- calcula geometria;
- reroute;
- mede regressões;
- aceita somente candidate válido.

### 15.3 Candidate comparison

Engine:

- fornece métricas exatas/normalizadas.

IA:

- interpreta trade-offs difíceis quando não existe uma preferência matemática suficiente.

A decisão final ainda precisa respeitar hard constraints e policy do produto.

### 15.4 Constraint derivation

IA:

- sugere que determinada relação importa.

Engine/rule layer:

- converte para constraint suportada;
- mede;
- valida conflitos;
- aplica inheritance.

---

## 16. Catálogo inicial de operações de IA

### `semantic.classify.v1`

Entrada:

- component/net context;
- connectivity;
- pin names;
- imported metadata;
- optional datasheet excerpts.

Saída:

- semantic roles;
- relationships;
- confidence;
- evidence refs.

### `constraint.suggest.v1`

Entrada:

- selected entities;
- semantics;
- current rules;
- manufacturing/board context.

Saída:

- suggested constraints;
- reason;
- severity recommendation;
- confidence.

### `floorplan.strategy.v1`

Entrada:

- functional groups;
- board shape;
- fixed objects;
- high-level electrical relations.

Saída:

- region strategy;
- ordering;
- isolation intentions;
- corridor reservations to consider.

### `optimization.focus.select.v1`

Entrada:

- global metrics;
- findings;
- unrouted nets;
- congestion hotspots;
- recent improvements/failures.

Saída:

- next focus;
- neighborhood;
- objective;
- escalation level.

### `routing.failure.diagnose.v1`

Entrada:

- deterministic RouteFailure;
- local geometry summary;
- blockers;
- constraints;
- recent transaction history summary.

Saída:

- cause classification;
- confidence;
- repair directions.

### `repair.plan.v1`

Entrada:

- finding/failure;
- affected neighborhood;
- allowed actions;
- constraints to preserve.

Saída:

- prioritized repair classes;
- target entities;
- objective;
- fallbacks.

### `block.review.v1`

Entrada:

- functional block summary;
- measured geometry/routing metrics;
- semantic relationships;
- open findings.

Saída:

- semantic findings;
- evidence refs;
- suggested deterministic checks/repairs.

### `global.review.v1`

Entrada:

- compact whole-board metrics;
- hotspot summaries;
- critical relationships;
- unresolved findings.

Saída:

- global concerns;
- prioritized follow-ups;
- possible neighborhoods to reopen.

### `adversarial.review.v1`

Entrada:

- independent candidate summary;
- no decision-history justification unless explicitly needed.

Saída:

- possible overlooked concerns;
- evidence refs;
- confidence.

---

## 17. AvailableActions como capability boundary

O modelo não deve poder inventar comandos arbitrários.

Exemplo:

```json
{
  "availableActions": [
    {
      "type": "REQUEST_LOCAL_OPTIMIZATION",
      "allowedTargetKinds": ["COMPONENT", "GROUP"],
      "maxTargets": 12
    },
    {
      "type": "REQUEST_REROUTE",
      "allowedTargetKinds": ["NET", "NET_GROUP"]
    },
    {
      "type": "REQUEST_CORRIDOR_REPLAN",
      "allowedTargetKinds": ["NET", "NET_GROUP"]
    }
  ]
}
```

A operação de IA só pode recomendar capability conhecida pelo Application Layer.

---

## 18. Context escalation

Não iniciar uma chamada com o maior contexto possível.

Níveis conceituais:

```text
L1 local entity/neighborhood
L2 + related nets/constraints/congestion
L3 + whole functional block
L4 + wider board summary
```

Uma operação começa no menor nível que provavelmente contém informação suficiente.

Se a resposta for `NEEDS_INFORMATION`, o orchestrator pode buscar/escalar contexto conforme policy e budget.

---

## 19. Request additional information

A IA não deve receber acesso livre ao banco/estado.

Quando precisar de algo, retorna solicitação estruturada permitida.

Exemplo:

```json
{
  "status": "NEEDS_INFORMATION",
  "missingInformation": [
    {
      "queryType": "RELATED_NETS",
      "entityId": "U17",
      "reason": "Need to verify whether moving U17 disrupts another critical interface"
    }
  ]
}
```

O orchestrator decide se:

- atende;
- nega;
- resume;
- encerra por budget.

---

## 20. Validation em três níveis

### 20.1 Syntax/schema

Pergunta:

> o JSON está no formato correto?

### 20.2 Semantic/application

Perguntas:

```text
entity exists?
constraint exists?
operation status is supported?
confidence range valid?
action arguments coherent?
```

### 20.3 Authorization/capability

Perguntas:

```text
action allowed for this operation?
target type allowed?
target locked?
budget permits it?
policy permits it?
```

Somente depois disso uma candidate transaction pode ser iniciada.

---

## 21. Retry policy

Retry não deve ser infinito.

Categorias:

### Invalid JSON/schema

- retry curto pedindo correção estrutural;
- usar mesmo request ID + attempt index;
- se persistir, falhar operação.

### Semantic invalidity

Exemplo: referência a entity inexistente.

- fornecer error summary mínimo;
- permitir no máximo pequeno número de correction attempts.

### Action rejected by deterministic engine

Não é necessariamente erro do LLM.

O orchestrator pode produzir nova operação contendo:

```text
proposal rejected
reason = TARGET_LOCKED
```

para planejar fallback.

---

## 22. Idempotency e stale state

Toda resposta precisa estar vinculada ao `designStateId` usado no request.

Antes de aplicar:

```text
response.designStateId == current expected state?
```

Se a placa avançou enquanto a IA respondia, a proposta pode estar stale.

Policy:

- nunca aplicar silenciosamente uma action em state diferente;
- revalidar/rebase quando seguro;
- caso contrário descartar/reconsultar.

---

## 23. Budgets

Toda operação deve conhecer limites.

Exemplos:

```json
{
  "budgets": {
    "maxActions": 3,
    "maxTargetsPerAction": 12,
    "maxContextEscalations": 2,
    "maxRetries": 1
  }
}
```

Budgets globais da run incluem:

- AI calls;
- tokens/cost;
- deterministic candidate count;
- compute time;
- repair depth;
- no-improvement threshold.

---

## 24. Event-driven AI calls

Não chamar LLM em intervalo arbitrário.

Eventos úteis:

```text
semantic uncertainty above threshold
user asks Suggest Constraints
repeated local deterministic failure
optimization stalls
functional block stabilizes
candidate approaches final review
unusual regression pattern
```

Eventos puramente geométricos resolvidos deterministicamente não precisam de LLM.

---

## 25. Routing failure flow

Exemplo completo:

```text
Detailed router
    ↓
RouteFailure N137
    ↓
local alternate path/layer/rip-up strategies exhausted by policy
    ↓
routing.failure.diagnose.v1
    ↓
LLM selects likely repair class / neighborhood
    ↓
request_local_optimization
    ↓
LNS/SA tests thousands of local placements
    ↓
global + local detailed rerouting
    ↓
regression engine
    ↓
structured outcome
```

A IA entra apenas quando o routing local já produziu facts/diagnostics úteis ou quando policy decidiu que o custo de continuar deterministicamente não compensa.

---

## 26. Agent Operation Definition

Cada operation deve possuir metadata versionada.

Modelo conceitual:

```text
AgentOperationDefinition
 ├── id
 ├── version
 ├── purpose
 ├── preambleTemplate
 ├── inputSchemaId
 ├── responseSchemaId
 ├── allowedActions
 ├── defaultModelPolicy
 ├── defaultBudgets
 ├── retryPolicy
 └── contextBuilder
```

Não espalhar prompts hardcoded pelo código.

---

## 27. Prompt registry

Prompts devem ser assets versionados.

Exemplo:

```text
Agent/Prompts/
  semantic.classify.v1.txt
  constraint.suggest.v1.txt
  repair.plan.v1.txt
  global.review.v1.txt
```

Alterar prompt é mudança de comportamento e precisa ser rastreável em benchmarks.

---

## 28. Schema registry

Contratos também são assets versionados.

```text
Agent/Schemas/
  common.request.v1.schema.json
  common.response.v1.schema.json
  repair.plan.request.v1.schema.json
  repair.plan.response.v1.schema.json
```

Uma run registra os schema IDs/hashes usados.

---

## 29. Provider abstraction

Application Layer envia uma operação abstrata.

Adapter do provider decide:

- formato da chamada;
- JSON mode/structured output;
- thinking mode;
- model identifier;
- token settings;
- retries de transporte.

Nenhum business rule depende de sintaxe DeepSeek/OpenAI/etc.

DeepSeek é o provider inicial conforme ADR-0001.

---

## 30. Model policies por operação

Não espalhar model names no código.

Exemplo conceitual:

```text
FAST
STANDARD_REASONING
DEEP_REASONING
```

Operation definition escolhe policy.

Provider config resolve para model/thinking settings.

Isso permite benchmark e troca de provider sem alterar contracts.

---

## 31. Thinking mode

Thinking/reasoning mode é propriedade de execução do provider, não do Domain.

Exemplo:

```text
semantic.classify.v1 → FAST
repair.plan.v1       → STANDARD_REASONING
adversarial.review   → DEEP_REASONING
```

Essas mappings precisam ser benchmarkadas.

---

## 32. Logging e audit trail

Registrar no mínimo:

```text
operation id/version
request id/run id
design state id
provider/model
model policy
prompt version/hash
schema version/hash
input hash
structured input
structured output
validation result
proposed actions
executed actions
deterministic outcome
latency
tokens/cost when available
```

Secrets nunca entram nesse log.

---

## 33. Replay

Uma chamada histórica deve poder ser reproduzida em harness:

```text
same operation
same structured input
provider/model A
provider/model B
prompt v1
prompt v2
```

Comparar:

- schema validity;
- action quality;
- deterministic success;
- repair success;
- regressions;
- tokens;
- latency;
- cost.

---

## 34. Avaliar IA pelo outcome

Não medir apenas “qualidade textual”.

Métricas:

```text
proposal validity rate
proposal execution rate
repair success rate
finding precision/recall where ground truth exists
improvement after AI-selected neighborhood
schema failure rate
retry rate
cost/run
latency/run
```

A pergunta correta é:

> a macro-decisão da IA melhora o comportamento do engine local?

---

## 35. Explainability sem chain-of-thought

Guardar:

```text
summary
reason code
facts/evidence refs
metric deltas
chosen action
result
```

Não depender de raciocínio privado detalhado.

Isso é mais compacto, auditável e estável.

---

## 36. Privacidade e minimização de dados

Como o provider inicial é cloud:

- enviar apenas context view necessária;
- não enviar automaticamente projeto inteiro;
- não enviar secrets/credentials;
- permitir identificar no audit log qual operação enviou quais entidades/campos;
- separar provider configuration do PRDX.

A UI deve poder indicar quando uma operação usa cloud versus processamento totalmente local.

---

## 37. Falha/indisponibilidade cloud

Sem IA ainda deve ser possível:

- importar;
- editar constraints;
- geometry/DRC;
- placement search determinístico;
- global/detailed routing;
- rip-up/reroute;
- regressão;
- export/report.

A run pode perder semantic reasoning avançado, mas não a integridade física do engine.

---

## 38. Testes de AgentOperations

Cada operation deve possuir fixtures de:

- happy path;
- unknown data;
- malformed response;
- hallucinated entity;
- unauthorized action;
- stale state;
- no-action;
- needs-information;
- retry exhausted;
- deterministic rejection.

---

## 39. Critério de implementação de nova AgentOperation

Uma nova operation só deve existir se:

1. o problema realmente exige reasoning/semantic judgment;
2. o input pode ser representado compactamente;
3. existe response contract claro;
4. existem actions/capabilities locais reais para executar a decisão;
5. outcome pode ser medido.

Não criar operation porque “talvez seja útil perguntar à IA”.

---

## 40. Ordem inicial de implementação

Depois do engine local mínimo necessário:

1. `semantic.classify.v1`;
2. `constraint.suggest.v1`;
3. `routing.failure.diagnose.v1`;
4. `repair.plan.v1`;
5. `optimization.focus.select.v1`;
6. `block.review.v1`;
7. `global.review.v1`;
8. `adversarial.review.v1`.

`floorplan.strategy.v1` pode entrar cedo se os primeiros test boards mostrarem benefício claro.

---

## 41. Princípio final

```text
LOCAL ENGINE
measures, searches, routes, validates, commits

CLOUD AI
classifies, prioritizes, diagnoses, proposes, reviews
```

A interface entre ambos é sempre estruturada, versionada e auditável.

Se uma operação puder ser resolvida de forma robusta por algoritmo local conhecido, ela **não deve virar uma chamada de IA apenas por conveniência de implementação**.
