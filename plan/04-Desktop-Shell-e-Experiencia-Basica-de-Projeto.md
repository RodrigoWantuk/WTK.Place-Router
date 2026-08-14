# PLAN-04 — Desktop Shell e Experiência Básica de Projeto

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-01 e PLAN-03 concluídos  
**Pode avançar em paralelo a:** PLAN-06  
**Desbloqueia:** PLAN-05 e participa do PLAN-09/12

---

## 1. Instrução ao agente

Você está entregando a primeira aplicação desktop utilizável do WTK.Place&Router. O objetivo não é fazer uma UI mockada: a aplicação deve usar os services reais de PRDX/import/project lifecycle já implementados.

Antes de codificar:

1. leia `/AGENTS.md`;
2. leia o plano mestre;
3. confirme PLAN-01/03 funcionais no branch;
4. leia este plano inteiro;
5. leia a arquitetura de interface e os contracts de projeto;
6. **inspecione o repositório de referência `WTK.MediaForge`: https://github.com/RodrigoWantuk/WTK.MediaForge**;
7. use o MediaForge como referência concreta para configuração correta de Avalonia, composição do `MainWindow`, MVVM/view resolution, `Dock.Avalonia`, painéis dockáveis/flutuantes e persistência/restauração de layout;
8. execute o fluxo completo create/open/import/save/view/inspect.

### Documentos obrigatórios

- `docs/02-Interface-e-Constraint-Authoring.md`
- `docs/07-Arquitetura-da-Interface.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0002-Stack-Desktop-e-Fronteiras-Arquiteturais.md`
- PLAN-01 e PLAN-03
- repositório de referência: `https://github.com/RodrigoWantuk/WTK.MediaForge`

### Regra de referência ao MediaForge

O agente **pode e deve se basear diretamente** no WTK.MediaForge para evitar reinventar a infraestrutura Avalonia/docking que já foi validada em outro projeto do mesmo autor.

Inspecionar especialmente os equivalentes existentes no MediaForge de:

```text
Directory.Packages.props / package versions
Studio/Desktop .csproj
App.axaml / DataTemplates
MainWindow.axaml
MainWindow code-behind restrito a concerns de Window/native
Dock Factory
Dock layout/state models
layout persistence service/document
floating window restoration
monitor-safe bounds handling
shell ViewModel composition
ToolDock versus DocumentDock
DataTemplate resolution de Context ViewModel → View
```

O objetivo é **reutilizar padrões e configuração comprovados**, incluindo:

- bootstrap correto do Avalonia;
- compiled bindings quando apropriado;
- custom title bar;
- `DockControl` como workspace central;
- `Dock.Avalonia` / `Dock.Model.Mvvm` e packages auxiliares compatíveis;
- panels realmente destacáveis em janelas independentes;
- `OwnerMode=None`/equivalente quando necessário para floating windows independentes;
- `ShowInTaskbar=true`/equivalente para painéis destacados apropriados;
- persistência de posição/tamanho/monitor dos floating docks;
- recuperação segura quando um monitor previamente utilizado não existe mais;
- DataTemplates para resolver Views a partir do `Context` dos Tool/Document dockables;
- toolbar/statusbar fora do DockControl;
- code-behind limitado a concerns de janela/visual/native que não pertencem ao ViewModel.

### O que NÃO copiar do MediaForge

O MediaForge é referência de infraestrutura desktop e UX shell, **não modelo de domínio do Place&Router**.

Não copiar/acoplar:

- conceitos de Scene/Layer/Source/Sink/Production específicos do MediaForge;
- `StudioShellViewModel` como um monólito;
- lógica específica de preview/video;
- tipos de domínio do MediaForge;
- namespaces/projetos MediaForge como dependência runtime.

Adapte os patterns ao desenho documentado do Place&Router:

```text
Design Navigator
Board Workspace
Constraint Composer
Inspector
Bottom Workbench
Optimization/Review
```

Se houver divergência entre uma implementação histórica do MediaForge e a documentação/ADR vigente do Place&Router, **Place&Router prevalece**.

---

## 2. Objetivo mensurável

Ao final, um usuário deve conseguir iniciar a aplicação e realizar:

```text
New Project
or Import DSN
or Open .prdx
→ see project in CAD/IDE shell
→ navigate components/nets
→ inspect basic properties
→ view board geometry/components/pads/nets
→ see diagnostics/readiness summary
→ Save / Save As
→ close/reopen without data loss
```

Ainda não é necessário executar autorouting/optimizer.

---

## 3. Bootstrap desktop

Criar projetos equivalentes a:

```text
PlaceRouter.Presentation.Avalonia
PlaceRouter.Desktop
```

ou estrutura consistente com ADR vigente.

Fixar versões estáveis de:

- Avalonia;
- CommunityToolkit.Mvvm;
- Dock.Avalonia family;

registrando-as centralmente.

**Antes de escolher/configurar esses packages, comparar com a configuração funcional existente em `WTK.MediaForge`**. O agente pode atualizar versões se releases estáveis/suportadas mais adequadas existirem, mas deve preservar a combinação compatível entre Avalonia e a família Dock em vez de escolher versões independentes por tentativa.

