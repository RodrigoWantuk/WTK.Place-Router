# 09 — Decisões Arquiteturais e Terminologia

## 1. Objetivo

Este documento consolida decisões arquiteturais que já foram discutidas e evita que documentos anteriores sejam interpretados como se todas as opções ainda estivessem igualmente abertas.

Ele também padroniza terminologia que apareceu com nomes diferentes durante a fase inicial de arquitetura.

A regra é:

- **Accepted** — direção arquitetural vigente;
- **Provisional** — direção atual, ainda sujeita a benchmark/ADR mais específico;
- **Open** — decisão propositalmente não fechada.

---

## 2. Estado físico canônico

### Accepted

Nome canônico:

```text
PhysicalDesignState
```

Representa o estado físico conjunto de placement + routing sobre o qual optimizer, transactions, verification e UI operam.

Termos históricos encontrados nos documentos:

```text
BoardState
PhysicalState
```

Devem ser interpretados como referências conceituais antigas ao mesmo tipo de estado, salvo quando o contexto indicar explicitamente um snapshot/modelo diferente.

Novos contracts/classes devem usar `PhysicalDesignState`.

### Transaction

Nome canônico:

```text
PhysicalDesignTransaction
```

`DesignTransaction` pode permanecer como abreviação textual, mas a API principal deve usar o nome completo quando houver risco de ambiguidade.

---

## 3. Stack de produto

### Accepted

```text
Primary language       C#
Runtime                .NET
Application type       Desktop-first
UI framework           Avalonia
Presentation pattern   MVVM
MVVM toolkit           CommunityToolkit.Mvvm
Docking                Dock.Avalonia family
Engine                 Headless / platform-agnostic
```

A versão exata de .NET/Avalonia/Dock é uma decisão de implementação a ser fixada no bootstrap do código usando versões estáveis/suportadas naquele momento.

---

## 4. Fronteiras arquiteturais

### Accepted

MVVM é padrão somente da Presentation Layer.

A aplicação segue conceitualmente:

```text
Presentation (Avalonia/MVVM)
          ↓
Application / Coordinators
          ↓
Domain + deterministic engines
          ↓
Infrastructure / adapters
```

Princípios:

- ViewModels não executam geometry/routing/LLM diretamente;
- Domain não referencia Avalonia;
- EDA adapters não contaminam o canonical model;
- AI providers são Infrastructure;
- CLI/tests usam o mesmo Application/Core da GUI.

---

## 5. Padrões de projeto

### Accepted

Usar patterns onde resolvem um problema concreto, sem transformar o projeto em uma implementação ritualística de GoF/Clean Architecture.

#### Strategy

Para algoritmos substituíveis/benchmarkáveis:

```text
route search strategy
placement search strategy
spatial index strategy
geometry kernel adapter
candidate scoring strategy
```

#### Transaction / Command-like actions

Mudanças físicas devem ser actions tipadas dentro de `PhysicalDesignTransaction`.

Isso sustenta:

- undo/redo;
- replay;
- audit;
- candidate isolation;
- regression;
- explainability.

#### Data-driven constraints + evaluator registry

Constraints são principalmente dados tipados com evaluators registrados, evitando uma árvore OO gigantesca para cada regra trivial.

#### Composite

Groups hierárquicos.

#### Domain events

Para invalidation/recomputation/review triggers, sem depender de um framework pesado de event bus.

---

## 6. Interface

### Accepted

Arquitetura visual no estilo CAD/IDE:

```text
TitleBar
Toolbar
DockControl
StatusBar
```

Workspace default:

```text
Design Navigator | Board Workspace | Constraint Composer + Inspector
                         |
                  Bottom Workbench
```

Painéis auxiliares são Tool docks destacáveis, redimensionáveis e multi-monitor.

Board/document surfaces usam DocumentDock.

O `PcbViewportControl` usa rendering especializado; entidades físicas não são milhares de Avalonia Controls.

---

## 7. Provider de IA

### Accepted

Provider inicial:

```text
DeepSeek
```

A escolha é operacional, não arquitetural.

O Place&Router usa uma abstração provider-agnostic.

Model/provider usados em cada run devem ser registrados para replay/benchmark.

Detalhes no ADR-0001.

---

## 8. Protocolo de IA

### Accepted

Toda chamada é uma `AgentOperation` tipada/versionada.

Formato conceitual:

```text
Stable policy
+ concise operation preamble
+ minimal structured context
+ strict response contract
```

A IA fica fora do numerical inner loop.

A resposta passa por:

```text
schema validation
→ semantic validation
→ action authorization
→ deterministic preconditions
→ candidate transaction
```

A IA nunca declara DRC/final validity.

---

## 9. Processamento local e estratégia algorítmica

### Accepted — direção

O engine local deve preferir teoria consolidada e bibliotecas maduras antes de custom algorithms e antes de cloud reasoning.

Ordem:

```text
known deterministic algorithm / mature library
        ↓
small Place&Router composition/adaptation
        ↓
custom algorithm only after benchmark evidence
        ↓
LLM only for semantic/strategic ambiguity
```

### Geometry

Accepted direction:

- canonical physical coordinates em `Int64`;
- unidade inicial proposta de 1 µm;
- `IGeometryKernel` abstrato;
- Clipper2 como strong candidate para clipping/offsetting/boolean geometry;
- broad-phase spatial index separado do exact geometry test;
- NetTopologySuite Quadtree como candidate inicial de índice mutável.

