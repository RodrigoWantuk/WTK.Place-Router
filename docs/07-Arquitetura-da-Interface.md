# 07 — Arquitetura da Interface Desktop

## 1. Objetivo

Este documento define a direção arquitetural da interface desktop do WTK.Place&Router.

Ele complementa [`02-Interface-e-Constraint-Authoring.md`](02-Interface-e-Constraint-Authoring.md):

- o documento `02` descreve **o que o usuário precisa conseguir fazer** para preparar componentes, nets, grupos, regiões, regras, stackup e manufacturing constraints;
- este documento descreve **como a aplicação desktop deve organizar esse workflow**, incluindo shell, docking, workspace central, painéis destacáveis, seleção, inspector, canvas, workbench, optimization/review UI e responsabilidades MVVM.

A principal referência prática é a interface atual do **WTK.MediaForge Studio**, que já resolveu problemas semelhantes de shell desktop profissional, docking redimensionável, janelas flutuantes reais, persistência de layout e sincronização de seleção.

O objetivo não é copiar a interface visual do MediaForge literalmente. O objetivo é reaproveitar os padrões de arquitetura e ergonomia que fazem sentido para uma ferramenta CAD/EDA.

A UI também precisa respeitar o princípio funcional definido nos documentos `02` e `10`:

> **importar/derivar/inferir antes de perguntar ao usuário.**

Advanced algorithm parameters pertencem a Diagnostics/Benchmark mode; o fluxo comum trabalha com intents, perfis e perguntas contextuais somente quando um unknown é materialmente necessário.

---

## 2. Direção de plataforma

A direção inicial do Place&Router é:

```text
Application type       Desktop
Primary language       C#
Runtime                .NET
UI framework           Avalonia
Presentation pattern   MVVM
MVVM toolkit           CommunityToolkit.Mvvm
Docking                Dock.Avalonia family
Core/Engine            Headless and platform-agnostic
```

A versão exata de .NET/Avalonia/Dock deve ser fixada por ADR quando a implementação começar. A intenção é usar versões estáveis e suportadas naquele momento, evitando copiar versões do MediaForge apenas por igualdade histórica.

### 2.1 Por que desktop

Place&Router possui perfil de aplicação muito próximo de CAD/EDA:

- canvas gráfico grande e altamente interativo;
- zoom/pan contínuos;
- milhares de entidades visuais;
- multi-selection;
- inspectors contextuais;
- docking e múltiplos monitores;
- arquivos de projeto locais;
- processamento pesado e potencialmente longo;
- integração com ferramentas e formatos locais;
- possível uso offline;
- designs eletrônicos potencialmente confidenciais.

Portanto, desktop é uma escolha natural e não apenas uma preferência estética.

### 2.2 Desktop não deve aprisionar o engine

A aplicação Avalonia deve ser apenas um host do mesmo Application/Core que pode futuramente ser usado por:

```text
Desktop UI
CLI
Tests
Benchmarks
Automation
Possible server/cloud host
```

O physical-design engine não deve depender de:

- Avalonia;
- Window;
- Control;
- Dispatcher/UI thread;
- Windows APIs;
- Linux APIs;
- macOS APIs.

---

## 3. Referência concreta: MediaForge Studio

A implementação atual do MediaForge Studio usa a seguinte família de bibliotecas:

```text
Avalonia
Avalonia.Desktop
Avalonia.Themes.Fluent
CommunityToolkit.Mvvm
Dock.Avalonia
Dock.Model.Mvvm
Dock.Controls.DeferredContentControl
Dock.Avalonia.Themes.Fluent
```

Na revisão que originou este documento, o MediaForge estava usando Avalonia `12.0.5` e a família Dock `12.0.0.2`. Esses números são **referência histórica da implementação consultada**, não pin obrigatório para Place&Router.

Arquivos de referência no MediaForge:

```text
Directory.Packages.props
WTK.MediaForge.Studio/WTK.MediaForge.Studio.csproj
WTK.MediaForge.Studio/Views/MainWindow.axaml
WTK.MediaForge.Studio/Docking/StudioDockFactory.cs
WTK.MediaForge.Studio/Docking/StudioDockLayoutState.cs
WTK.MediaForge.Studio/Models/StudioLayoutDocument.cs
WTK.MediaForge.Studio/Services/StudioLayoutService.cs
WTK.MediaForge.Studio/ViewModels/StudioShellViewModel.cs
```

