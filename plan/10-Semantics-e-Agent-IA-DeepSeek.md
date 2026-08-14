# PLAN-10 — Semantics e Agent IA DeepSeek

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-05 e PLAN-08 concluídos  
**Pode avançar em paralelo a:** PLAN-09 e PLAN-11  
**Participa de:** PLAN-12

---

## 1. Instrução ao agente

Você está implementando a camada de reasoning do WTK.Place&Router. O engine determinístico já deve conseguir importar, avaliar, posicionar, rotear, diagnosticar failures e executar repairs sem depender do LLM. A IA entra agora para reduzir input humano, interpretar semântica, escolher foco de otimização, diagnosticar situações ambíguas e revisar resultados — **nunca para substituir geometria, DRC ou numerical search**.

Antes de codificar:

1. leia `/AGENTS.md` e `/plan/00-ROADMAP-MESTRE-V0.1.md`;
2. confirme que PLAN-05 e PLAN-08 estão funcionais;
3. leia este plano inteiro;
4. leia todos os documentos/ADRs obrigatórios;
5. preserve `AgentOperation` versionada e provider abstraction;
6. implemente primeiro com provider fake/recorded fixtures e depois conecte DeepSeek;
7. nunca coloque API key em PRDX, fixture, log ou commit.

### Documentos obrigatórios

- `docs/01-Interoperabilidade-e-Modelo-Canonico.md`
- `docs/02-Interface-e-Constraint-Authoring.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/05-Agente-IA-Revisao-e-Memoria.md`
- `docs/08-Protocolo-de-Iteracoes-com-IA.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/adr/0001-DeepSeek-como-Provider-Inicial.md`
- PLAN-05 e PLAN-08

---

## 2. Objetivo mensurável

Ao final, a aplicação deve conseguir executar operações tipadas como:

```text
semantic.classify.v1
constraint.suggest.v1
floorplan.strategy.v1
optimization.focus.select.v1
routing.failure.diagnose.v1
repair.plan.v1
candidate.compare.v1
block.review.v1
global.review.v1
adversarial.review.v1
```

com fluxo:

```text
Deterministic context builder
→ minimal AgentOperation JSON
→ provider adapter
→ strict JSON response
→ schema validation
→ semantic validation
→ authorization
→ optional deterministic action/transaction
→ measured outcome
→ run/audit log
```

---

## 3. Semantic graph operacional

Antes da cloud, consolidar modelo/runtime para:

- semantic roles de component/net/pad;
- functional blocks;
- relationships;
- provenance/confidence;
- user confirmation/rejection;
- relationship status candidate/confirmed/rejected quando necessário.

Relações mínimas úteis:

```text
decouples
feedback-network-of
switching-output-of
power-rail-of
sensitive-to
aggressor-to
clock-of
differential-pair-member
kelvin-sense-of
return-path-related
functional-block-member
```

Não force taxonomia enorme; entregue relações consumidas pelos workflows da v0.1.

---

## 4. Deterministic semantic enrichment primeiro

Reutilizar PLAN-05 e ampliar somente onde necessário:

```text
names/topology/import metadata
→ deterministic candidates
→ only unresolved/high-value cases go to AI
```

Nunca chamar cloud para reconhecer algo que regra local simples já resolveu com segurança adequada.

---

## 5. AgentOperation contracts

Criar contracts formais para request/response.

Envelope request comum deve suportar:

```text
operation
schemaVersion
requestId
runId
projectId/projectRevision
designStateId/revision
objective
focus
facts
constraints
metrics
findings
semanticContext
historySummary
availableActions
budgets
responseContract
```

Nem todo campo precisa estar presente em toda operation; usar views específicas e compactas.

### Regra crítica

`facts` determinísticos e `semanticContext` inferido devem permanecer separados.

---

## 6. Response contracts e JSON Schemas

Criar schemas versionados para as operações v0.1 implementadas.

Resposta comum deve cobrir quando aplicável:

```text
status
summary
actions/findings
confidence
evidenceRefs
assumptions
missingInformation
nextOperationHint
```

Não solicitar/store chain-of-thought. `summary` é justificativa curta e auditável.

Validar resposta antes de qualquer action.

---

## 7. Provider abstraction

Implementar boundary equivalente a:

```text
IAiProvider
AgentProviderRequest
AgentProviderResponse
ProviderCapabilities
```

