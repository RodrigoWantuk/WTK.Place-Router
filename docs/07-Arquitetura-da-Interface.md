# 07 — Arquitetura da Interface Desktop

## 1. Objetivo

Este documento define a direção arquitetural da interface desktop do WTK.Place&Router.

Ele complementa [`02-Interface-e-Constraint-Authoring.md`](02-Interface-e-Constraint-Authoring.md):

- o documento `02` descreve **o que o usuário precisa conseguir fazer** para preparar componentes, nets, grupos, regiões, regras, stackup e manufacturing constraints;
- este documento descreve **como a aplicação desktop deve organizar esse workflow**, incluindo shell, docking, workspace central, painéis destacáveis, seleção, inspector, canvas, workbench, optimization/review UI e responsabilidades MVVM.

A principal referência prática é a interface atual do **WTK.MediaForge Studio**, que já resolveu problemas semelhantes de shell desktop profissional, docking redimensionável, janelas flutuantes reais, persistência de layout e sincronização de seleção.

O objetivo não é copiar a interface visual do MediaForge literalmente. O objetivo é reaproveitar os padrões de arquitetura e ergonomia que fazem sentido para uma ferramenta CAD/EDA.

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

Seleção é uma preocupação transversal e precisa ser centralizada.

Não devemos ligar painel A diretamente a painel B, painel C e canvas.

Modelo conceitual:

```text
SelectionState
 ├── Primary
 ├── SelectedObjects[]
 ├── SelectionKind
 ├── Source
 └── optional focus/navigation intent
```

Seleção de U17 no Navigator deve poder:

1. selecionar U17 no canvas;
2. atualizar Inspector;
3. atualizar Constraint Composer;
4. destacar constraints relacionadas;
5. filtrar findings relacionados;
6. opcionalmente centralizar viewport.

Seleção no canvas executa o fluxo inverso.

### 14.1 Multi-selection

É requisito inicial, não melhoria futura.

Exemplo:

```text
4 Components Selected

Shared properties
Side    Top
Group   POWER_BUCK

Bulk actions
Create Group
Add Constraint
Lock
Assign Region
```

Ou:

```text
12 Nets Selected

Set electrical properties
Set routing class
Create group
Add relationship constraint
```

---

## 15. Inspector contextual

Preservar o padrão `InspectorHostViewModel.SelectedPage` do MediaForge.

Mappings planejados:

```text
Component   → ComponentInspector
Net         → NetInspector
Group       → GroupInspector
Constraint  → ConstraintInspector
Region      → RegionInspector
Via         → ViaInspector
Track       → TrackInspector
Finding     → FindingInspector
Multi       → MultiSelectionInspector
Nothing     → EmptyInspector
```

### 15.1 Component Inspector

Seções candidatas:

```text
GENERAL
Reference
Part number
Value
Footprint
Provenance

ELECTRICAL
Role
Power
Aggressor
Susceptibility

PLACEMENT
Pose
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
Violations
```

### 15.2 Net Inspector

```text
GENERAL
Name
Endpoints
Group/class

ELECTRICAL
Type
Voltage
Current
Frequency
Bitrate/bandwidth
Edge rate
Aggressor
Susceptibility

ROUTING
Width
Priority
Layers
Length
Vias
Impedance/skew

RELATIONS
Sensitive-to
Keep-away
Differential pair
Return-path requirements
```

### 15.3 Provenance

Todo campo importante deve poder exibir:

```text
Imported
User Defined
AI Inferred
Derived
Default
Unknown
```

A interface não deve fazer informação inferida parecer fato importado.

---

## 16. Constraint Composer

Painel direito dedicado a criação de regras.

Diferentemente de um dialog esporádico, constraint authoring deve ficar visível e integrado ao workspace.

Exemplo:

```text
Create Relation

FROM
U17

TO
ADC_GROUP

Type
[ Minimum separation ▼ ]

Distance
[ 10.00 ] mm

Scope
[ All relevant layers ▼ ]

Enforcement
(•) Required
( ) Preferred
( ) Optimization Goal

Reason
[ Switching regulator vs sensitive analog ]

[ Add Constraint ]
```

### 16.1 Source/target a partir da seleção

O Composer deve conseguir usar:

- primary selection;
- current multi-selection;
- grupos;
- nets;
- regions;
- manual lookup/search.

### 16.2 Preview antes de commit

Antes de adicionar a regra, o canvas pode mostrar:

- halo de separação;
- região proibida;
- relation line;
- affected objects;
- possible existing conflicts.

### 16.3 AI Suggestions

O painel pode possuir tab:

```text
Constraints | Suggestions
```

