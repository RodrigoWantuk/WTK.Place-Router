# 09 — Decisões arquiteturais atuais e terminologia canônica

## 1. Objetivo

Este documento consolida decisões que já emergiram das discussões e corrige ambiguidades de nomenclatura existentes nos documentos iniciais.

Ele não substitui os documentos detalhados. Sua função é servir como **índice de decisões vigentes**, deixando claro o que já está aceito, o que ainda é provisório e quais nomes devem ser usados daqui em diante.

---

## 2. Status das decisões

### 2.1 Aceitas

- Produto desktop-first.
- Linguagem principal: C#.
- UI multiplataforma com Avalonia.
- MVVM restrito à Presentation Layer.
- `CommunityToolkit.Mvvm` como toolkit MVVM preferencial.
- Workspace no estilo CAD/IDE com docking real e painéis destacáveis.
- Família `Dock.Avalonia` como direção inicial para docking.
- Physical-design engine headless e independente da UI.
- Placement e routing tratados como um problema físico conjunto.
- Hard constraints validadas deterministicamente.
- IA sem autoridade para declarar a PCB válida.
- Toda interação com IA é uma operação tipada, versionada, auditável e com resposta estruturada.
- IA fora do inner loop numérico.
- DeepSeek como provider inicial de IA.
- `deepseek-v4-flash` como modelo default da primeira integração.
- Provider abstraction obrigatória: DeepSeek não deve aparecer no Domain nem no optimizer.

### 2.2 Provisórias / a validar experimentalmente

- PRDX como nome definitivo do formato canônico.
- LNS + Simulated Annealing como primeira combinação principal de search.
- Estratégia exata do detailed router.
- Granularidade do global-routing grid/corridor model.
- Uso automático de `deepseek-v4-pro` como escalonamento de raciocínio.
- Uso de provider-native tool calling dentro de uma operação de IA.
- Modelo de persistência interna de candidates/deltas.

### 2.3 Em aberto

- versão exata de .NET a fixar no início da implementação;
- engine in-process versus processo headless separado;
- política final de local-first/cloud execution;
- suporte inicial completo de import/export por EDA;
- biblioteca geométrica concreta;
- estratégia futura de SI/PI/thermal solvers;
- política comercial/licenciamento.

---

## 3. Terminologia canônica

Os primeiros documentos utilizaram `BoardState`, `PhysicalState` e `PhysicalDesignState` com sentidos próximos. A partir deste documento, a nomenclatura canônica é:

### 3.1 `Design`

Representa o projeto canônico lógico e físico configurável:

- board definition;
- stackup;
- components;
- footprints/pads;
- nets;
- groups;
- regions;
- constraints;
- semantic relationships;
- manufacturing profile;
- metadata/provenance.

`Design` não é um candidate específico da otimização.

### 3.2 `PhysicalDesignState`

É a fotografia física de um candidate em um instante.

Contém, conforme a fidelidade atual:

- component poses;
- tracks/routes;
- vias;
- layer assignments;
- provisional routes;
- reserved corridors;
- congestion/occupancy derived views;
- locks/frozen state;
- findings ligados ao candidate;
- métricas derivadas.

Este é o nome que deve substituir novos usos de `BoardState` e `PhysicalState`.

### 3.3 `PhysicalDesignTransaction`

Representa uma alteração experimental aplicada sobre um `PhysicalDesignState` para produzir outro candidate.

Exemplos:

```text
MoveComponent
RotateComponent
MoveGroup
RipRoute
RerouteNet
ChangeCorridor
ChangeLayerAssignment
```

A transaction possui diff, validação, métricas e resultado de commit/rollback.

### 3.4 `CandidateEvaluation`

Resultado determinístico da avaliação de um `PhysicalDesignState`.

Deve separar:

```text
Validity
Required violations
Preference costs
Optimization metrics
Routing metrics
Electrical proxies
Regression information
```

### 3.5 `Finding`

Problema, risco ou observação detectada por engine, heurística, usuário ou IA.

Finding não equivale automaticamente a hard violation.

### 3.6 `AgentOperation`

Uma única interação tipada com o provider de IA, definida em `08-Protocolo-de-Iteracoes-com-IA.md`.

Exemplos:

```text
semantic.classify.v1
constraint.suggest.v1
routing.failure.diagnose.v1
repair.plan.v1
block.review.v1
global.review.v1
```

---

## 4. Nomes antigos

Os seguintes termos encontrados nos documentos iniciais devem ser interpretados como aliases históricos:

```text
BoardState     → PhysicalDesignState
PhysicalState  → PhysicalDesignState
DesignTransaction → PhysicalDesignTransaction
```

Novos documentos e código não devem introduzir novamente os aliases antigos.

