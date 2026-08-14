# PLAN-07 — Detailed Routing, DRC e Rip-up/Reroute

**Status:** APPROVED  
**Pré-requisito obrigatório:** PLAN-06 concluído  
**Desbloqueia:** PLAN-08, PLAN-09 e PLAN-11

---

## 1. Instrução ao agente

Você está implementando o primeiro router que produz cobre físico real do WTK.Place&Router. O global router já deve existir; agora os `RouteGuide`s precisam virar tracks/vias legalizáveis e failures precisam retornar evidência suficiente para repair.

Antes de codificar:

1. leia `/AGENTS.md` e o plano mestre;
2. confirme PLAN-06 funcional com guides/congestion diagnostics;
3. leia este plano inteiro;
4. leia documentos obrigatórios;
5. preserve geometry/constraint engine como autoridade de DRC;
6. implemente o fluxo pin-access → route → cleanup → exact checks → rip-up/reroute.

### Documentos obrigatórios

- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/05-Agente-IA-Revisao-e-Memoria.md` (apenas contracts de findings/review)
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/adr/0003-Processamento-Local-e-Estrategia-Algoritmica.md`
- PLAN-02, PLAN-03 e PLAN-06

---

## 2. Objetivo mensurável

Ao final, para boards v0.1 suportados, o engine deve conseguir:

```text
consume RouteGuides
→ analyze pad access
→ route tracks/vias in 2.5D
→ obey width/clearance/layer/via rules
→ cleanup path
→ exact post-route DRC
→ measure length/vias
→ rip-up/reroute local conflicts
→ return committed candidate route OR structured RouteFailure
```

---

## 3. Route representation

Solidificar no Domain/runtime:

- Route;
- TrackSegment;
- Via;
- layer transition;
- route status (`PROVISIONAL`, `VALIDATED`, `STALE`, `LOCKED/PRESERVE` conforme contract vigente);
- route ownership by net;
- source/provenance;
- geometry version/base revision.

Persistir somente estado físico aceito no PRDX; search internals ficam fora.

---

## 4. Pin access analysis

Implementar serviço que gera access candidates para cada pad:

- legal exit directions;
- local clearance;
- nearby obstacles;
- neckdown possibility quando explicitamente suportado;
- candidate via access;
- escape difficulty score;
- reason para pad sem acesso.

Pin access usa geometry kernel/exact rules e também alimenta future placement score.

Não assumir que o centro do pad é sempre um node roteável suficiente.

---

## 5. Search space 2.5D

Criar representação local/adaptativa de search graph com estado equivalente a:

```text
RouteNode
  x
  y
  layer
  incomingDirection
```

Transições suportadas inicialmente:

- horizontal;
- vertical;
- 45° diagonal;
- via para layer legal.

Evitar grid mínimo uniforme cobrindo toda a placa. Construir search space em corridor/neighborhood relevante usando manufacturing rules e obstacles inflados.

---

## 6. Obstacle inflation

Para track centerline:

```text
obstacle geometry
+ required clearance
+ half route width
→ forbidden region
```

Via usa envelope/shape compatível com diameter/annular/clearance.

Inflation acelera search, mas exact DRC roda depois. Não confiar apenas na aproximação.

---

## 7. A* primary detailed search

Implementar `IRouteSearchStrategy` com A* inicial.

Heuristic admissível/prática baseada em:

- Manhattan/octile distance;
- lower bound de layer changes.

Real path cost pode incluir:

- length;
- bend penalty;
- via penalty;
- congestion/history;
- undesired layer/direction;
- deviation from RouteGuide;
- proximity preference;
- rip-up/history cost.

Hard obstacles/rules removem edges/states, não adicionam uma penalidade compensável.

Registrar seed somente onde houver randomization auxiliar; A* baseline deve ser determinístico.

---

## 8. RouteGuide integration

Detailed router deve preferir corridor/global guide sem ficar aprisionado por ele.

Policy:

```text
guide cells/region → low cost
near guide         → moderate cost
far outside guide  → higher cost
hard forbidden     → impossible
```

Se guide não é detalhadamente viável, router pode escapar e registrar deviation metric/diagnostic.

---

## 9. Path cleanup/legalization

Depois do path discreto:

1. remove collinear nodes;
2. merge compatible segments;
3. tighten/pull corners quando exact geometry permitir;
4. garantir 45°/allowed geometry policy;
5. instantiate tracks/vias;
6. run exact constraint/DRC validation;
7. medir final length/vias.

Cleanup não pode introduzir violation silenciosa.

---

## 10. DRC pós-rota

Usar Constraint/Geometry engine, cobrindo ao menos:

- minimum width;
- spacing/clearance track↔track/pad/via/copper/edge;
- via diameter/drill/basic annular rule suportada;
- legal layer transitions;
- board/keepout;
- net connectivity/shorts básicos;
- max vias/length quando Required;
- component↔net separation com copper final quando rule existir.

DRC deve produzir findings/evidence por entity IDs.

---