Sugestões nunca entram silenciosamente nas constraints efetivas.

Workflow:

```text
Suggest
→ Review
→ Accept/Edit/Reject
→ Commit as explicit rule
```

---

## 17. Board Workspace

O Board Workspace é o documento central.

Estrutura:

```text
BoardWorkspaceView
 ├── Workspace Header
 └── PcbViewportControl
```

Header inicial possível:

```text
[Select] [Pan] [Region] [Measure]
Layer: [All ▼]
View:  [Physical ▼]

Grid          ✓
Courtyards    ✓
Nets          ✓
Routes        ✓
Constraints   ✓

Zoom 74%   [Fit] [1:1]
```

Routing manual poderá adicionar ferramentas específicas depois.

---

## 18. PcbViewportControl

O canvas não deve representar cada entidade como um `Control` Avalonia.

Evitar:

```text
TrackControl × 20,000
ViaControl × 1,000
PadControl × 3,000
```

Usar um renderer especializado:

```text
PcbViewportControl
      ↓
IPcbRenderer
      ↓
render board state efficiently
```

Entidades visuais:

- board outline;
- footprints;
- pads;
- courtyards;
- tracks;
- vias;
- zones;
- ratsnest;
- groups/regions;
- constraints;
- halos;
- reserved corridors;
- congestion overlays;
- findings;
- selection;
- transaction diffs.

### 18.1 MVVM boundary

ViewModel fornece estado de viewport e comandos de alto nível.

O control/code-behind pode possuir lógica visual de:

- pointer hit testing bridge;
- drag gesture;
- pan;
- wheel zoom;
- hover;
- selection rectangle;
- render invalidation;
- coordinate transform.

Mas a mutação do design deve virar command/transaction no Application/Core.

---

## 19. View modes

A arquitetura deve aceitar diferentes projeções do mesmo BoardState.

Planejadas:

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

Nem todas precisam existir inicialmente.

### Physical

Visão convencional da placa.

### Connectivity

Enfatiza ratsnest e relações elétricas, reduzindo clutter de cobre conforme necessário.

### Constraints

Exibe:

- minimum-separation halos;
- required/preferred regions;
- forbidden regions;
- semantic relationships;
- constraint violations.

### Routing

Enfatiza:

- routes;
- vias;
- corridors;
- unrouted nets;
- layer assignment.

### Congestion

Exibe capacity/demand map por layer.

### Optimization Diff

Compara estados:

```text
U17 old → new
C18 rotated
N37 rerouted
2 vias removed
```

---

## 20. Overlays de constraints

Constraints precisam ser visíveis graficamente.

Exemplos:

### Separation halo

```text
       required 10 mm
   ┌──────────────────┐
   │                  │
   │       U17        │
   │                  │
   └──────────────────┘
```

### Region

```text
Required Region
Preferred Region
Forbidden Region
```

### Routing resource

```text
Reserved Corridor
Critical Corridor
Capacity Hotspot
```

### EMI/semantics

```text
Aggressor region
Sensitive object
High-dv/dt copper
Quiet region
```

O usuário deve poder ligar/desligar overlays para evitar poluição visual.

---

## 21. Bottom Workbench

Reaproveitar o conceito do MediaForge de um painel inferior com tabs internas.

Tabs iniciais:

```text
Constraints
Findings
Routing
Optimization
Metrics
Log
```

### 21.1 Constraints

Tabela filtrável:

```text
ID      Type       Source    Target       Rule                 Status
C-001   Required   U17       ADC_GROUP    separation >=10mm   PASS
C-002   Preferred  USB_PAIR  POWER        avoid overlap       WARN
```

### 21.2 Findings

```text
HIGH   Feedback route near SW
MED    USB corridor congested
LOW    U17 rotation could reduce vias
```

Selecionar finding deve:

- atualizar selection;
- destacar objetos;
- opcionalmente focar viewport;
- abrir FindingInspector;
- mostrar repairs disponíveis.

### 21.3 Routing

```text
62 nets
58 routed
4 pending

N137 FAILED
Reason: corridor blocked
Blockers: U17, C42
```

### 21.4 Optimization

```text
Run #14
Iteration 3821
Candidate 247

Hard violations      0
Wirelength           1284 mm
Vias                  83
Critical congestion  0.41

Best candidate       #221
```

### 21.5 Metrics

Histórico e comparação entre candidates/runs.

### 21.6 Log

Import, engine, router, AI/tool, error e diagnostics relevantes.

---

## 22. Optimization Tool

Optimization pode começar como tab do Workbench, mas sua arquitetura deve permitir destacá-la como `Tool` real.

Exemplo:

```text
OPTIMIZATION

State
Running

Phase
Local repair

Current problem
N137 cannot route

Neighborhood
U17 / C41 / C42 / L3

Candidates tested
1,842

Best delta
-17.2%

[Pause] [Stop] [Keep Best]
```

A UI não deve congelar enquanto o optimizer roda.

Progress/event streams devem ser desacoplados do physical-design execution thread.

---

## 23. Review Mode

O produto não termina quando o optimizer encontra um candidate.

A interface precisa permitir revisão explícita.

Recursos:

- before/after;
- candidate comparison;
- transaction diff;
- resolved findings;
- new regressions;
- score breakdown;
- constraint status delta;
- move/routing explanations;
- accept/reject candidate;
- revert specific user edits quando suportado.

Documento candidato:

```text
document.comparison.<id>
```

Pode mostrar dois candidates ou baseline versus candidate.

---

## 24. Context menus

Navigator e canvas devem compartilhar actions sem duplicar lógica.

### Component

```text
Focus
Lock / Unlock
Move to Group...
Assign Region...
Add Constraint...
Show Related Nets
Show Related Components
Suggest Constraints
Analyze
```

### Net

```text
Highlight
Add to Group...
Set Electrical Properties...
Add Constraint...
Show Endpoints
Route Now
Rip-up Route
Analyze
```

### Finding

```text
Focus
Inspect
Show Evidence
Attempt Repair
Ignore/Accept Risk (when allowed)
```

Actions chamam commands/application services comuns.

---

## 25. Undo / Redo

O Place&Router possui vantagem arquitetural importante: o modelo já prevê `DesignTransaction`.

A UI manual deve usar a mesma infraestrutura.

Exemplos:

```text
MoveComponentTransaction
RotateComponentTransaction
CreateConstraintTransaction
EditNetPropertiesTransaction
CreateGroupTransaction
MoveRegionTransaction
```

Consequências:

- Ctrl+Z/Ctrl+Y naturais;
- audit trail comum;
- UI e optimizer usam o mesmo mutation model;
- diff/review é consistente;
- autosave/recovery ficam mais simples no futuro.

---

## 26. Toolbar

A toolbar principal deve permanecer curta.

Ações candidatas:

```text
New/Open/Save
Import/Refresh Design
Undo/Redo
Board Setup
Readiness
Optimize
Pause/Stop (contextual)
Review Best Candidate
```

Ferramentas específicas de canvas ficam preferencialmente no header do Board Workspace, evitando uma toolbar global gigantesca.

---

## 27. Status Bar

A status bar deve fornecer contexto operacional permanente.

Idle:

```text
Ready
│ Board 100×70 mm
│ 2 layers
│ 47 components
│ 62 nets
│ 58/62 routed
│ DRC 0
│ Findings 3
│ Zoom 74%
```

Optimization:

```text
Optimizing
│ Candidate #221
│ Iteration 3821
│ Hard violations 0
│ 4 nets pending
│ Best score ...
```

Mensagens longas pertencem a diagnostics/log, não à status bar.

---

## 28. Keyboard e shortcuts

Arquitetura de shortcuts deve ser centralizada.

Primeiro conjunto provável:

```text
Ctrl+N        New
Ctrl+O        Open
Ctrl+S        Save
Ctrl+Z        Undo
Ctrl+Y        Redo
F             Fit board/selection (context-dependent)
Esc           cancel current gesture/tool
Delete        context-dependent delete where legal
Space/Middle  pan gesture, depending on final UX
```

Shortcuts não devem estar espalhados em code-behind sem um serviço/registry comum.

---

## 29. Acessibilidade e automation IDs

O MediaForge já adotou Automation IDs e accessible names em superfícies importantes. Place&Router deve manter esse padrão desde cedo.

Objetivos:

- automated UI tests estáveis;
- keyboard navigation;
- accessible names;
- test selectors que não dependem de texto localizado;
- suporte futuro a screen readers onde aplicável.

IDs devem ser estáveis e semânticos:

```text
design-navigator.search
board-workspace.viewport
constraint-composer.add
optimization.start
workbench.findings
```

---

## 30. Tema e estilo visual

A aplicação deve ter estética profissional de IDE/CAD, não de “chat de IA”.

Características:

- dark theme inicialmente;
- densidade informacional alta, mas organizada;
- typography consistente;
- panels discretos;
- accent usado para seleção/ação, não para decorar tudo;
- status severity claro;
- grids e tables compactos;
- pouca ornamentação.

A IA aparece como capacidade integrada:

```text
Suggest
Analyze
Optimize
Explain
Repair
```

Não como paradigma central:

```text
Chat with your PCB
```

