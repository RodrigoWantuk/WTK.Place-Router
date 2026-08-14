# PLAN-09 — Edição Física Interativa e Recovery

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-03, PLAN-04, PLAN-05, PLAN-07 e PLAN-08 concluídos  
**Desbloqueia:** experiência CAD integrada da v0.1

---

## 1. Instrução ao agente

Você está implementando a edição física manual do WTK.Place&Router. A aplicação já deve possuir viewport, transactions, constraints, detailed router e joint optimizer. Sua função é conectar essas capacidades para que o usuário possa editar placement/routing diretamente e o software determine automaticamente o menor estágio que precisa ser invalidado/recalculado.

Antes de codificar:

1. leia `/AGENTS.md` e `/plan/00-ROADMAP-MESTRE-V0.1.md`;
2. confirme todos os pré-requisitos funcionais no branch;
3. leia este plano inteiro;
4. leia os documentos obrigatórios;
5. preserve o princípio de **dependency-driven invalidation**, nunca rollback cronológico cego;
6. implemente interação + transaction + recovery + review como um fluxo único;
7. não encerre após implementar apenas drag, apenas undo ou apenas reroute: a entrega é o ciclo integrado.

### Documentos obrigatórios

- `docs/02-Interface-e-Constraint-Authoring.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/05-Agente-IA-Revisao-e-Memoria.md`
- `docs/07-Arquitetura-da-Interface.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md`
- PLAN-03, PLAN-04, PLAN-05, PLAN-07 e PLAN-08

### Referência de UI

Continuar respeitando a arquitetura definida no PLAN-04. Quando precisar mexer em docking/shell/Avalonia, usar como referência:

`https://github.com/RodrigoWantuk/WTK.MediaForge`

Não copiar domínio MediaForge; usar somente os padrões de desktop/docking já documentados.

---

## 2. Objetivo mensurável

Ao final, o usuário deve conseguir:

```text
select component or route
→ move/rotate/edit
→ see immediate geometry/constraint feedback
→ commit manual transaction
→ system computes affected scope
→ invalidates only stale derived artifacts
→ reroutes/rechecks affected neighborhood when possible
→ opens/updates findings
→ shows before/after diff and regressions
→ undo/redo using same transaction model
```

Uma edição distante não deve causar full-board reroute sem motivo.

---

## 3. Interaction modes

Board Workspace deve oferecer pelo menos:

```text
Select
Pan
Move/Rotate component
Route/Edit route
Region edit (já existente, integrar)
```

Interação deve respeitar zoom/pan transform e não usar screen pixels como domain coordinates.

Mostrar snap/grid como view/helper; não alterar canonical coordinate precision.

---

## 4. Component move/rotate

Fluxo de drag:

```text
pointer down selected component
→ begin preview transaction / draft pose
→ interactive broad-phase feedback
→ show obvious Required violations visually
→ pointer up
→ create typed Move/Rotate transaction
→ exact preconditions/evaluation
→ commit manual state even if temporarily invalid when policy allows
→ run EditImpactPlanner/RecoveryPlanner
```

### Regras

- USER_LOCKED não move sem unlock explícito;
- fixed mechanical component exige policy/confirmation apropriada;
- durante drag, usar avaliação barata/throttled;
- no drop, usar avaliação exata;
- estado temporariamente inválido pode existir e deve gerar findings;
- não desfazer silenciosamente a ação do usuário;
- snap visual pode existir, mas o domínio recebe coordenadas canônicas.

---

## 5. Interactive route creation/edit

Entregar operações suficientes para v0.1:

- start route from pad/track endpoint;
- add/change track path using router-assisted segments;
- move/drag existing segment onde suportado;
- delete/rip route/net route;
- add/remove via through router action;
- lock/unlock route;
- request reroute selected net;
- route selected unrouted net.

### Router-assisted behavior

O usuário pode indicar waypoint/corridor/endpoint; o detailed router calcula geometry legal entre pontos quando possível.

Não obrigar usuário a desenhar cada coordenada de centerline manualmente.

Quando path escolhido pelo usuário não puder ser legalizado, retornar preview/diagnostic claro e permitir nova tentativa.

---

## 6. Route editing transaction actions

Usar/expandir actions do PLAN-07:

```text
ReplaceRoute
MoveTrackSegment/Waypoint
AddVia
RemoveVia
RipRoute
LockRoute
UnlockRoute
```

Toda edição produz `TransactionDiff` com affected nets/spatial scope/constraints.

Não mutar route collection diretamente no ViewModel.

---

## 7. Interactive DRC feedback

Durante move/route preview mostrar feedback rápido:

- collision/board bounds;
- approximate clearance;
- locked/forbidden region;
- route collision candidates;
- constraint halo violation.

Após commit/drop:

- exact DRC/constraint evaluation;
- connectivity check;
- findings atualizados.

Diferenciar visualmente preview warning de exact committed finding.

---

## 8. Recovery pipeline integrado

Expandir `RecoveryPlanner` do PLAN-03 para executar stages reais disponíveis.

### Exemplo: mover U17

```text
Move U17
→ update absolute geometry/spatial index
→ re-evaluate affected constraints
→ recompute pin access for U17 nets
→ invalidate/rip routes whose endpoints/clearance became stale
→ reroute affected nets locally
→ update local congestion/global guides if required
→ run regression L0-L2
→ update findings/metrics
```

### Exemplo: mover track N17

```text
Edit N17 segment
→ exact route DRC/connectivity
→ update local congestion
→ re-evaluate component↔net / net↔net constraints nearby
→ regression
```

