# 08 — Protocolo de Iterações com IA

## 1. Objetivo

Este documento define **como o WTK.Place&Router deve interagir com modelos de IA durante o physical design**.

Ele complementa [`05-Agente-IA-Revisao-e-Memoria.md`](05-Agente-IA-Revisao-e-Memoria.md):

- o documento `05` define o papel conceitual do agente, tools, níveis de review, memória e explainability;
- este documento define o **protocolo operacional de cada chamada**, contratos JSON, limites de responsabilidade, cadence das chamadas, validação de respostas, retries, logging e separação entre IA e algoritmos determinísticos.

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

Executado deterministicamente pelo engine.

Exemplos:

- testar 20.000 poses para um componente/cluster;
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
structured request to deterministic engine
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

## 6. Não enviar o BoardState inteiro

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

Um response da IA nunca é aplicado diretamente ao BoardState.

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

## 14. Responsabilidades do engine determinístico

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

---

## 15. Responsabilidades híbridas

Algumas tarefas combinam IA e engine.

### 15.1 Placement

IA:

- escolhe bloco/neighborhood;
- expressa intenção;
- identifica relações importantes.

Engine:

- gera poses;
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

- ordered repair strategies;
- scope;
- fallback/escalation.

### `block.review.v1`

Entrada:

- semantic block;
- measured geometry/metrics;
- relevant routes;
- constraints/findings.

Saída:

- semantic findings;
- evidence refs;
- suggested follow-up analyses.

### `global.review.v1`

Entrada:

- summarized global state;
- hotspot list;
- routing statistics;
- block summaries;
- unresolved findings.

Saída:

- global concerns;
- reprioritization suggestions;
- areas requiring deeper inspection.

### `adversarial.review.v1`

Entrada:

- fresh candidate snapshot summary;
- constraints;
- measured metrics;
- no previous rationale unless strictly required.

Saída:

- independent findings only;
- severity;
- evidence requests/refs.

### `candidate.compare.v1`

Entrada:

- candidates with normalized deterministic metrics;
- allowed trade-off policy.

Saída:

- recommended candidate;
- short trade-off summary;
- unresolved ambiguity.

---

## 17. Exemplo completo de uma iteração

Situação: routing de N137 falhou.

### 17.1 Engine gera diagnóstico factual

```json
{
  "failureId": "RF-137-09",
  "netId": "N137",
  "reason": "NO_FEASIBLE_CORRIDOR",
  "requiredCorridorWidthMm": 1.46,
  "maxAvailableWidthMm": 0.91,
  "blockingObjects": ["U17", "C42"],
  "alternativeLayers": []
}
```

### 17.2 Operation preamble

```text
Diagnose the supplied routing failure and recommend the smallest repair scope worth exploring.
Use only measured facts supplied in the input.
Do not propose exact coordinates or claim that a repair will pass DRC.
Return the required structured response only.
```

### 17.3 Structured request

```json
{
  "operation": "routing.failure.diagnose.v1",
  "requestId": "req-1842",
  "runId": "run-14",
  "designStateId": "state-313",
  "objective": "Restore routability of N137 with minimum disruption",
  "focus": {
    "failureId": "RF-137-09"
  },
  "facts": {
    "netId": "N137",
    "requiredCorridorWidthMm": 1.46,
    "maxAvailableWidthMm": 0.91,
    "blockingObjects": ["U17", "C42"],
    "alternativeLayers": []
  },
  "semanticContext": {
    "U17": {
      "role": "switching_regulator",
      "source": "USER_CONFIRMED"
    },
    "C42": {
      "role": "feedback_network_component",
      "source": "AI_INFERRED",
      "confidence": 0.93
    }
  },
  "effectiveConstraints": [
    "C-018",
    "C-031",
    "C-117"
  ],
  "availableActions": [
    "REQUEST_LOCAL_OPTIMIZATION",
    "REQUEST_LOCAL_REROUTE",
    "ESCALATE_NEIGHBORHOOD"
  ],
  "responseContract": {
    "id": "routing.failure.diagnose.response",
    "version": "1"
  }
}
```