Uma interface conversacional pode existir no futuro como superfície auxiliar.

---

## 31. Localization

Strings de produto não devem ficar espalhadas diretamente em XAML/ViewModels desde a fundação.

Mesmo que pt-BR seja a primeira linguagem operacional, a arquitetura deve aceitar localization.

Textos de:

- menus;
- panel titles;
- constraint types;
- statuses;
- dialogs;
- findings;
- AI explanation templates;

precisam ser externalizáveis.

IDs internos e schema permanecem language-neutral.

---

## 32. Performance da UI

Place&Router pode trabalhar com placas grandes. A UI deve ser desenhada para isso desde cedo.

### 32.1 Não materializar tudo como Controls

Canvas usa renderer especializado.

### 32.2 Virtualização

Listas/tabelas grandes devem usar controls/estruturas virtualizadas quando possível:

- components;
- nets;
- constraints;
- findings;
- logs;
- candidates.

### 32.3 Atualizações incrementais

Uma iteração do optimizer não deve forçar rebuild completo de todas as collections da UI.

Preferir:

- delta notifications;
- throttling/coalescing;
- snapshot rate configurável para visualização;
- background computation;
- UI-thread dispatch apenas para projeções necessárias.

### 32.4 Canvas durante optimization

O engine pode produzir centenas/milhares de candidates por segundo. Não devemos tentar renderizar cada um.

A UI recebe:

- best/current candidate snapshots em frequência limitada;
- meaningful transactions;
- metrics stream agregado.

---

## 33. Responsabilidades do code-behind

Permitido:

- pointer capture;
- drag/resize visual;
- native Window events;
- screen/monitor queries;
- DPI/native integration;
- focus edge cases;
- specialized rendering bridge.

Não permitido:

- constraint rules;
- routing logic;
- optimization strategy;
- project mutation sem transaction;
- model-provider calls;
- import/export business logic.

---

## 34. Responsabilidades dos ViewModels

ViewModels devem:

- projetar estado para binding;
- expor commands;
- representar selection/context;
- converter domain state em labels/list rows/editable properties;
- coordenar dialogs/workspace através de services apropriados.

Não devem duplicar o domain model inteiro desnecessariamente.

Quando um modelo canônico possuir campos que a UI ainda não representa, eles precisam permanecer preservados no projeto.

---

## 35. Workspace state versus design state

Separar rigorosamente:

### Design state

Pertence ao projeto:

```text
components
nets
constraints
regions
board
routes
user design metadata
```

### Workspace state

Pertence ao usuário/app:

```text
panel positions
floating windows
active tabs
zoom preferences
visible overlays
search/filter preferences
window bounds
```

Mover o Inspector para outro monitor nunca deve sujar o arquivo PRDX/design.

---

## 36. Plano de implementação da interface

### UI-01 — Shell e docking

Implementar:

- MainWindow;
- custom title bar;
- toolbar;
- status bar;
- `PlaceRouterDockFactory`;
- `DockControl`;
- Tool/Document IDs;
- real floating windows;
- redock;
- reset layout;
- layout persistence;
- safe multi-monitor restore.

Critério:

> todos os painéis default podem ser redimensionados, movidos, destacados, reencaixados e restaurados sem perder o workspace.

### UI-02 — Design Navigator

Implementar:

- Components/Nets/Groups/Rules tabs;
- search;
- filters;
- selection;
- multi-selection;
- badges;
- context menus;
- bulk operation hooks.

### UI-03 — Selection infrastructure

Implementar antes de proliferar painéis:

- `SelectionState`;
- `SelectionCoordinator`;
- primary + multi selection;
- canvas ↔ navigator ↔ inspector synchronization;
- focus/highlight intents.

### UI-04 — Inspector architecture

Implementar:

- `InspectorHostViewModel`;
- Empty;
- Component;
- Net;
- Group;
- Constraint;
- Region;
- MultiSelection inspectors;
- provenance indicators.

### UI-05 — PCB Workspace base

Implementar:

- Board document;
- `PcbViewportControl`;
- coordinate transform;
- pan/zoom;
- fit;
- layer visibility;
- board outline;
- footprints/pads;
- selection/hit testing;
- ratsnest.

### UI-06 — Constraint Composer

Implementar:

- source/target selectors;
- common constraint types;
- Required/Preferred/Goal;
- distance/layer scope;
- reason;
- preview overlay;
- conflict preview;
- commit through transaction.

### UI-07 — Board Setup

Implementar UI para:

- board dimensions/shape;
- stackup;
- material;
- copper weight;
- manufacturing profile;
- keepouts;
- fixed mechanical regions.