---

## 5. Arquitetura de alto nível vigente

```text
External EDA
    ↓
Design Exchange / Importers
    ↓
Canonical Design (PRDX concept)
    ↓
Constraint Workspace
    ↓
PhysicalDesignEnvironment
    │
    ├── Geometry / DRC / Constraints
    ├── Search / Optimization
    ├── Global + Detailed Routing
    ├── Verification / Regression
    └── Semantic views
    │
    ↕
Agent Orchestrator
    ↓
IA Provider Adapter
    ↓
DeepSeek initially
```

O Agent nunca contorna `PhysicalDesignEnvironment` para alterar geometria diretamente.

---

## 6. Arquitetura de aplicação

```text
Avalonia Views
      ↓
ViewModels (MVVM)
      ↓
Application / Coordinators
      ↓
Domain + Physical Design Engine
      ↓
Infrastructure adapters
```

Infrastructure adapters incluem:

- DeepSeek e futuros providers de IA;
- EasyEDA/KiCad/DSN/IPC import/export;
- persistence;
- logging/telemetry;
- eventual integration com solvers externos.

O Domain não deve referenciar Avalonia, DeepSeek, EasyEDA ou detalhes de transporte.

---

## 7. Provider inicial de IA

A decisão detalhada está em [`adr/0001-DeepSeek-como-Provider-Inicial.md`](adr/0001-DeepSeek-como-Provider-Inicial.md).

Resumo:

```text
Provider inicial       DeepSeek
Modelo default         deepseek-v4-flash
Endpoint preferido     API oficial DeepSeek
Formato de operação    JSON estruturado
Validação               JSON Schema local + semantic validation
Thinking                definido por operation policy
Provider abstraction    obrigatória
```

O modelo exato é configuração operacional, não parte do Domain.

---

## 8. Operação de IA: decisão inicial

Apesar de o documento `05` usar a linguagem de “tools”, a implementação inicial deve preferir um protocolo mais controlado:

```text
AgentOperation input
      ↓
DeepSeek
      ↓
Typed JSON response
      ↓
Schema validation
      ↓
Application authorization
      ↓
Deterministic engine action/transaction
```

Ou seja, inicialmente a IA **recomenda ou solicita ações estruturadas**. O orchestrator executa as ações autorizadas no engine.

Provider-native tool calling pode ser incorporado depois, mas não é requisito para o primeiro ciclo autônomo e não deve alterar os contratos internos.

---

## 9. Política de privacidade e credenciais

A escolha inicial de um provider cloud cria uma fronteira explícita de dados.

Regras:

1. API key nunca entra no PRDX ou no arquivo de projeto.
2. API key nunca é registrada em logs ou AgentOperation archives.
3. Credenciais devem vir de configuração segura do usuário, environment/secret store apropriado ou mecanismo equivalente.
4. O usuário deve conseguir identificar claramente qual provider/model está ativo.
5. O sistema deve saber quais dados de projeto serão enviados externamente numa AgentOperation.
6. O contexto enviado deve seguir o princípio de minimização: somente dados relevantes à operação.
7. Futuro provider local/offline deve poder usar o mesmo AgentOperation contract.

A existência de um provider cloud não altera a regra de que o engine determinístico e o projeto continuam funcionando sem IA.

---

## 10. Roadmap: dependências versus cronologia

O documento `06-Roadmap-e-Criterios-de-Sucesso.md` deve ser interpretado principalmente como **ordem de dependências técnicas**, não como obrigação de concluir toda a engine antes de tocar na GUI.

O shell Avalonia, docking, import visualization e primeiras telas de Constraint Workspace podem avançar assim que os contratos mínimos de `Design`, `PhysicalDesignState`, import e constraints estiverem estáveis.

Portanto, é aceitável trabalhar em paralelo:

```text
Workstream A — Core/Geometry/Design Model
Workstream B — Import/PRDX
Workstream C — Constraint Engine
Workstream D — Desktop Shell/Constraint Workspace
Workstream E — Routing/Search/Verification
Workstream F — Agent Protocol/DeepSeek integration
```

As dependências entre workstreams continuam obrigatórias; a cronologia não precisa ser estritamente linear.

---

## 11. Regra para novas decisões

Quando uma decisão mudar comportamento arquitetural ou criar dependência externa relevante, ela deve ser registrada como ADR.

Exemplos:

- versão de .NET;
- geometry library;
- engine in-process/out-of-process;
- formato PRDX v0.1;
- router algorithm baseline;
- persistence engine;
- provider/model routing policy.

Documentos conceituais descrevem o sistema; ADRs registram **por que uma escolha concreta foi tomada e em que status ela está**.