Não referenciar Avalonia a partir de Domain/Core/Application.

---

## 4. Shell e docking

Implementar shell baseado na arquitetura documentada e tomando o MediaForge como referência de implementação:

```text
TitleBar
Toolbar
DockControl
StatusBar
```

Default workspace:

```text
Design Navigator | Board Workspace | Constraint Composer + Inspector
                         |
                  Bottom Workbench
```

Nesta fase Constraint Composer pode estar em estado funcional reduzido/placeholder informativo; o PLAN-05 entrega authoring completo.

### Requisitos

- ToolDocks podem float/dock;
- Board Workspace usa DocumentDock;
- central board document não deve ser destruído ao manipular tools;
- floating windows aparecem no taskbar quando apropriado;
- restore deve ser seguro em monitor desconectado;
- toolbar/statusbar ficam fora da estrutura docking conforme docs.

### Referência obrigatória de docking

Antes de implementar Dock Factory/layout do zero, o agente deve inspecionar no MediaForge os arquivos/classes equivalentes a:

```text
MainWindow.axaml
StudioDockFactory
StudioDockLayoutState
StudioLayoutDocument
StudioLayoutService
StudioShellViewModel
```

A nomenclatura/localização pode ter mudado no MediaForge; encontrar os equivalentes atuais no repositório.

Usar como referência concreta para:

- criação de RootDock/ProportionalDock/ToolDock/DocumentDock;
- splitters e proportions;
- `CanClose`, `CanFloat`, `CanPin`, `CanDrag`, `CanDrop`;
- floating real de Tool panels;
- managed window layer;
- restaurar floating windows;
- monitor ID e bounds;
- evitar docks duplicados no restore;
- clamp de janelas fora da área visível;
- persistência de layout.

Reutilizar/adaptar código somente quando juridicamente e arquiteturalmente apropriado por ser repositório do mesmo projeto/autor; ainda assim, renomear e remodelar para o domínio Place&Router e manter boundaries próprias.

---

## 5. Workspace persistence

Persistir estado de UI separado do PRDX:

- dock layout;
- floating bounds;
- panel visibility;
- last selected internal tabs quando útil;
- recent files;
- viewport preferences simples.

Não persistir constraints/design state em workspace settings.

Restore deve clamp floating windows para telas disponíveis.

**Basear a estratégia de persistência/restauração e os casos de monitor desconectado na implementação equivalente do WTK.MediaForge**, corrigindo/adaptando onde necessário em vez de redesenhar sem referência.

---

## 6. Application coordination

Criar ViewModels/coordinators seguindo docs, algo equivalente a:

```text
PlaceRouterShellViewModel
ProjectCoordinator
WorkspaceCoordinator
SelectionCoordinator
DesignNavigatorViewModel
BoardWorkspaceViewModel
InspectorHostViewModel
BottomWorkbenchViewModel
ToolbarViewModel
StatusBarViewModel
```

Não concentrar toda lógica em um `ShellViewModel` gigantesco.

ViewModels chamam Application services; não chamam ZipArchive/DSN parser/geometry library diretamente.

Para view resolution/docking Context, observar o padrão MediaForge de Tool/Document carregar um `Context` ViewModel e Avalonia DataTemplates resolverem a View. Preferir esse padrão ao Dock model construir Views diretamente.

---

## 7. Project commands

Implementar UX real para:

- New Project;
- Import DSN;
- Open PRDX;
- Save;
- Save As;
- Close Project;
- recent projects básico.

### Comportamento

- mostrar diagnostics importantes de load/import;
- não descartar projeto dirty silenciosamente;
- save usa service atômico do PLAN-01/03;
- import mostra capability/loss summary compreensível;
- cancel de file picker não gera erro.

---

## 8. Design Navigator básico

Implementar tabs/páginas mínimas:

```text
Components
Nets
Groups
Rules
```

Nesta fase:

- Components e Nets são realmente populados;
- Groups/Rules podem exibir o estado existente sem editor completo;
- search por reference/name;
- filtros básicos placed/unplaced/locked/has-warning;
- seleção individual e multi-select foundation;
- badges simples para missing/warning/locked.

Não criar uma VM por primitive de board; listas/navigator podem usar VMs de linha quando apropriado.

O Project Explorer do MediaForge pode ser usado como referência de UX para tabs internas, busca, contextual add/list/card patterns e AutomationIds, adaptando os conceitos para Components/Nets/Groups/Rules.

---

## 9. Selection service

Implementar `ISelectionService`/SelectionCoordinator como fonte de verdade.

Seleção originada no Navigator deve refletir em:

- Board highlight;
- Inspector;
- status/context quando aplicável.

Seleção no Board deve atualizar Navigator/Inspector.

Suportar pelo menos:

- none;
- single component;
- single net;
- multi-component selection;
- provenance/source da seleção para evitar loops de eventos.

---

## 10. PcbViewportControl v0.1