---

## 4. O que o MediaForge resolveu e devemos preservar

### 4.1 Shell estável ao redor de uma área dockável

O MediaForge usa uma MainWindow com quatro faixas:

```text
┌───────────────────────────────────────────────┐
│ Custom Title Bar                             │
├───────────────────────────────────────────────┤
│ Toolbar                                      │
├───────────────────────────────────────────────┤
│                                               │
│                DockControl                    │
│                                               │
├───────────────────────────────────────────────┤
│ Status Bar                                    │
└───────────────────────────────────────────────┘
```

Na implementação consultada, as alturas eram aproximadamente:

```text
TitleBar   36 px
Toolbar    36 px
Dock area  *
StatusBar  26 px
```

O Place&Router deve usar a mesma filosofia:

- chrome global permanece estável;
- toda a área de trabalho principal pertence ao docking system;
- a aplicação não tenta gerenciar lateral/bottom panels manualmente através de Grids fixos.

### 4.2 Title bar própria

A janela do MediaForge usa client-area estendida e remove a decoração padrão do SO.

Para Place&Router isso permite:

- identidade visual consistente;
- menu/branding/estado de projeto integrados;
- controle previsível do shell em Windows/Linux/macOS.

A implementação precisa continuar respeitando comportamento nativo de:

- arrastar janela;
- maximizar/restaurar;
- minimizar;
- double-click de titlebar;
- DPI;
- múltiplos monitores.

### 4.3 Docking real

A referência do MediaForge usa:

- `RootDock`;
- `ProportionalDock`;
- `ToolDock`;
- `DocumentDock`;
- `ProportionalDockSplitter`;
- `Tool`;
- `Document`.

O layout default é construído programaticamente através de uma `Factory` especializada.

Esse é o modelo indicado para Place&Router.

---

## 5. ToolDock versus DocumentDock

A distinção precisa existir desde o início.

### 5.1 Documents

Documents representam superfícies centrais de trabalho.

Exemplos planejados:

```text
document.board
document.constraint-graph
document.candidate.<id>
document.comparison.<id>
document.routing-analysis.<id>
```

O primeiro documento é o **Board Workspace**.

Documents podem possuir tabs centrais e dividir a mesma região de edição.

### 5.2 Tools

Tools são painéis auxiliares que podem:

- redimensionar;
- mover;
- reencaixar;
- virar tabs de outro ToolDock;
- auto-hide/pin quando suportado;
- destacar para janelas reais.

Tool IDs iniciais sugeridos:

```text
tool.design-navigator
tool.constraint-composer
tool.properties
tool.optimization
tool.workbench
tool.findings
tool.metrics
tool.log
```

Nem todos precisam estar visíveis por default.

### 5.3 Política inicial

O Board Workspace principal deve permanecer estruturalmente estável.

Os painéis auxiliares devem possuir comportamento equivalente a:

```text
CanFloat = true
CanPin   = true
CanDrag  = true
CanDrop  = true
```

O usuário deve conseguir transformar o workspace numa estação de trabalho multi-monitor sem hacks.

---

## 6. Janelas flutuantes reais

Esse requisito é obrigatório.

Um painel destacado não deve ser apenas uma camada visual dentro da MainWindow.

Deve ser uma janela real capaz de:

- sair dos limites da janela principal;
- ir para outro monitor;
- aparecer adequadamente no taskbar quando a política exigir;
- ser redimensionada independentemente;
- ser restaurada na próxima execução.

A referência do MediaForge cria floating docks com owner independente e `ShowInTaskbar` habilitado.

Para Place&Router, o objetivo é suportar cenários como:

```text
Monitor 1
  Board Workspace

Monitor 2
  Design Navigator
  Inspector
  Constraint Composer

Monitor 3
  Optimization
  Findings
  Metrics
```

---

## 7. Persistência de layout

A posição do usuário é estado de workspace e deve ser persistida.

Modelo conceitual:

```text
PlaceRouterLayoutDocument
 └── Layout
      ├── left/right/bottom proportions
      ├── panel visibility
      ├── panel collapsed/auto-hide state
      ├── active documents/tabs
      ├── floating docks
      └── optional workspace preferences
```

Floating state:

```text
ToolId
X
Y
Width
Height
MonitorId
```

### 7.1 Restore seguro para múltiplos monitores

Devemos preservar a solução do MediaForge:

1. tentar restaurar no monitor original;
2. se ele não existir mais, usar o monitor primário;
3. clamp de width/height ao work area;
4. clamp de X/Y para que a janela fique visível;
5. fallback virtual seguro quando monitor information não estiver disponível.

Isso evita janelas perdidas após:

- remover um monitor;
- mudar resolução;
- mover notebook entre setups;
- alterar DPI;
- usar remote desktop.

### 7.2 Comandos necessários

O shell deve expor:

```text
Reset Layout
Redock All Panels
Show/Hide Panel
Save Workspace Layout
```

Inicialmente um único workspace persistido é suficiente. Perfis de workspace podem ser adicionados depois.

---

## 8. Layout default do Place&Router

Topologia recomendada:

```text
                 PlaceRouterDockFactory

Horizontal Main Dock
│
├── Left ToolDock
│   └── Design Navigator
│
├── splitter
│
├── Center ProportionalDock
│   │
│   ├── DocumentDock
│   │   └── Board Workspace
│   │
│   ├── splitter
│   │
│   └── Bottom ToolDock
│       └── Workbench
│
├── splitter
│
└── Right ProportionalDock
    │
    ├── Constraint Composer
    │
    ├── splitter
    │
    └── Inspector
```

Visualmente:

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ WTK Place&Router                                                    _ □ X  │
├──────────────────────────────────────────────────────────────────────────────┤
│ File | Import | Board | Constraints | Optimize | Review       [▶ Optimize] │
├──────────────────┬──────────────────────────────────┬────────────────────────┤
│ DESIGN NAVIGATOR │                                  │ CONSTRAINT COMPOSER    │
│                  │                                  │                        │
│ Components       │                                  ├────────────────────────┤
│ Nets             │          PCB WORKSPACE           │ INSPECTOR              │
│ Groups           │                                  │                        │
│ Rules            │                                  │ contextual properties  │
│                  │                                  │                        │
├──────────────────┴──────────────────────────────────┴────────────────────────┤
│ Constraints | Findings | Routing | Optimization | Metrics | Log             │
│                             BOTTOM WORKBENCH                                 │
├──────────────────────────────────────────────────────────────────────────────┤
│ Ready │ 47 components │ 62 nets │ 2 layers │ DRC: 0 │ Candidate: —         │
└──────────────────────────────────────────────────────────────────────────────┘
```

As proporções exatas não são invariantes e devem ser ajustadas por visual QA.

---

## 9. MainWindow e shell

Estrutura conceitual:

```text
PlaceRouterShellViewModel
PlaceRouterDockFactory
PlaceRouterLayoutService
PlaceRouterLayoutDocument
```

A MainWindow deve ser fina.

Code-behind permitido apenas para comportamentos genuinamente de Window/UI, por exemplo:

- monitor enumeration;
- restore de floating windows após abertura;
- persist layout durante fechamento;
- native window behavior;
- pointer/keyboard behavior impossível ou inadequado no VM.

Regra:

> lógica de produto não vive em `.axaml.cs`.

---

## 10. MVVM e limites arquiteturais

MVVM é padrão da **Presentation Layer**, não arquitetura do physical-design engine.

Estrutura desejada:

```text
Avalonia Views
      ↓ binding
ViewModels
      ↓
Application / Coordinators
      ↓
Domain + Engine
```

ViewModels não devem:

- executar A* diretamente;
- calcular clearance;
- manipular polygon engine;
- chamar provider de LLM diretamente;
- serializar formatos EDA diretamente;
- conter regras de physical design.

Eles solicitam use cases/coordinators e projetam estado para a UI.

---

## 11. Evitar um ShellViewModel monolítico

O MediaForge possui um `StudioShellViewModel` que coordena muitos serviços e subsistemas. Ele funciona, mas é uma classe ampla.

Place&Router deve começar mais modular.

Estrutura proposta:

```text
PlaceRouterShellViewModel
 ├── DesignNavigatorViewModel
 ├── BoardWorkspaceViewModel
 ├── InspectorHostViewModel
 ├── ConstraintComposerViewModel
 ├── BottomWorkbenchViewModel
 ├── OptimizationViewModel
 ├── StatusBarViewModel
 └── ToolbarViewModel