Domain/Application não devem conhecer payload proprietário DeepSeek.

Implementações mínimas:

```text
Fake/RecordedAiProvider
DeepSeekAiProvider
```

Pode existir `NullAiProvider` se útil para modo local/offline.

---

## 8. ModelPolicy

Não espalhar nome de modelo pelo código.

Criar policy lógica semelhante a:

```text
FAST
STANDARD_REASONING
DEEP_REASONING
```

Mapeamento inicial segue ADR-0001 e configuração atual do projeto/provider.

Operation definition escolhe policy, não string de modelo hardcoded em ViewModel.

Permitir configurar thinking/non-thinking quando suportado pelo adapter e pela policy vigente.

---

## 9. DeepSeek adapter

Implementar adapter usando API oficial vigente conforme ADR/documentação atual no momento da execução.

Requisitos:

- API key via secret/config local seguro ou environment variável apropriada;
- base URL/model configuráveis em Infrastructure settings;
- timeout/cancellation;
- retry somente para falhas transitórias/idempotentes;
- response JSON-only/structured mechanism quando disponível;
- usage/token/cost metadata quando provider retornar;
- thinking mode conforme ModelPolicy;
- logs sem secret;
- response raw opcional apenas em run/debug store seguro quando policy permitir.

Não tornar provider-native tool calling obrigatório na v0.1; o protocolo principal é request JSON → response JSON → validação local.

---

## 10. Operation registry

Criar definitions versionadas por operation:

```text
operation id/version
concise preamble
input context builder
response schema
model policy
allowed action types
retry policy
context budget
escalation policy
```

Não usar um prompt universal “PCB engineer” para tudo.

---

## 11. Context builders

Cada operation deve construir contexto mínimo relevante.

Exemplo `routing.failure.diagnose.v1`:

- RouteFailure factual;
- local blockers;
- required/available passage;
- constraints afetadas;
- related nets/components;
- local congestion;
- recent repair summary.

Não enviar automaticamente board inteiro.

### Context escalation

Implementar níveis aproximados:

```text
L1 entity/local neighborhood
L2 related nets + local routing/congestion
L3 functional block
L4 wider board summary
```

Escalar somente quando response `NEEDS_INFORMATION`/policy indicar necessidade.

---

## 12. Validation pipeline

Toda response passa por:

### 1. Schema validation

JSON e contract correto.

### 2. Semantic validation

- IDs existem;
- confidence/ranges válidos;
- evidenceRefs existem;
- requested operation/action faz sentido no estado.

### 3. Authorization

- action está em `availableActions`;
- target não está user-locked quando action exige move;
- provider não pode alterar secrets/project lifecycle;
- IA não recebe autoridade para aceitar hard violation.

### 4. Deterministic preconditions

Engine verifica viability antes de candidate transaction.

---

## 13. Retry policy

Retry não pode virar loop infinito.

Casos:

- invalid JSON/schema → one compact corrective retry;
- missing context justificável → context escalation conforme budget;
- transient provider error → bounded technical retry;
- sem solução/low confidence → retornar unresolved/finding, não insistir indefinidamente.

Registrar cada tentativa na run.

---

## 14. Operações v0.1 obrigatórias

### `semantic.classify.v1`

Classifica roles/relationships não resolvidos localmente.

Saída sempre candidate/inferred até confirmação/policy.

### `constraint.suggest.v1`

Sugere constraints suportadas pelo registry, com reason/confidence/evidence.

Não cria Required rule silenciosamente; UI/user/policy deve aceitar.

### `optimization.focus.select.v1`

Recebe metrics/findings/failures e escolhe neighborhood/focus macro.

Engine executa search local.

### `routing.failure.diagnose.v1`

Interpreta RouteFailure estruturado e classifica causa/repair direction.

### `repair.plan.v1`

Propõe classes de repair usando apenas actions permitidas.

Engine gera/testa candidates.

### `candidate.compare.v1`

Somente quando trade-off não está totalmente definido matematicamente; recebe métricas exatas e interpreta.

### `block.review.v1` e `global.review.v1`

Geram findings sem modificar state.

`adversarial.review.v1` pode entrar na v0.1 se custo/complexidade não bloquear os obrigatórios acima; caso contrário fica habilitado como operação registrada para PLAN-12 completar.

---