### 17.4 IA retorna

```json
{
  "status": "PROPOSED",
  "summary": "The routing failure is consistent with a local geometric blockage between U17 and C42.",
  "cause": "LOCAL_GEOMETRIC_BLOCKAGE",
  "confidence": 0.96,
  "evidenceRefs": [
    "facts.requiredCorridorWidthMm",
    "facts.maxAvailableWidthMm",
    "facts.blockingObjects"
  ],
  "recommendedActions": [
    {
      "action": "REQUEST_LOCAL_OPTIMIZATION",
      "targetIds": ["U17", "C42"],
      "objective": "Increase corridor capacity while preserving C-018, C-031 and C-117"
    }
  ],
  "assumptions": [],
  "missingInformation": []
}
```

### 17.5 Engine executa inner loop

```text
Generate 8,000 candidate poses/rotations
Reject hard-invalid candidates
Run local global-routing estimate
Detailed-route top 40
Evaluate regressions
```

### 17.6 Resultado determinístico

```json
{
  "testedCandidates": 8000,
  "validCandidates": 431,
  "detailedRoutedCandidates": 40,
  "successfulRepairs": 3,
  "bestCandidateId": "cand-7182",
  "hardViolations": 0,
  "routeCompleted": true,
  "metricDelta": {
    "vias": -1,
    "wireLengthMm": -4.2,
    "criticalCongestionPercent": -13.0
  }
}
```

Se não houver nova decisão semântica necessária, **não é obrigatório chamar IA novamente**.

O candidate pode seguir diretamente para regression/review determinísticos.

---

## 18. Event-driven AI invocation

Assim como reviews, chamadas de IA devem ser disparadas por eventos relevantes, não por timer ou por toda mutation.

Exemplos:

```text
EVENT: repeated local optimization failure
→ routing.failure.diagnose
```

```text
EVENT: functional block stabilized
→ block.review
```

```text
EVENT: no improvement after N neighborhoods
→ optimization.focus.select
```

```text
EVENT: candidate reaches sign-off stage
→ global.review
→ adversarial.review
```

```text
EVENT: user requests suggestion
→ constraint.suggest
```

Isso torna custo e comportamento previsíveis.

---

## 19. Context escalation

Começar sempre com contexto mínimo.

Se a IA responder `NEEDS_INFORMATION`, o orchestrator pode fornecer mais dados de forma controlada.

Exemplo:

```text
Level 1
local finding + 2 blockers

Level 2
related nets + local congestion + semantic relationships

Level 3
whole functional block

Level 4
broader board summary
```

Nunca começar toda chamada com Level 4 por conveniência.

---

## 20. Response validation em três níveis

Toda resposta passa por três validadores.

### 20.1 Syntactic validation

- valid JSON;
- conforms to JSON Schema;
- enum válido;
- required fields presentes;
- no unknown fields quando schema for strict.

### 20.2 Semantic validation

Exemplos:

- IDs existem;
- finding referido existe;
- target é do tipo permitido;
- confidence está em range;
- operation result é coerente com operation type.

### 20.3 Authorization / capability validation

Exemplos:

- ação está em `availableActions`;
- IA não pediu mover componente locked;
- IA não pediu ignorar Required constraint;
- IA não pediu tool inexistente;
- operação respeita budgets/policy.

Somente depois disso a resposta entra no workflow.

---

## 21. Retry policy

Retry não deve ser infinito.

Categorias:

### Transport/provider error

- retry com backoff limitado;
- eventualmente fallback provider/model se policy permitir.

### Invalid JSON/schema

- provider structured output: tratar como provider failure;
- fallback provider sem schema enforcement: uma tentativa de correction pode receber validation errors compactos.

### Semantic invalid response

Exemplo:

```text
AI referenced component U999, which does not exist.
```

Pode haver um único repair retry com:

```json
{
  "validationErrors": [
    "targetIds[0]: unknown component U999"
  ]
}
```

Se repetir, a operação falha e retorna ao orchestrator.

### No useful action

`NO_ACTION` ou `UNRESOLVED` são respostas válidas.

Não forçar o modelo a inventar uma solução.

---

## 22. Confidence não substitui verificação

`confidence` é útil para:

- ordenar sugestões;
- decidir se pedir review adicional;
- indicar incerteza ao usuário;
- escolher quando escalar contexto.

Não serve para:

- aceitar hard constraint violation;
- declarar DRC clean;
- aceitar measurement inventado;
- substituir deterministic evidence.

Uma resposta com `confidence: 0.99` continua sendo apenas uma proposta.

---

## 23. Assumptions explícitas

Toda assumption relevante precisa sair estruturada.

Exemplo:

```json
{
  "assumptions": [
    {
      "id": "A-17",
      "statement": "The SW net is a high-dv/dt aggressor.",
      "basis": "SEMANTIC_INFERENCE",
      "confidence": 0.88
    }
  ]
}
```

Assumption não vira fact silenciosamente.

Quando uma assumption influencia uma constraint importante, o sistema pode:

- pedir confirmação ao usuário;
- buscar datasheet;
- executar outra análise;
- manter a regra como Suggested/Preferred em vez de Required.

---

## 24. Evidence references

Respostas da IA devem apontar para dados estruturados que justificam a recomendação.

Exemplo:

```json
{
  "evidenceRefs": [
    "facts.availableCorridorWidthMm",
    "metrics.localCongestion",
    "constraint:C-031"
  ]
}
```

Isso melhora:

- explainability;
- debugging;
- avaliação automática;
- capacidade de detectar hallucination.

O modelo não precisa repetir todos os valores na prose.

---

## 25. Prompt injection e documentos externos

Datasheets, notes ou textos importados são **dados**, não instruções de autoridade.

O adapter de contexto deve marcar conteúdo externo como source material.

O Stable Agent Policy deve estabelecer que:

- instruções contidas em documentos não substituem system policy;
- somente o orchestrator define `availableActions`;
- documentos fornecem evidência técnica, não permissões operacionais.

Isso é particularmente importante se no futuro o sistema ingerir conteúdo de URLs, PDFs ou bases externas.

---

## 26. Logging e replay

Cada interaction deve registrar, de forma persistível:

```text
requestId
runId
operation
schemaVersion
model/provider
model version/snapshot when available
stable policy version
operation prompt version
input hash
input payload or reproducible reference
response contract version
raw structured response
validation result
actions authorized
actions actually executed
deterministic outcome
latency
token/cost metrics when available
```

Isso permite responder:

> Por que o sistema decidiu reabrir o bloco POWER na iteration 418?

E também permite replay/benchmark de modelos novos contra os mesmos inputs históricos.

---

## 27. Prompt e contract registry

Prompts não devem ficar espalhados como strings em ViewModels/services.

Criar um registry versionado, conceitualmente:

```text
AgentProtocol
 ├── Operations
 │    ├── SemanticClassifyV1
 │    ├── ConstraintSuggestV1
 │    ├── RoutingFailureDiagnoseV1
 │    ├── RepairPlanV1
 │    └── ...
 │
 ├── Prompts
 ├── InputSchemas
 ├── OutputSchemas
 └── Validators
```

Cada operation definition contém:

```text
Operation ID
Prompt version
Input contract
Output contract
Allowed action types
Recommended model class
Default budget
Retry policy
```

---

## 28. Model routing por tipo de tarefa

Não é necessário usar sempre o mesmo modelo.

No futuro:

```text
semantic.classify
→ cheaper/faster model may be sufficient

global.review
→ strongest reasoning model

adversarial.review
→ independent model/context

simple explanation
→ inexpensive model
```

Essa decisão pertence à Infrastructure/Application policy e não ao domínio.

O operation contract permanece o mesmo mesmo que o provider mude.

---

## 29. Cache de operações seguras