Criar custom-rendered board viewport.

Renderizar a partir de snapshot/read model, não de milhares de Avalonia Controls:

- board outline;
- holes/keepouts básicos;
- component body/courtyard opcional;
- pads;
- ratsnest/connectivity lines simples;
- existing tracks/vias se projeto já trouxer;
- selected entities;
- warning/finding markers simples.

### Interação

- zoom wheel;
- pan;
- fit board;
- 1:1 visual command quando aplicável;
- click/select component/pad/net geometry com hit testing coerente;
- resize do window não destrói transform.

Ainda não implementar move/route edit; isso entra no PLAN-09.

O PreviewWorkspace/Canvas do MediaForge pode servir de referência para composição do workspace e separação View/ViewModel, mas **não copiar sua lógica de vídeo/rendering**. O renderer de PCB é próprio.

---

## 11. Inspector básico

Páginas mínimas:

- Empty;
- Component;
- Net;
- Group/read-only;
- Constraint/read-only;
- BulkSelection summary.

Exibir provenance/Unknown claramente onde dados existirem.

Component:

- ref/value/part/footprint;
- pose/side/lock;
- connected nets;
- basic semantic classification se houver.

Net:

- name/endpoints;
- imported electrical/routing data;
- status unknown/provenance;
- route status se existir.

Edição avançada fica para PLAN-05/09.

O padrão `InspectorHost` + página contextual do MediaForge deve ser usado como referência em vez de criar um inspector monolítico com condicionais espalhadas.

---

## 12. Bottom Workbench básico

Criar tabs internas:

```text
Constraints
Findings
Routing
Optimization
Metrics
Log
```

Nesta fase pelo menos:

- Findings lista diagnostics/findings reais;
- Constraints mostra rule count/violations;
- Log mostra application diagnostics relevantes;
- demais tabs podem mostrar estado `Not available until optimizer/router is present`, mas não devem conter dados inventados.

Click em Finding deve selecionar/focar entidade quando possível.

O Bottom Workbench do MediaForge pode ser usado como referência para um único ToolDock com tabs internas em vez de proliferar painéis independentes sem necessidade.

---

## 13. Statusbar e readiness

Exibir dados reais:

```text
project status
component count
net count
layer count
readiness
hard violation count
warnings
zoom
```

Não executar lógica de readiness na ViewModel; consumir service do engine.

---

## 14. Threading/cancellation

Load/import/save devem rodar sem travar desnecessariamente a UI quando operação puder ser longa.

- não mutar Domain concorrente diretamente do render thread;
- usar immutable/read snapshots/deltas adequados;
- cancellation onde service já permitir;
- erros técnicos viram diagnostic/dialog apropriado.

---

## 15. Visual/automation identifiers

Adicionar `AutomationId` estável nos principais comandos/painéis para permitir smoke visual futuro.

Seguir o padrão de identifiers/visual QA do MediaForge quando aplicável.

Não investir em sistema completo de UI automation neste plano.

---

## 16. Testes mínimos

Priorizar:

1. ShellViewModel/ProjectCoordinator create/open/save flow com fake/real application services leves;
2. SelectionCoordinator evita loop e propaga seleção;
3. layout restore clampa floating window de monitor inexistente;
4. basic viewport transform/hit-test em funções extraíveis determinísticas;
5. smoke manual/documentado: import DSN → UI → save → reopen.

Ao implementar layout persistence, aproveitar como referência os testes existentes do MediaForge para disconnected monitor, bounds inválidos e duplicate ToolId, adaptando o conjunto mínimo de alto valor.

Não gastar o plano tentando testar pixel a pixel toda a UI.

---

## 17. Fora de escopo

- authoring completo de constraints;
- component drag;
- route editing;
- router/optimizer;
- DeepSeek;
- Gerber/export;
- visual polish final.

---

## 18. Critérios de aceitação

Plano concluído quando:

- aplicação desktop inicia;
- docking shell funciona e persiste;
- floating panels funcionam como janelas reais e restauram com segurança;
- DSN pode ser importado através da UI;
- PRDX pode ser aberto/salvo/reaberto;
- board é renderizado a partir do estado real;
- Navigator/Board/Inspector compartilham selection;
- diagnostics/findings aparecem;
- status/readiness vêm do core;
- engine/domain continuam headless;
- build e testes alvo passam.

### Demonstração mensurável

Em uma sessão limpa:

```text
Launch
→ Import sample.dsn
→ Board visible
→ detach Inspector to floating window
→ select U1 in Navigator
→ U1 highlighted + Inspector populated
→ Save sample.prdx
→ close/reopen application
→ workspace safely restored
→ same component/net/board counts and placement shown
```

---

## 19. Relatório final

Informar:

- quais arquivos/padrões do WTK.MediaForge foram usados como referência;
- stack/versions de UI escolhidos e diferenças em relação ao MediaForge;
- estrutura de docks entregue;
- floating/layout persistence entregue;
- project flow demonstrado;
- viewport capabilities;
- selection integration;
- validações executadas.