## 11. Connectivity verification

Após route candidate:

- provar endpoints da net conectados pelo geometry/topology representado;
- detectar open segment/via transition inválida;
- impedir route de net A conectando copper de net B;
- multi-pin net deve satisfazer connectivity para todos os endpoints roteados.

Não depender apenas do fato de A* ter encontrado um target.

---

## 12. Differential pair baseline

Implementar suporte inicial suficiente para não tratar par crítico como duas nets independentes quando relationship confirmado:

```text
pair corridor
→ coupled/centerline strategy ou leader/follower controlado
→ pair tracks with required gap
→ exact gap/clearance
→ skew measurement
```

Não implementar tuning/meander sofisticado além do necessário para casos simples.

Se o caso não puder ser suportado com segurança, retornar `UNSUPPORTED/FAILURE` explícito em vez de rotear como duas nets comuns sem aviso.

---

## 13. Route ordering

Consumir ordering/criticality do PLAN-06.

Tratar explicitamente:

- locked/preserved routes;
- differential pair group;
- high-width/high-current demand;
- constrained nets.

Não deixar primeiras nets monopolizarem recursos sem negotiated reroute.

---

## 14. Local rip-up/reroute

Escalada implementada:

```text
1 alternate local path
2 alternate pin access
3 alternate legal layer
4 rip low-cost local blockers
5 negotiated reroute affected net set
6 widen routing neighborhood
7 declare placement-related blockage
```

Rip-up cost considera:

- net criticality;
- route lock/preserve policy;
- dependent nets/resources;
- prior reroute/failure history;
- disruption cost;
- route stability.

Nunca ripar LOCKED sem autorização explícita.

---

## 15. Placement-related RouteFailure

Quando routing escalation esgota alternativas, produzir diagnostic estruturado útil ao PLAN-08:

```text
failureKind = PLACEMENT_BLOCKAGE
netId
blockedRegion
requiredPassage/capacity
availablePassage/capacity
blockingComponents
blockingRoutes
attemptedAccessPoints
attemptedLayers
suggestedRepairTargets
routingEvidence
```

Não propor coordenadas de component como verdade; apenas blockers/evidence.

---

## 16. Incremental routing

Integrar DependencyGraph/EditImpactPlanner:

- reroute nets cujo endpoint/component mudou;
- revalidate routes spatialmente próximas à geometry alterada;
- preserve independent validated routes;
- update congestion cells/metrics afetadas;
- mark wider scope stale somente quando necessário.

Full reroute pode existir como fallback/command explícito.

---

## 17. Transaction integration

Routing candidate deve ser aplicado dentro de `PhysicalDesignTransaction`:

```text
RipRouteAction
AddTrackAction
AddViaAction
ReplaceRouteAction
LockRouteAction
```

ou actions equivalentes.

Candidate route não altera irreversivelmente accepted state antes de evaluation/commit.

---

## 18. Metrics

Medir por net/candidate:

- route length;
- via count;
- bends;
- layer changes;
- guide deviation;
- local congestion impact;
- DRC violation count/types;
- routing attempts/ripups;
- elapsed compute.

Essas métricas alimentam PLAN-08 e UI futura.

---

## 19. Benchmark/test fixtures

Criar poucos boards sintéticos de alto valor:

- simple open two-pin;
- obstacle requiring detour;
- via/layer transition;
- two nets requiring negotiated reroute;
- impossible corridor due to component blockers;
- multi-pin net;
- simple differential pair se suportado.

Testes:

1. route simple legal;
2. route respeita inflated obstacle e exact clearance;
3. via transition legal;
4. locked route não é ripada;
5. local reroute resolve conflict fixture;
6. impossible path retorna structured failure;
7. cleanup não muda connectivity;
8. incremental edit invalida/revalida expected scope;
9. same deterministic input/config gives same route.

---

## 20. Fora de escopo

- push-and-shove avançado;
- any-angle/free-angle router completo;
- high-speed tuning avançado;
- plane/pour synthesis avançada;
- placement search;
- AI;
- UI route editor.

---

## 21. Critérios de aceitação

Plano concluído quando:

- pin access é real e diagnosticável;
- A* 2.5D produz tracks/vias;
- exact DRC valida cobre final;
- connectivity é verificada;
- local rip-up/reroute funciona em fixture não trivial;
- placement blockage produz blockers/evidence;
- routing integra transactions/invalidation;
- metrics e benchmark estão disponíveis;
- tests/build passam.

### Demonstração mensurável

Rode um fixture onde net N1 inicialmente falha por route concorrente, rip-up local permite uma nova solução e o final tem:

```text
route completion = 100%
blocking Required violations = 0
via count = N
route length = X
ripups = Y
```

E outro fixture impossível deve retornar `PLACEMENT_BLOCKAGE` com componentes bloqueadores.

---

## 22. Relatório final

Informar search space/A* implementado, DRC coverage, differential pair baseline, rip-up escalation, fixtures/resultados e limitações que ficam para PLAN-08/09.