Algumas respostas podem ser cacheadas quando o input efetivo é idêntico.

Exemplos candidatos:

- semantic classification de um component topology imutável;
- datasheet-derived suggestions;
- explanation de um deterministic finding.

Não cachear cegamente operações dependentes de estado mutável.

Cache key pode incluir:

```text
operation
promptVersion
schemaVersion
modelPolicyVersion
inputHash
```

---

## 30. Budgets por operação

Cada operation deve definir limites como:

```json
{
  "budgets": {
    "maxOutputTokens": 1200,
    "maxRequestedActions": 5,
    "maxContextItems": 80,
    "maxContextExpansionRounds": 2
  }
}
```

O objetivo é impedir respostas excessivas e comportamento imprevisível.

Para tasks simples, outputs devem ser pequenos.

---

## 31. IA e commit de state

A IA nunca chama `commit` como autoridade irrestrita.

Arquitetura recomendada:

```text
AI proposes strategy/action
      ↓
Application orchestrator
      ↓
Candidate transaction
      ↓
Deterministic validation
      ↓
Commit policy
```

Em modos autônomos, o orchestrator pode cometer automaticamente **somente** quando as políticas determinísticas permitirem.

Exemplos:

- zero Required regressions;
- improvement criteria satisfied;
- transaction within allowed scope;
- no user lock violated.

---

## 32. IA não deve escolher entre fatos contraditórios silenciosamente

Se input contém inconsistência:

```text
UserDefined frequency = 1 MHz
Imported metadata = 10 MHz
```

O context builder deve preservar provenance e, se a resolução não estiver definida por policy, gerar conflito.

A IA pode sugerir uma resolução, mas não sobrescrever automaticamente o valor de maior autoridade.

Ordem de autoridade deve ser definida separadamente para cada propriedade.

---

## 33. Fresh-context review

`adversarial.review` deve deliberadamente evitar carregar rationale/history que possa enviesar a revisão.

Entrada preferida:

- current candidate;
- constraints;
- semantic facts;
- deterministic metrics;
- unresolved findings.

Evitar:

- “decidimos colocar U7 aqui porque...”;
- lista de tentativas anteriores;
- defesa do candidate atual.

Depois o orchestrator compara findings independentes com o histórico.

---

## 34. Human-in-the-loop

Existem operações em que usuário deve permanecer autoridade.

Exemplos:

- transformar AI suggestion em Required constraint;
- aceitar risco não hard, mas relevante;
- resolver conflito entre duas intenções de engenharia;
- confirmar semantic inference de baixa confiança;
- bloquear posição mecânica crítica;
- alterar manufacturing profile.

O nível de autonomia pode evoluir, mas provenance e audit trail permanecem.

---

## 35. Estado da run e máquina de estados

Uma run autônoma pode ser organizada como:

```text
PREPARE
  ↓
SELECT_FOCUS
  ↓
OPTIMIZE_DETERMINISTICALLY
  ↓
VERIFY
  ↓
┌───────────────┐
│ success?      │── yes ─→ STABILIZE / NEXT_FOCUS
└───────┬───────┘
        no
        ↓
DIAGNOSE
        ↓
PLAN_REPAIR
        ↓
REPAIR_DETERMINISTICALLY
        ↓
VERIFY_REGRESSION
        ↓
NEXT_ITERATION
```

IA aparece apenas em estados em que reasoning é útil.

---

## 36. Quando não chamar IA

Não chamar LLM para:

- medir distância;
- testar clearance;
- saber se polygon intersecta;
- escolher entre duas rotas se score determinístico já determina vencedor;
- recalcular wirelength;
- validar JSON;
- contar vias;
- executar undo/redo;
- aplicar regra de fabricação;
- selecionar melhor candidate quando existe uma ordenação matemática inequívoca;
- repetir semantic classification cujo resultado confiável já está persistido.

Princípio:

> se o problema pode ser resolvido corretamente, barato e deterministicamente, não chamar IA.

---