```

Coordinators/services:

```text
SelectionCoordinator
WorkspaceCoordinator
ConstraintAuthoringCoordinator
LayoutCoordinator
OptimizationCoordinator
ReviewCoordinator
ProjectCoordinator
```

O Shell coordena o workspace global, mas não implementa lógica detalhada dos painéis.

---

## 12. DataTemplates e resolução ViewModel → View

Preservar o padrão do MediaForge:

```text
Dock Tool/Document
      ↓ Context = ViewModel
ContentControl
      ↓ DataTemplate
Avalonia View
```

O docking model não deve criar Views diretamente.

Exemplo:

```text
DesignNavigatorViewModel
  → DesignNavigatorView

InspectorHostViewModel
  → InspectorView

BoardWorkspaceViewModel
  → BoardWorkspaceView
```

Benefícios:

- testabilidade;
- menor acoplamento ao Dock;
- substituição de views sem alterar layout model;
- compiled bindings;
- design-time support mais limpo.

---

## 13. Design Navigator

Painel esquerdo responsável por navegação estrutural.

Tabs iniciais:

```text
Components | Nets | Groups | Rules
```

`Regions` pode virar uma quinta tab ou ser integrado a Groups/Board conforme a UX real demonstrar.

### 13.1 Recursos comuns

- search;
- filtros;
- multi-selection;
- badges de warnings/findings;
- classificação elétrica;
- status placed/unplaced/locked;
- seleção sincronizada com canvas;
- context menus;
- bulk actions.

O navigator deve sinalizar missing data com impacto diferente:

```text
informational unknown
confidence-reducing unknown
required-now unknown
```

### 13.2 Components

Exemplo:

```text
U17
TPS54360
Buck regulator
⚠ 2 findings
```

Filtros:

```text
All
Unplaced
Locked
Power
Analog
Digital
Sensitive
Aggressor
Missing data
Constraint violations
```

### 13.3 Nets

Exemplo:

```text
SW
Switching Power
500 kHz
4.5 A peak
Aggressor: HIGH
```

### 13.4 Groups

Suporte visual a hierarquia:

```text
POWER
├── INPUT
├── BUCK_5V
└── LDO_3V3

ANALOG
└── ADC_FRONTEND
```

Grupos sugeridos automaticamente devem mostrar provenance e permitir Accept/Edit/Reject.

### 13.5 Rules

Agrupar por:

```text
Required
Preferred
Goals

Placement
Routing
Electrical
Manufacturing
Mechanical
Thermal
```

---

## 14. Selection Service central

Seleção é estado compartilhado da Presentation/Application boundary.

Estrutura conceitual:

```text
SelectionState
 ├── Primary
 ├── SelectedObjects[]
 ├── SelectionKind
 └── Source
```

Selecionar `U17` no Navigator deve atualizar:

- canvas highlight;
- Inspector;
- Constraint Composer;
- related findings;
- related nets/relationships.

Selecionar no canvas produz o fluxo inverso.

Não sincronizar painéis por referências diretas entre ViewModels.

---

## 15. Multi-selection

Multi-selection é requisito inicial, não melhoria tardia.

Exemplo:

```text
C17
C18
C19
C20
```

Inspector:

```text
4 Components Selected

Shared properties
Side          Top
Group         POWER_BUCK

Bulk actions
Create Group
Add Constraint
Lock
Set Region
```

Ou:

```text
12 Nets Selected

Set:
Current
Frequency
Aggressor level
Routing class
```

Bulk edit existe para exceções/ajustes; não para obrigar o usuário a preencher o que o enrichment já consegue obter.

---

## 16. Inspector Host

Padrão contextual:

```text
InspectorHostViewModel
    ↓ SelectedPage