### UI-08 — Bottom Workbench

Implementar tabs:

- Constraints;
- Findings;
- Routing;
- Optimization;
- Metrics;
- Log.

### UI-09 — Optimization UI

Implementar:

- readiness view;
- start;
- running state;
- current phase/problem;
- progress/metrics;
- pause/stop;
- best candidate;
- candidate navigation.

### UI-10 — Review UI

Implementar:

- baseline vs candidate;
- before/after;
- transaction diff;
- regressions;
- findings delta;
- score decomposition;
- explanations;
- accept/reject.

### UI-11 — Advanced visualization

Implementar progressivamente:

- constraint overlays;
- routing corridors;
- congestion;
- EMI proxies;
- thermal proxies;
- optimization diffs.

### UI-12 — Workspace persistence hardening

Cobrir:

- floating windows;
- monitor disconnect;
- DPI/resolution change;
- active tabs;
- panel visibility;
- overlay preferences;
- restore defaults;
- visual QA em resoluções representativas.

---

## 37. Testes da UI

A arquitetura deve permitir Avalonia Headless para fluxos que não dependam de renderização/native window real.

Categorias:

### ViewModel tests

- commands;
- selection propagation;
- inspector choice;
- filtering;
- constraint composer state;
- readiness projection.

### Layout state tests

- invalid values;
- missing monitor;
- bounds clamp;
- persistence round-trip;
- defaults.

### Headless UI tests

- shell creation;
- DataTemplate resolution;
- primary workflows;
- Automation IDs;
- keyboard commands.

### Native/visual QA

- dock/undock;
- floating windows;
- multi-monitor;
- DPI;
- resize;
- dark theme;
- common screen sizes;
- canvas performance.

---

## 38. O que deve ser reaproveitado conceitualmente do MediaForge

Reaproveitar:

1. Avalonia + MVVM;
2. Dock.Avalonia family;
3. Factory programática de layout;
4. distinção `Tool`/`Document`;
5. real floating windows;
6. multi-monitor persistence e normalization;
7. App-level DataTemplates para dock contexts;
8. InspectorHost contextual;
9. bottom workbench com tabs internas;
10. Selection service/coordinator;
11. custom titlebar + toolbar + statusbar;
12. code-behind restrito a comportamento visual/window;
13. accessibility/automation IDs;
14. headless UI testing.

---

## 39. O que não deve ser copiado literalmente do MediaForge

1. O conteúdo/semântica de Navigation, Production e Scene editing.
2. Um ShellViewModel excessivamente centralizador.
3. Qualquer dependência específica do media engine.
4. O canvas de Scene editing, porque PCB possui escala e densidade gráfica diferentes.
5. As versões exatas dos pacotes sem nova avaliação.
6. Proporções fixas do layout sem novo visual QA.
7. Patterns criados para Preview GPU/native hosting que não sejam relevantes à PCB.

---

## 40. Princípios invariantes atuais da UI

1. A aplicação é desktop-first.
2. Avalonia + MVVM é a direção inicial.
3. O engine permanece headless e sem dependência de Avalonia.
4. O workspace segue paradigma IDE/CAD.
5. Painéis auxiliares são dockables reais.
6. Floating panels são janelas reais e multi-monitor.
7. Layout do workspace é persistido fora do design da PCB.
8. Board Workspace é a superfície central.
9. Navigator, canvas, Inspector e Composer compartilham um Selection model central.
10. Multi-selection é requisito básico.
11. Constraint authoring é uma superfície principal, não um recurso escondido.
12. Inspector é contextual.
13. O canvas usa renderização especializada, não milhares de Avalonia Controls.
14. UI e optimizer usam o mesmo modelo de transactions para mutações de design.
15. AI é capability integrada, não o paradigma de navegação da aplicação.
16. Findings, regressions e explanations são first-class na UI.
17. O usuário deve conseguir revisar o que o optimizer fez antes de aceitar um candidate.
18. A interface precisa permanecer utilizável durante processamento pesado.

---

## 41. Próximas decisões a formalizar por ADR

Quando a implementação da UI se aproximar, criar ADRs específicos para:

- versão de .NET;
- versão de Avalonia;
- versão/viabilidade de Dock.Avalonia;
- renderer inicial do `PcbViewportControl`;
- layout serialization strategy;
- localization framework;
- dialog/window service;
- DataGrid/Tree control strategy;
- virtualização de grandes coleções;
- threading/event delivery do optimizer para UI;
- keyboard/shortcut service;
- theme/resource organization.

Este documento fixa a arquitetura de produto e workspace. Os ADRs fixarão escolhas concretas de implementação conforme forem validadas.