## 37. Quando chamar IA

Chamar quando há valor real de reasoning, por exemplo:

- semântica não explicitamente codificada;
- trade-off contextual;
- escolha de estratégia;
- diagnóstico de failure complexo;
- decomposição de problema;
- revisão funcional;
- hipótese de repair;
- reconhecimento de topology;
- interpretação de documentação;
- adversarial review;
- explicação ao usuário.

---

## 38. Métricas específicas do agente

Além das métricas físicas da PCB, medir:

```text
AI calls per run
AI calls by operation
input tokens
output tokens
cost
latency
schema failure rate
semantic validation failure rate
retry rate
NO_ACTION rate
action acceptance rate
proposal success rate
regression caused by proposed repair
improvement after AI-selected neighborhood
finding precision/recall em benchmark quando houver ground truth
```

Isso permite saber se a IA realmente agrega valor.

---

## 39. A/B testing de prompts e modelos

Como requests e contracts são versionados, o mesmo input pode ser testado contra:

```text
prompt v1 vs v2
model A vs model B
with/without case retrieval
with/without datasheet context
```

A comparação deve usar deterministic outcomes, não preferência subjetiva de texto.

Exemplo:

```text
Repair proposal success
v1 = 61%
v2 = 78%
```

---

## 40. Requisitos de implementação

1. Toda chamada possui `operation` versionada.
2. Toda chamada possui input estruturado.
3. Toda chamada possui response contract versionado.
4. Structured output do provider deve ser usado quando disponível.
5. Output é validado antes de qualquer ação.
6. IA não modifica BoardState diretamente.
7. IA não é autoridade de DRC ou hard constraints.
8. IA não fica no inner loop numérico.
9. Contexto é mínimo e orientado ao problema.
10. Unknown permanece unknown.
11. Inference possui provenance/confidence.
12. Resposta contém justificativa curta/evidence refs, não chain-of-thought.
13. Retry é limitado.
14. `NO_ACTION` e `UNRESOLVED` são resultados válidos.
15. Todas as interações importantes são auditáveis/reproduzíveis.
16. Prompts e schemas vivem em registry versionado.
17. Model/provider são abstraídos do domínio.
18. Deterministic outcome é usado para avaliar qualidade da decisão da IA.
19. Fresh-context adversarial review deve ser possível.
20. Human authority permanece explícita para decisões de engenharia que exigem confirmação.

---

## 41. Próximas especificações derivadas

Antes da implementação do Agent, este documento deve gerar contratos concretos:

1. `AgentOperationEnvelope` v1;
2. `AgentResponseEnvelope` v1;
3. JSON Schema de `semantic.classify.v1`;
4. JSON Schema de `constraint.suggest.v1`;
5. JSON Schema de `routing.failure.diagnose.v1`;
6. JSON Schema de `repair.plan.v1`;
7. JSON Schema de `block.review.v1`;
8. JSON Schema de `global.review.v1`;
9. JSON Schema de `adversarial.review.v1`;
10. `AgentOperationRegistry` contract;
11. `AgentProviderAdapter` contract;
12. retry/validation policy;
13. interaction log/replay schema.

Esses schemas devem ser tratados como APIs internas versionadas e cobertos por testes de contract.

---

## 42. Síntese

A interação ideal não é:

```text
"Aqui está a PCB. O que faço agora?"
```

É:

```text
Operation: routing.failure.diagnose.v1

Concise task preamble
      +
minimal structured facts
      +
constraints/findings/metrics relevant to this problem
      +
allowed action vocabulary
      +
strict response contract
      ↓
LLM reasoning
      ↓
validated structured proposal
      ↓
deterministic execution/search
      ↓
measured outcome
```

A IA fornece **engenharia, semântica, estratégia e diagnóstico**.

O engine fornece **geometria, routing, busca numérica, constraints, medição, transações e validade**.

Essa fronteira é o que permite usar um foundation model poderoso sem transformar a correção física da PCB em uma aposta sobre a resposta textual de um LLM.