```

Mappings iniciais:

```text
Component → ComponentInspector
Net       → NetInspector
Group     → GroupInspector
Constraint→ ConstraintInspector
Region    → RegionInspector
Track     → TrackInspector
Via       → ViaInspector
Finding   → FindingInspector
```

Um `MultiSelectionInspector` deve tratar propriedades comuns e mixed values.

---

## 17. Component Inspector

Seções candidatas:

```text
GENERAL
Reference
Part
Footprint
Value

ELECTRICAL
Role
Power
Aggressor
Susceptibility

PLACEMENT
Side
Rotation
Region
Locked

RELATIONSHIPS
Decoupling
Feedback
Functional group
Related nets

CONSTRAINTS
Required
Preferred
Suggested

PROVENANCE
Imported
User-defined
Deterministically inferred
AI-inferred
Derived
Unknown
```

---

## 18. Net Inspector

Exemplo:

```text
NET: SW

Electrical
Type              Switching power
Voltage           ...
Current           4.5 A
Frequency         500 kHz
Edge rate         Fast

EMI
Aggressor         High
Susceptibility    Low

Routing
Priority          Critical
Width             derived/effective
Layers            L1 preferred
Vias              <= 1 preferred

Relations
Keep away from:
ADC_INPUTS
FEEDBACK_NETS
XTAL
```

Valores devem mostrar effective source/provenance.

---

## 19. Constraint Composer

Painel direito dedicado a criação de relações.

Exemplo:

```text
FROM
U17

TO
ADC_GROUP

Type
Minimum separation

Distance
10.00 mm

Enforcement
Required

Reason
Switching regulator vs sensitive analog
```

A fonte/target podem vir da seleção atual.

---

## 20. Suggestions no Constraint Composer

Possível tab:

```text
Constraints | Suggestions
```

Exemplo:

```text
Suggestions for U17

☐ Keep CIN close to VIN/GND
☐ Keep FB network away from SW
☐ Minimize SW copper area
☐ Keep L3 close to SW
```

Estados:

```text
AI Suggested
Deterministically Suggested
User Defined
Imported
Derived
```

Sugestão não entra silenciosamente como Required.

---

## 21. PCB Workspace

Documento central:

```text
document.board
```

Pode coexistir futuramente com:

```text
document.constraint-graph
document.candidate.<id>
document.comparison.<id>
document.routing-analysis.<id>
```

---

## 22. Board header/toolbar local

Exemplo:

```text
[Select] [Pan] [Route] [Region]
Layer: [All]
View:  [Physical]

Grid
Courtyards
Nets
Routes
Constraints

Zoom 74% [Fit] [1:1]
```

---

## 23. PcbViewportControl

O canvas não pode representar cada track/via/pad como Avalonia `Control` individual.

Estrutura:

```text
BoardWorkspaceView
      ↓
PcbViewportControl
      ↓
IPcbRenderer
```

O renderer desenha em batches/layers:

```text
board
regions
copper
tracks
vias
pads
footprints
courtyards
ratsnest
overlays
selection
findings
```

---

## 24. Renderer boundary

Criar abstração desde cedo:

```text
IPcbRenderer
```

Primeira implementação pode usar rendering customizado do Avalonia.

Futuramente:

```text
AvaloniaDrawingRenderer
GpuRenderer
```

sem alterar Domain/ViewModels.

---

## 25. Hit testing

Hit testing não deve depender de milhares de controls.

Fluxo:

```text
pointer screen coordinate
      ↓
viewport transform
      ↓
board coordinate
      ↓
spatial query/local render index
      ↓
exact hit test
      ↓