## 15. AI Suggestions UI

Integrar ao Constraint Workspace/Inspector/Bottom Workbench:

- tab/section `Suggestions`;
- mostrar operation/source/model/confidence;
- evidence/summary curta;
- Accept/Modify/Reject;
- accepted suggestion vira UserConfirmed/UserDefined ou provenance apropriada;
- rejected suggestion deve poder evitar reapresentação idêntica na mesma revision/context.

Não transformar o produto em chatbot.

---

## 16. Optimization integration

Integrar Agent com PLAN-08 somente em macro-iterations.

Exemplo:

```text
optimizer repeatedly fails hotspot
→ optimization.focus.select or routing.failure.diagnose
→ AI chooses neighborhood/repair class
→ deterministic LNS/SA/router tests many candidates locally
→ measured result returned
```

Nunca chamar LLM a cada candidate/move/A* expansion.

---

## 17. Reviews

Implementar ao menos:

- functional block review;
- whole-board global review;
- repair review quando triggered.

Reviews produzem `Finding` com:

- severity/recommendation;
- entity refs;
- evidence refs;
- confidence;
- source operation/model;
- status active/resolved/rejected.

Review AI não declara DRC pass/fail final.

---

## 18. Audit/run logging

Registrar por call:

```text
operation/request/run IDs
project/state revision
provider/model/policy
prompt/operation version
input hash
structured input/output
validation result
proposed actions
executed actions
deterministic outcome
latency
tokens/cost when available
```

Sensitive data policy deve ser respeitada.

Large history pertence a `.prdxrun`/run store, não ao PRDX principal.

---

## 19. Replay/benchmark harness

Criar harness capaz de rodar recorded AgentOperation fixture contra:

- Fake recorded response;
- DeepSeek real quando secret disponível;
- future provider sem alterar operation contract.

Medir:

- schema success rate;
- retry rate;
- latency;
- tokens/cost;
- action acceptance;
- repair success after proposal;
- regressions after proposed strategy.

Não requer benchmark massivo neste plano.

---

## 20. Failure/offline behavior

Sem internet/provider key:

- aplicação continua funcional localmente;
- AI features mostram unavailable/disabled;
- optimizer/router continuam executáveis;
- nenhuma feature local básica deve crashar por ausência de DeepSeek.

---

## 21. Testes mínimos

1. operation request serializa conforme schema;
2. invalid AI response é rejeitada antes de action;
3. nonexistent target ID falha semantic validation;
4. locked component action falha authorization/precondition;
5. Fake provider executa semantic/repair flow end-to-end;
6. context builder não envia board inteiro em local failure fixture;
7. one retry corrige malformed response fixture ou encerra bounded;
8. accepted constraint suggestion persiste provenance correta;
9. AI unavailable não quebra optimizer local;
10. optional integration test DeepSeek somente quando secret existir, sem fazê-lo requisito do build normal.

---

## 22. Fora de escopo

- fine-tuning;
- autonomous unrestricted tool loop;
- provider-specific types no Domain;
- IA calculando coordenadas/DRC como autoridade;
- design memory sofisticada/vector DB;
- MCTS/ML routing.

---

## 23. Critérios de aceitação

Plano concluído quando:

- semantic graph e provenance estão operacionais;
- AgentOperation contracts/schemas existem;
- Fake + DeepSeek provider adapters existem;
- context é minimal/operation-specific;
- response passa por schema/semantic/authorization/deterministic validation;
- operations obrigatórias executam;
- suggestions/reviews aparecem na UI;
- optimizer usa IA apenas em macro-decisions;
- no-provider mode funciona;
- audit/replay básico existe;
- build/test alvo passa.

### Demonstração mensurável

Executar um caso:

```text
Detailed routing returns PLACEMENT_BLOCKAGE
→ routing.failure.diagnose.v1
→ AI proposes REQUEST_LOCAL_OPTIMIZATION on blockers
→ deterministic optimizer tests candidates
→ one repair succeeds
→ result/metrics logged
```

E demonstrar que desligar DeepSeek mantém o mesmo projeto abrindo, validando, roteando e otimizando localmente.

---

## 24. Relatório final

Informar operations implementadas, ModelPolicy/provider mapping, validation/retry behavior, context sizes aproximados, UI suggestions/reviews, fake/real provider validations e segurança de secrets.