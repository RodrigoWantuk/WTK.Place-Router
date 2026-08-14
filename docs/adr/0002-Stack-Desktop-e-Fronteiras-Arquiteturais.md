# ADR-0002 — Stack desktop e fronteiras arquiteturais

**Status:** Accepted  
**Data:** 2026-08-14

## Contexto

O Place&Router será uma aplicação de engenharia com características de CAD/EDA:

- canvas gráfico complexo;
- interação intensiva com seleção/zoom/pan;
- docking e multi-monitor;
- processamento pesado;
- projeto local persistente;
- optimizer headless;
- formatos EDA externos;
- provider de IA substituível.

A arquitetura precisa preservar a produtividade de uma aplicação desktop sem acoplar o physical-design engine à UI.

## Decisão

### Linguagem

C# será a linguagem principal do produto.

A versão exata de .NET será fixada no início da implementação em ADR próprio ou revisão deste ADR, usando uma versão estável/suportada naquele momento.

### UI

```text
Framework       Avalonia
Pattern         MVVM
MVVM toolkit    CommunityToolkit.Mvvm
Docking         Dock.Avalonia family
```

### Fronteira principal

MVVM é arquitetura da **Presentation Layer**, não arquitetura do physical-design engine.

```text
Avalonia Views
      ↓
ViewModels
      ↓
Application / Coordinators
      ↓
Domain + Physical Design Engine
      ↓
Infrastructure Adapters
```

O Domain/Engine não referencia:

- Avalonia;
- Dock.Avalonia;
- Window/Control;
- DeepSeek;
- EasyEDA/KiCad-specific DTOs;
- APIs de SO sem adapter explícito.

## Arquitetura geral

Usar uma abordagem **domain-oriented + Ports & Adapters**, sem aplicar Clean Architecture de forma ritualística.

Objetivo:

- dependências apontam para o domínio;
- fronteiras externas usam adapters;
- abstrações existem onde há variação/isolamento real;
- evitar interfaces artificiais para classes que não precisam ser substituídas.

## Modules conceituais

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
PlaceRouter.Application
PlaceRouter.App
PlaceRouter.Cli
PlaceRouter.Tests
PlaceRouter.Benchmarks
```

Os nomes finais de assemblies podem mudar; as responsabilidades não devem colapsar todas no projeto de UI.

## Patterns indicados

### Strategy

Usar quando o algoritmo pode variar de forma real.

Exemplos:

```text
IRoutingStrategy
IGlobalRoutingStrategy
IPlacementSearchStrategy
ICandidateScorer
ICongestionEstimator
```

Implementações possíveis:

```text
AStarRouter
MazeRouter
NegotiatedCongestionRouter

LnsSearch
AnnealingSearch
```

Não criar Strategy para operações que nunca terão implementação alternativa plausível.

### Transaction / Command-oriented actions

Alterações físicas devem ser representáveis como ações estruturadas dentro de `PhysicalDesignTransaction`.

Exemplos:

```text
MoveComponent
RotateComponent
MoveGroup
RipRoute
RerouteNet
ChangeLayerAssignment
```

Isso fornece naturalmente:

- diff;
- replay;
- undo/redo;
- audit;
- regression;
- commit/rollback;
- Agent action validation.

Não é necessário implementar o GoF Command literalmente em toda classe; o requisito é possuir ações tipadas e transacionáveis.

### Data-driven Constraint Evaluators

Constraints devem preferir:

```text
Constraint data
      +
registered deterministic evaluator
```

em vez de criar uma hierarquia OO enorme com uma classe específica para cada combinação simples de regra.

Specification-like composition pode ser usada onde agregar valor, especialmente para selectors e predicates.

### Composite

Adequado para grupos hierárquicos:

```text
POWER
 ├── BUCK
 └── LDO
```

ComponentGroup/NetGroup/FunctionalGroup devem permitir composição sem duplicar constraints em todos os membros.

### Domain Events / Observer-like eventing

Mudanças relevantes podem publicar eventos internos tipados:

```text
ComponentMoved
RouteChanged
NetRouted
ConstraintChanged
FindingOpened
TransactionCommitted
```

Esses eventos alimentam:

- incremental recomputation;
- event-driven reviews;
- UI projections;
- logging;
- cache invalidation.

Preferir um event bus interno simples e explícito antes de adotar frameworks pesados.

### State como lifecycle explícito

Candidates, findings, runs e transactions possuem estados claros, porém não precisam necessariamente implementar o GoF State Pattern.

Exemplos:

```text
Candidate: Draft → Evaluating → Accepted/Rejected
Finding: Open → UnderRepair → Resolved/AcceptedRisk
Run: Ready → Running → Paused → Completed/Failed
```

## Domain model versus acceleration structures

Separar a representação canônica do design das estruturas derivadas para performance.

Exemplo:

```text
Canonical Domain
  Components
  Nets
  Routes
  Constraints

Derived runtime structures
  SpatialIndex
  CongestionGrid
  ConnectivityIndex
  ConstraintIndex
  DependencyGraph
  OccupancyMap
```

As estruturas derivadas podem ser descartadas/reconstruídas e não definem a semântica do projeto.

## PCB Viewport

O canvas da PCB deve ser custom-rendered.

Evitar representar cada elemento como um `Control` Avalonia independente:

```text
TrackControl × thousands
ViaControl × hundreds
PadControl × thousands
```

Preferência:

```text
PcbViewportControl
      ↓
rendering specialized by viewport/layer/state
```

Views/ViewModels controlam estado e interação; o renderer trabalha com geometria/projeções apropriadas.

Uma abstração de renderer pode permitir evolução futura sem alterar o Domain.

## Headless engine

O mesmo engine deve ser executável por:

```text
Desktop
CLI
Tests
Benchmarks
future automation/server host
```

A GUI não é requisito para importar, validar, otimizar ou testar um design.

## In-process versus out-of-process

Permanece em aberto.

A primeira implementação pode começar in-process por simplicidade, desde que a Application Layer não dependa dessa decisão.

Razões futuras para separar processo:

- isolamento de crashes;
- long-running optimization;
- resource limits;
- server/remote execution;
- multiple UI clients.

Essa decisão será tomada depois de existirem contracts estáveis e profiling real.

## Testabilidade

ViewModels devem ser testáveis sem criar Window real sempre que possível.

O engine deve possuir testes independentes da UI.

Categorias esperadas:

- unit tests de geometry/domain;
- constraint evaluator tests;
- importer round-trip tests;
- transaction/regression tests;
- routing/search benchmarks;
- AgentOperation contract tests;
- Avalonia headless UI tests;
- docking/layout persistence tests;
- visual QA.

## Consequências

### Positivas

- stack alinhado ao perfil desktop/CAD do produto;
- multiplataforma sem exigir frontend web;
- core reutilizável por CLI/tests/cloud futuro;
- arquitetura próxima à experiência já adquirida com MediaForge;
- baixo acoplamento com EDA/provider/UI.

### Riscos

- Avalonia/docking exigem QA real em Windows/Linux/macOS;
- canvas complexo precisará de profiling e possivelmente renderer especializado;
- excesso de abstrações pode atrasar protótipo se Ports & Adapters for aplicado de forma dogmática.

Mitigação principal:

> abstrair fronteiras reais; manter implementação simples dentro de cada módulo até existir evidência de que maior generalização é necessária.