selection
```

O geometry/spatial engine local é fonte de verdade para hit testing físico.

---

## 26. View Modes

Projetar desde cedo:

```text
Physical
Connectivity
Constraints
Routing
Congestion
EMI
Thermal
Optimization Diff
```

Nem todos precisam existir inicialmente.

### Physical

Visão normal.

### Connectivity

Destaca ratsnest/conectividade.

### Constraints

Destaca regiões, halos e relationships.

### Routing

Route guides, corridors e tracks.

### Congestion

Capacity/demand/hotspots derivados do global router.

### Optimization Diff

Before/after de transaction/candidate.

---

## 27. Constraint overlays

Constraints precisam ser visualizáveis.

Exemplos:

- minimum-separation halo;
- Required Region;
- Preferred Region;
- Forbidden Region;
- routing corridor;
- high-EMI area;
- congestion overlay.

Seleção de uma constraint deve ativar overlay correspondente e foco nos objetos afetados.

---

## 28. Context menus

Component:

```text
Focus
Lock / Unlock
Move to Group
Assign Region
Add Constraint
Show Related Nets
Suggest Constraints
Analyze
```

Net:

```text
Highlight
Add to Group
Set Electrical Properties
Add Constraint
Show Endpoints
Route Now
Rip-up Route
Analyze
```

Ações de tuning algorítmico de baixo nível não devem aparecer nesses menus comuns.

---

## 29. Bottom Workbench

Tabs:

```text
Constraints
Findings
Routing
Optimization
Metrics
Log
```

Mesmo padrão de Tool que pode virar floating window se o usuário desejar.

---

## 30. Constraints tab

Tabela conceitual:

```text
ID      Enforcement  Source      Target      Rule                 Status
C-001   Required     U17         ADC_GROUP   separation >=10mm    PASS
C-002   Preferred    USB_PAIR    POWER       avoid overlap        WARN
```

Clique centraliza objetos e abre Inspector.

---

## 31. Findings tab

Exemplo:

```text
HIGH   FB route near SW
MED    USB corridor congested
LOW    U17 could rotate to reduce vias
```

Finding selecionado:

- canvas focus;
- highlight;
- Inspector;
- evidence;
- transaction history relacionada;
- repair actions.

---

## 32. Routing tab

Exemplo:

```text
62 nets
58 routed
4 pending

N137 FAILED
Reason: corridor blocked
Blockers: U17, C42
```

Detalhes podem incluir:

```text
required passage
available passage
alternate layers
rip-up attempts
placement-repair escalation
```

---

## 33. Optimization tab/tool

Exemplo:

```text
Run #14
Iteration 3,821
Candidate #247

Hard violations      0
Wirelength           1284 mm
Vias                  83
Critical congestion  0.41