### Placement

Accepted direction:

```text
fixed/mechanical-first
→ graph/region-oriented coarse seed
→ legalization
→ fast multi-fidelity evaluation
→ LNS
→ Simulated Annealing refinement
```

LNS é a estrutura principal de reabertura de neighborhoods; SA é o mecanismo inicial para exploração/escape de mínimos locais.

### Routing

Accepted direction:

```text
pin access
→ global routing / resource reservation
→ detailed routing
→ negotiated rip-up/reroute
→ placement repair when routing evidence requires it
```

- coarse capacity grid por layer;
- HPWL seguido de RMST/RSMT quando maior fidelidade for necessária;
- FLUTE/equivalente como candidate de RSMT rápido;
- A*/Dijkstra no global routing;
- negotiated congestion PathFinder-like;
- A* 2.5D como detailed route search inicial;
- Hadlock como Strategy/benchmark futuro;
- obstacle inflation para clearance;
- exact DRC pós-rota.

### Constraint pre-solving

OR-Tools CP-SAT é candidate opcional apenas para pequenos subproblemas discretos bem delimitados. Não é o placement/geometry engine principal.

### Benchmark-gated

Ainda não são escolhas finais:

- grid resolution;
- spatial index definitivo;
- uso definitivo de FLUTE;
- SA schedule;
- LNS sizes;
- score weights/normalization;
- routing→placement escalation threshold;
- native geometry acceleration;
- CP-SAT adoption em produção.

Detalhes em `10-Processamento-Local-e-Algoritmos-Deterministicos.md` e ADR-0003.

---

## 10. Princípio de redução de input do usuário

### Accepted

A UX segue:

```text
IMPORT
→ DERIVE DETERMINISTICALLY
→ APPLY PROFILE DEFAULTS
→ INFER/SUGGEST
→ ASK USER ONLY IF MATERIAL
```

O sistema não deve exigir preenchimento em massa de propriedades apenas porque o schema possui campos opcionais.

`Unknown` é válido.

Uma pergunta é mostrada quando o dependency analysis indicar que aquela ausência bloqueia ou degrada materialmente uma decisão atual.

---

## 11. Credenciais, privacidade e cloud

### Accepted

Credenciais de provider:

- não entram em PRDX;
- não entram em project JSON;
- não aparecem em logs normais;
- ficam em configuração segura/local apropriada à plataforma.

O contexto enviado à cloud deve ser minimizado pela operação e registrado/auditável conforme policy do produto.

A UI deve deixar claro:

- provider/model ativo;
- quando uma operação cloud está sendo usada;
- quando uma ação é totalmente local.

Um provider local/offline futuro deve poder substituir DeepSeek sem alterar Domain/AgentOperation contracts.

---

## 12. Interoperabilidade

### Accepted

- canonical model interno desacoplado do EDA;
- PRDX como nome provisório do formato/modelo persistível;
- EasyEDA como primeiro adapter prático provável;
- arquitetura preparada para KiCad/Specctra/IPC-2581/outros;
- round-trip export é objetivo arquitetural.

### Provisional

O formato exato PRDX v0.1 ainda precisa de JSON Schema formal.

---

## 13. Joint placement/routing

### Accepted

Não existe top-level `PlacementEngine → RoutingEngine → Done`.

Routing pode reabrir placement.

Placement deve consumir feedback de routability/corridors antes de routing final.

A abstração superior trabalha sobre o mesmo `PhysicalDesignState`.

---

## 14. Validade versus qualidade

### Accepted

```text
Hard constraints → validity
Preferences       → cost
Goals             → optimization metric
```

Nenhum score positivo compensa uma Required violation.

---

## 15. Roadmap como dependências, não waterfall rígido

### Accepted

As fases do documento `06` descrevem principalmente **ordem de dependência técnica**, não uma obrigação de terminar todo o engine antes de começar UI/Agent infrastructure.

Workstreams podem avançar em paralelo quando contracts mínimos estiverem estáveis.

Exemplo:

```text
Core/Geometry ───────────────→ Routing/Search
      │
      ├────→ PRDX/Importer
      │         │
      │         └────→ Constraint Workspace UI
      │
      └────→ Domain contracts ─────→ Desktop shell/UI

Agent protocol/provider adapter pode ser implementado/testado com fixtures
antes de o optimizer final existir, mas não deve substituir engine capabilities.
```

---

## 16. Open decisions

Ainda propositalmente abertas:

- versão final de .NET/Avalonia na primeira solução;
- package names finais;
- formato PRDX v0.1 completo;
- first EasyEDA handoff concreto;
- score normalization final;
- thermal/SI/PI solver depth;
- provider/model routing depois dos benchmarks iniciais;
- política comercial/licenciamento geral do projeto;
- eventual engine out-of-process versus in-process;
- search avançado/MCTS/ML.

---

## 17. ADRs

- `ADR-0001` — DeepSeek como provider inicial;
- `ADR-0002` — Stack desktop e fronteiras arquiteturais;
- `ADR-0003` — Processamento local e estratégia algorítmica.

Novas decisões concretas que alterem architecture/invariants devem ganhar ADR próprio em vez de ficarem somente em conversa ou comentários de código.
