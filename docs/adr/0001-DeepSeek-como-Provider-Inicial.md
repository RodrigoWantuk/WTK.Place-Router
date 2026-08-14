# ADR-0001 — DeepSeek como provider inicial de IA

**Status:** Accepted  
**Data:** 2026-08-14

## Contexto

O Place&Router precisa de um provider de IA para validar o protocolo definido em `08-Protocolo-de-Iteracoes-com-IA.md` e implementar as primeiras operações de reasoning, semantic enrichment, diagnóstico e review.

A prioridade inicial não é encontrar o modelo absolutamente mais forte disponível. É validar:

- AgentOperation contracts;
- JSON input/output;
- schema validation;
- context minimization;
- reasoning fora do inner loop;
- integração com deterministic engine;
- logging/replay;
- custo e latência por operação;
- capacidade real de melhorar decisões de physical design.

Experimentos anteriores do ecossistema WTK indicaram que DeepSeek oferece uma relação custo/qualidade adequada para iniciar essa validação.

## Decisão

Usar **DeepSeek como provider inicial** da camada de IA.

Configuração inicial:

```text
Provider         DeepSeek
Default model    deepseek-v4-flash
API              official DeepSeek API
Transport        OpenAI-compatible Chat Completions initially
JSON mode        enabled for structured operations
Thinking         selected per operation policy
Schema authority local Place&Router validation
```

O provider será encapsulado por uma abstração própria do Place&Router.

Nenhum tipo de Domain, Geometry, Routing, Search, Verification ou Constraint deve depender diretamente de classes/DTOs da API DeepSeek.

## Model routing inicial

Não criar complexidade prematura de multi-model routing.

Primeira política:

```text
all initial AgentOperations
    → deepseek-v4-flash
```

A própria operation define se thinking está habilitado.

Política conceitual futura:

```text
FAST
  → V4 Flash / non-thinking

STANDARD_REASONING
  → V4 Flash / thinking

DEEP_REASONING
  → benchmark before deciding whether V4 Pro is justified
```

`deepseek-v4-pro` não é requisito para a primeira versão e não deve ser usado automaticamente antes de benchmarks do próprio Place&Router demonstrarem vantagem relevante.

## JSON Output

O adapter deve solicitar JSON Output quando a operação exigir resposta estruturada.

Mesmo com JSON Output habilitado:

1. a resposta passa por parse local;
2. a resposta passa pelo JSON Schema do `responseContract`;
3. IDs, enums e referências passam por semantic validation;
4. ações passam por authorization validation;
5. somente depois podem originar uma `PhysicalDesignTransaction` ou outra ação do Application Layer.

JSON válido não significa resposta semanticamente válida.

## Structured contracts

O contrato interno pertence ao Place&Router.

Exemplo:

```text
repair.plan.v1
    ↓
repair.plan.response.v1
```

O adapter traduz esse contrato para os recursos disponíveis no provider.

A aplicação não deve depender de funcionalidades Beta de schema enforcement para garantir integridade.

Provider-native strict/tool schema pode ser testado futuramente, mas a validação local permanece obrigatória.

## Tool calling

A primeira integração não exige que DeepSeek execute um loop autônomo de provider-native tool calls.

Fluxo inicial preferido:

```text
Application builds AgentOperation
      ↓
DeepSeek returns typed JSON decision
      ↓
Application validates response
      ↓
Application/engine executes authorized action
      ↓
Deterministic result becomes input to a later AgentOperation if reasoning is needed again
```

Isso mantém controle, replay e compatibilidade entre providers.

Provider-native tool calling poderá ser adicionado como optimization/feature posterior sem mudar os contracts internos.

## Thinking mode

Thinking é uma propriedade da `AgentOperationPolicy`, não uma escolha feita pelo Domain.

Exemplos conceituais:

```text
semantic.classify.v1
  thinking = disabled

constraint.suggest.v1
  thinking = disabled or benchmarked

routing.failure.diagnose.v1
  thinking = enabled

repair.plan.v1
  thinking = enabled

global.review.v1
  thinking = enabled
```

A política final será definida por benchmark.

O Place&Router não deve persistir nem depender do reasoning privado do modelo. Para auditoria são suficientes:

- input factual;
- final structured response;
- summary/evidence references;
- action executed;
- deterministic outcome.

## Provider abstraction

Interface conceitual:

```text
IAgentProvider
    ExecuteAsync(
        AgentOperationRequest request,
        AgentOperationDefinition definition,
        CancellationToken cancellationToken)
```

Implementações possíveis:

```text
DeepSeekAgentProvider
OpenAIAgentProvider       future
AnthropicAgentProvider    future
LocalAgentProvider        future
FakeAgentProvider         tests
ReplayAgentProvider       tests/benchmarks
```

O objetivo é poder repetir o mesmo benchmark mudando apenas provider/model.

## Credenciais

A API key:

- não entra no PRDX;
- não entra em project JSON;
- não entra em transaction logs;
- não entra em AgentOperation input archives;
- nunca deve aparecer em diagnostics.

A implementação deverá obter segredo através de configuração segura de usuário, environment/secret storage ou mecanismo equivalente apropriado à plataforma.

## Privacidade e transferência de dados

DeepSeek é inicialmente um provider remoto. Portanto, AgentOperations podem enviar dados do projeto para um serviço externo.

A aplicação deve:

- exibir provider/model ativo;
- minimizar o contexto enviado;
- enviar somente a view necessária para cada operação;
- manter provenance do que foi enviado/recebido nos logs técnicos, sem segredos;
- permitir futura substituição por provider local usando os mesmos contracts.

O uso do provider de IA nunca é necessário para abrir, preservar ou validar deterministicamente uma PCB.

## Observabilidade

Por AgentOperation registrar:

```text
provider
model
thinking mode
operation/version
prompt-policy version
request/response schema version
input hash
output hash
latency
token usage
estimated/reported cost
validation result
executed action
deterministic outcome
```

Isso permitirá comparar DeepSeek com outros providers de forma objetiva.

## Consequências positivas

- baixo custo inicial;
- capacidade de validar muitas AgentOperations;
- JSON Output e tool calling disponíveis no provider;
- thinking/non-thinking configuráveis;
- baixa barreira para experimentação;
- arquitetura continua provider-agnostic.

## Riscos

- qualidade pode variar por tipo de tarefa de engenharia;
- JSON válido pode não cumprir semanticamente o contract;
- reasoning pode ser insuficiente em reviews complexos;
- API/provider é dependência externa e pode mudar;
- projetos podem conter informação sensível enviada ao provider remoto.

Mitigações:

- schema/semantic validation;
- deterministic authority;
- benchmark por operation;
- replay;
- provider abstraction;
- context minimization;
- futura opção local/offline.

## Critério de reconsideração

Reavaliar a decisão quando:

- benchmarks mostrarem baixa taxa de sucesso em operações importantes;
- outro modelo produzir ganho relevante de repair/review por custo aceitável;
- requisitos de privacidade exigirem processamento local;
- mudanças de API/preço tornarem DeepSeek inadequado;
- uma classe de operation justificar model routing especializado.

A troca de provider não deve exigir alteração do Domain nem do physical-design engine.

## Referências externas verificadas na data da decisão

Na data deste ADR, a documentação oficial do DeepSeek lista `deepseek-v4-flash` e `deepseek-v4-pro` como modelos de API, ambos com JSON Output, tool calls e thinking/non-thinking. Os nomes legados `deepseek-chat` e `deepseek-reasoner` foram retirados do fluxo recomendado. Detalhes operacionais devem sempre ser verificados novamente antes da implementação, pois são dependência externa mutável.