Best candidate       #221
```

O mesmo ViewModel deve conseguir existir como tab inferior ou Tool destacado.

---

## 34. Algorithm details versus user explanation

O painel comum mostra engineering intent/outcome:

```text
Optimizing local congestion near U17
Testing alternative placements and routes
Best candidate improved corridor capacity by 18%
```

Diagnostics/Benchmark mode pode mostrar:

```text
LNS neighborhood size
SA temperature
A* expanded nodes
present/history congestion factor
grid resolution
```

Essa separação preserva usabilidade sem esconder informação necessária para desenvolvimento/auditoria.

---

## 35. Review Mode

Review precisa ser workflow explícito.

Superfícies:

```text
before / after
diff overlays
transaction actions
metric deltas
resolved findings
new regressions
AI explanation
engine evidence
```

Decisões possíveis:

```text
Accept
Reject
Repair
Rollback
Keep as alternate candidate
```

---

## 36. Candidate comparison

Documents podem mostrar:

```text
Candidate A
Candidate B
```

Comparar:

- validity;
- routing completion;
- vias;
- trace length;
- congestion;
- critical loops;
- constraints;
- findings;
- score breakdown.

---

## 37. Undo/Redo

UI manual deve usar a mesma infraestrutura de `PhysicalDesignTransaction` do optimizer.

Exemplos:

```text
MoveComponent
CreateConstraint
EditNetProperties
CreateGroup
MoveRegion
```

Isso permite:

```text
Undo
Redo
Replay
Audit
```

sem criar um segundo modelo de mutação só para UI.

---

## 38. Toolbar global

Categorias iniciais:

```text
File
Import
Board
Constraints
Optimize
Review
Export
```

A toolbar deve mostrar actions frequentes, não cada possibilidade avançada.

---

## 39. Perfis de otimização

A UI comum oferece intents compreensíveis:

```text
Balanced
Routing-first
Compact
Low-via
EMI-conscious
Manufacturing-conservative
```

O mapping exato entre profile e parâmetros pertence ao engine e é versionado.

Advanced settings podem existir, mas não são requisito para uma run normal.

---

## 40. Status bar

Idle:

```text
Ready
│ Board 100×70mm
│ 2 layers
│ 47 components
│ 62 nets
│ 58/62 routed
│ DRC 0
│ Warnings 3
│ Zoom 74%
```

Run:

```text
Optimizing
│ Candidate #221
│ Iteration 3821
│ Hard violations 0
│ 4 nets pending
```

Cloud operation, quando ativa, deve poder aparecer discretamente como estado auditável sem transformar a interface em chat-centric.

---

## 41. Readiness UX

Readiness não é um questionário.

Deve separar:

```text
READY
READY WITH NON-BLOCKING UNKNOWNS
NEEDS N MATERIAL INPUTS
CONSTRAINT CONFLICT
```

Quando houver material input pendente, abrir uma fila curta de perguntas explicadas, não uma property grid inteira.

---

## 42. AI presence na interface

A IA não é o paradigma central da UI.

Ações podem aparecer como:

```text
Suggest
Analyze
Optimize
Explain
Repair
Review
```

Uma interface conversacional pode existir futuramente, mas não substitui:

```text
board
navigator
inspector
constraints
workbench
```

A UI deve distinguir:

```text
Local computation
Cloud AI operation
```

quando isso for relevante para privacidade/auditoria.

---

## 43. Settings

Settings de usuário/aplicação incluem:

```text
language
theme
workspace layout
AI provider credentials/config
optimization profile defaults
advanced/diagnostic mode
```

Secrets não entram no project/PRDX.

---

## 44. Localization

Strings visíveis devem ser externalizadas desde cedo.

O idioma inicial pode ser pt-BR, mas não hardcode textos funcionais críticos em ViewModels.

IDs/enums/domain names permanecem estáveis e independentes de tradução.

---

## 45. Accessibility e automation

Elementos primários devem possuir:

- stable AutomationIds;
- accessible names;
- keyboard navigation;
- focus states;
- contrast adequado.

Isso também facilita Avalonia Headless/UI automation tests.

---

## 46. Keyboard e shortcuts

Baseline:

```text
Ctrl+Z Undo
Ctrl+Y Redo
Ctrl+S Save
Ctrl+O Open
F           Fit board
+/-         Zoom
Delete      remove selected editable object/action where safe
Esc         cancel current tool/selection mode
```

Shortcuts devem passar por serviço/command routing, não lógica espalhada em Views.

---

## 47. Threading

UI thread nunca executa optimizer/routing pesado.

Fluxo:

```text
UI command
   ↓
Application async operation
   ↓
engine worker/tasks
   ↓
immutable/snapshot progress
   ↓