### Exemplo: metadata edit

Sem physical recomputation.

---

## 9. Automatic versus user-confirmed recovery

Definir policy clara.

### Automatic

- spatial index update;
- constraint/DRC reevaluation;
- local pin access recalculation;
- cheap/local reroute quando não move user-locked entities e não causa disruption relevante;
- findings refresh;
- metrics refresh;
- route guide/local congestion refresh quando escopo conhecido.

### Requires explicit user action or review

- moving additional components beyond edit target;
- ripping locked/preserved routes;
- widening neighborhood de optimizer de forma disruptiva;
- accepting new hard violation como candidate final;
- applying an optimizer candidate that significantly alters manual intent.

Não parar para microconfirmações em recomputações seguras.

---

## 10. Undo/Redo UI

Conectar transaction history do PLAN-03.

Undo/Redo precisa:

- atualizar state/revision;
- rodar invalidation/recovery apropriada;
- restaurar routing/placement semanticamente;
- refletir selection/viewport/finding status;
- não usar uma segunda pilha paralela de UI-only state para dados físicos.

Workspace-only actions como abrir painel não entram no physical transaction history.

---

## 11. Review diff UI

Após mudança física relevante, Bottom Workbench/Review deve poder mostrar:

```text
Actions performed
Affected entities
Before/after pose/route summary
Resolved findings
New findings/regressions
Metric deltas
Routes invalidated/recovered
Recovery actions taken
Status: valid / invalid-requires-review
```

Click em item deve focar board/entidade quando possível.

Não exibir chain-of-thought; mostrar evidência/diff estruturados.

---

## 12. Manual edit versus active optimizer run

Se uma run de optimizer está ativa e o usuário edita estado canônico:

```text
base project/state revision changes
→ run becomes STALE_BASELINE
```

Policy de UX:

- sinalizar run stale imediatamente;
- não deixar resultado stale aplicar silenciosamente;
- permitir cancel/keep for comparison conforme Application contract;
- não bloquear usuário de editar somente para preservar run antiga.

---

## 13. Findings lifecycle

Quando edit/recovery ocorrer:

- findings afetados devem ser reavaliados;
- resolved deixam de aparecer como active mas podem permanecer no diff/history;
- newly introduced ficam active;
- unaffected findings preservam identidade/status;
- finding não deve duplicar a cada recheck se representa o mesmo problema/evidence equivalente.

Definir stable finding key ou deduplication strategy apropriada.

---

## 14. Selective reroute visualization

Durante recovery, viewport deve conseguir destacar:

- route sendo invalidada;
- affected neighborhood;
- nets rerouteadas;
- preserved routes;
- new path/diff.

Não precisa animação sofisticada; precisa tornar o impacto compreensível.

---

## 15. Manual route preservation policies

Ao usuário editar manualmente uma rota, permitir marcar policy equivalente a:

```text
LOCKED
PRESERVE_PREFERRED
REROUTABLE
```

Default de uma rota manual não deve ser destruído imediatamente pelo optimizer sem considerar provenance/policy.

Persistir policy no PRDX quando fizer parte da intenção do usuário.

---

## 16. Save/reopen

Após edits e recovery:

- save deve persistir somente accepted/current project state conforme contract;
- routes/poses/manual policies persistem;
- stale caches não persistem;
- reopen reconstrói derived artifacts sob demanda;
- recovery journal continua funcionando para edits não salvos.

---

## 17. Performance pragmática

Interactive drag não pode executar detailed reroute a cada pixel.

Usar tiers:

```text
pointer move → cheap/broad feedback throttled
pointer drop → exact local evaluation
post-commit → recovery pipeline
```

Coalesce render updates e não transformar cada primitive em ObservableObject.

---

## 18. Testes mínimos

1. drag component produz transaction/diff correto;
2. USER_LOCKED não move;
3. move U17 invalida apenas expected nets/routes em fixture;
4. route edit rechecks local DRC/connectivity;
5. auto-recovery local reroute resolve caso simples;
6. edit que exige mover outro component não faz isso silenciosamente;
7. undo/redo restaura placement+routing;
8. active optimizer run vira stale após edit;
9. findings não duplicam em repeated recheck;
10. save/reopen preserva manual route policy;
11. smoke UI: move component → affected route reroute → diff visible.

---

## 19. Fora de escopo

- push-and-shove avançado estilo EDA completo;
- shove interativo em tempo real de dezenas de tracks;
- collaborative editing;
- AI auto-review como requisito de edit;
- MCTS;
- full visual polish.

---

## 20. Critérios de aceitação

Plano concluído quando:

- components podem ser movidos/rotacionados manualmente;
- routing pode ser criado/editado/ripado em escopo v0.1;
- preview feedback é rápido e exact check acontece no commit;
- EditImpactPlanner determina escopo real;
- RecoveryPlanner rerouteia/revalida seletivamente;
- undo/redo físico funciona;
- findings/regressions/diff são visíveis;
- runs stale são protegidas;
- manual intent/policies persistem;
- build/test alvo passa.

### Demonstração mensurável

```text
Open routed sample.prdx
→ move U17 by 4 mm
→ two connected routes marked stale
→ unrelated routes remain valid
→ local reroute succeeds for one net
→ second net creates clear finding
→ undo restores prior placement/routes
→ redo reapplies and repeats correct affected scope
```

---

## 21. Relatório final

Informar interaction modes entregues, transaction actions, invalidation/recovery examples, reroute scope, findings lifecycle, undo/redo, stale run behavior e smoke validation.