UI projection update
```

Progress updates devem ser throttled/coalesced para não transformar a render thread em bottleneck.

---

## 48. Run progress

Não publicar toda micro-mutação de milhares de candidates para a UI.

Publicar snapshots úteis:

```text
phase
iteration
best candidate
active neighborhood
metrics
current routing failure
findings count
```

Debug/benchmark mode pode coletar detalhe maior em log, sem redesenhar o canvas a cada move.

---

## 49. Workspace state versus design state

Não misturar:

```text
PhysicalDesignState
```

com:

```text
Workspace/UI state
```

Exemplos de workspace state:

- dock positions;
- zoom;
- selected tab;
- filters;
- panel visibility.

Exemplos de design state:

- component pose;
- constraint;
- route;
- region;
- property value.

Salvar layout de UI nunca altera PRDX design semantics.

---

## 50. Plano de implementação

### UI-01 — Shell e docking

- MainWindow;
- custom titlebar;
- toolbar;
- statusbar;
- `PlaceRouterDockFactory`;
- tool/document IDs;
- floating real;
- redock;
- reset layout;
- monitor-safe persistence.

### UI-02 — Design Navigator

- tabs;
- search;
- filters;
- selection;
- multi-selection;
- badges;
- provenance;
- missing-data severity.

### UI-03 — Inspector architecture

- host;
- component/net/group/constraint/region;
- mixed values;
- provenance;
- bulk edit.

### UI-04 — PCB Workspace foundation

- viewport;
- renderer;
- pan/zoom;
- layers;
- board outline;
- footprint/pad rendering;
- selection/hit testing.

### UI-05 — Constraint Composer

- source/target;
- relation type;
- Required/Preferred/Goal;
- preview;
- provenance;
- suggestions.

### UI-06 — Board/Manufacturing Setup

- board;
- stackup;
- profiles;
- keepouts;
- mechanical regions.

### UI-07 — Readiness + contextual questions

- automatic enrichment status;
- blocking/non-blocking unknowns;
- ranked questions;
- safe fallbacks;
- conflict navigation.

### UI-08 — Workbench

- constraints;
- findings;
- routing;
- metrics;
- logs.

### UI-09 — Optimization UI

- profiles;
- progress;
- candidates;
- current problem;
- pause/stop;
- diagnostics mode.

### UI-10 — Review Mode

- before/after;
- diff;
- regressions;
- candidate compare;
- accept/reject/repair.

### UI-11 — Workspace persistence

- floating state;
- monitors;
- panel visibility;
- active tabs;
- viewport preferences.

### UI-12 — Accessibility / visual QA

- headless tests;
- automation IDs;
- keyboard;
- common resolutions;
- multi-monitor native checks.

---

## 51. Test strategy

### ViewModel tests

Sem Avalonia quando possível.

### Avalonia Headless

- DataTemplates;
- commands;
- selection;
- panel content;
- workflow smoke tests.

### Docking tests

- layout creation;
- reset;
- redock;
- visibility;
- floating-state normalization.

### Native tests

- real floating windows;
- taskbar;
- multiple monitors;
- DPI;
- unplugged monitor restore.

### Visual QA

Resoluções mínimas:

```text
1366×768
1920×1080
2560×1440
```

Também testar scaling/DPI diferentes.

---

## 52. O que reaproveitar do MediaForge

Reaproveitar conceitualmente:

- Avalonia/MVVM separation;
- Dock.Avalonia architecture;
- Factory-based layout;
- real floating tools;
- monitor-safe persistence;
- ViewModel→View DataTemplates;
- central Selection Service;
- contextual Inspector;
- Bottom Workbench pattern;
- thin Window code-behind;
- stable automation IDs;
- headless/visual QA strategy.

---

## 53. O que não copiar do MediaForge

Não copiar cegamente:

- Studio-specific ViewModels;
- media/output concepts;
- preview/native-host infrastructure;
- shell VM excessivamente centralizado;
- dimensões/proporções sem novo QA;
- visual styling idêntico;
- assumption de que todo missing property precisa de editor manual.

Place&Router é CAD/EDA e exige seleção espacial, constraint overlays, candidate comparison, routing diagnostics e zero-tuning muito mais fortes.

---

## 54. Critérios de aceite da arquitetura UI

A arquitetura de UI está adequada quando:

1. engine funciona sem Avalonia;
2. board workspace não depende do Dock internamente;
3. Tools podem destacar para janelas reais;
4. layout sobrevive a mudanças de monitor;
5. seleção sincroniza todas as superfícies;
6. multi-selection funciona desde cedo;
7. Inspector é contextual;
8. constraints são visualizáveis no board;
9. run pode ser acompanhada sem bloquear UI;
10. candidate/regression diff é navegável;
11. usuário comum não precisa tunar algoritmos;
12. missing data só interrompe quando materialmente necessário;
13. local/cloud boundaries são auditáveis;
14. lógica de produto não vive em code-behind.

---

## 55. Decisões ainda abertas

- versão final de .NET/Avalonia na solução inicial;
- tema visual final;
- renderer Avalonia inicial versus GPU desde cedo;
- package escolhido para icons;
- workspace profiles múltiplos;
- engine in-process versus processo separado;
- advanced property-grid implementation;
- eventual chat surface.

Essas decisões não impedem o shell/architecture definidos aqui.

---

## 56. Princípio final

Place&Router deve parecer e funcionar como uma **ferramenta de engenharia CAD/EDA profissional**, não como um formulário técnico nem como um chat que por acaso desenha PCB.

O usuário opera sobre objetos reais:

```text
components
nets
groups
regions
constraints
routes
findings
candidates
```

O software usa processamento local e IA para reduzir o trabalho necessário para configurar e otimizar esses objetos, sem esconder a fonte de verdade física nem exigir conhecimento dos parâmetros internos dos algoritmos.
