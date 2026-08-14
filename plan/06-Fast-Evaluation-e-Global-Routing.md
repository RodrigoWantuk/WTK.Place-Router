# PLAN-06 — Fast Evaluation e Global Routing

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-02 e PLAN-03 concluídos  
**Pode avançar em paralelo a:** PLAN-04/05  
**Desbloqueia:** PLAN-07 e PLAN-08

---

## 1. Instrução ao agente

Você está implementando a camada que responde rapidamente se um placement parece roteável e por onde as nets deveriam aproximadamente passar. Esta camada não produz cobre final; ela produz métricas, capacidade, guides/corridors e diagnostics para orientar placement e detailed routing.

Antes de codificar:

1. leia `/AGENTS.md` e o plano mestre;
2. confirme Geometry/Constraints e Project/Transactions funcionais;
3. leia este plano inteiro;
4. leia os documentos obrigatórios;
5. implemente o pipeline multi-fidelity completo deste plano, incluindo benchmark mínimo de correlação/qualidade.

### Documentos obrigatórios

- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/06-Roadmap-e-Criterios-de-Sucesso.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/adr/0003-Processamento-Local-e-Estrategia-Algoritmica.md`
- PLAN-02 e PLAN-03

---

## 2. Objetivo mensurável

Ao final deve ser possível, para um `PhysicalDesignState` ainda não roteado:

```text
compute fast placement metrics
→ estimate multi-pin topology
→ build per-layer routing resource grid
→ reserve/route coarse guides for nets
→ negotiate congestion
→ identify hotspots/blockers
→ return RouteGuides/ReservedCorridors + metrics + RouteFailure diagnostics
```

Placement futuro deve conseguir perguntar “qual o impacto de colocar este componente aqui?” sem detailed-route completo.

---

## 3. Fast evaluator contract

Criar serviço/strategy equivalente a:

```text
FastPhysicalEvaluator.Evaluate(state, scope, profile)
→ FastEvaluationResult
```

Métricas v0.1:

- weighted HPWL;
- pad Manhattan distance;
- density/local occupancy;
- pin escape pressure proxy;
- critical relationship distances;
- region occupancy;
- coarse routing demand;
- reserved corridor consumption;
- estimated vias/layer transitions quando possível;
- spare routing capacity proxy.

Required violations continuam sendo validade separada, não score.

---

## 4. Net criticality baseline

Criar baseline determinístico para peso/ordem de nets usando dados disponíveis:

- explicit routing priority;
- Required length/skew rules;
- differential-pair status;
- current/width demand;
- legal layer count;
- pin escape difficulty;
- endpoint count;
- semantic criticality confirmada;
- user override.

Unknown não vira criticality inventada; usar neutral/default provenance quando necessário.

---

## 5. Multi-terminal topology estimate

Pipeline:

```text
2-pin net → direct Manhattan/octile estimate
multi-pin → HPWL ultrabarato
survivors/important nets → RMST/RSMT estimate
```

Avaliar FLUTE/equivalente conforme documentação/licença. Se não for incorporado neste plano, implementar RMST baseline simples e deixar boundary `ISteinerTopologyEstimator` para substituição mensurável.

Não copiar código incompatível de referência externa.

---

## 6. Routing resource grid

Implementar coarse view por copper layer:

```text
RoutingGrid
  LayerGrid
    Cell/Edge:
      totalCapacity
      obstacleFraction
      reservedCapacity
      committedDemand
      presentCongestionCost
      historicalCongestionCost
      localPenalty
```

Grid pitch deve ser derivado automaticamente de board size/manufacturing/routing pitch/performance profile; não expor ao usuário normal.

Grid é runtime/cache, nunca PRDX canônico.

---

## 7. Capacity estimation

Transformar geometria/keepouts/component bodies/pads/fixed copper em capacidade aproximada por layer.

Considerar:

- minimum width + clearance;
- board boundary;
- keepouts;
- unavailable layers;
- obstacle fraction;
- via transition feasibility aproximada;
- reserved critical corridors já persistidos como intent quando houver.

Não prometer DRC exato nesta fase.

---

## 8. Global router

Implementar `IGlobalRouter`/strategy equivalente.

Two-pin branch:

- A* ou Dijkstra sobre resource graph coarse;
- custo por distance + congestion + via transition + layer preference + corridor rules.

Multi-pin:

- obter tree/topology estimada;
- route branches compartilhando recursos;
- produzir `RouteGuide`/`ReservedCorridor` com confidence/demand.

Guide deve referenciar net/stable IDs e layers/regions, sem fingir track geometry final.

---

## 9. Negotiated congestion

Implementar estratégia PathFinder-like:

```text
initial routes
→ detect overused resources
→ increase present/history cost
→ rip/re-route affected guides
→ iterate until no overflow or budget
```

Registrar:

- iteration count;
- overflow cells/edges;
- historical cost evolution;
- nets repeatedly failing;
- convergence/budget reason.

Evitar ordem greedy permanente.

---

## 10. Net ordering

Ordenar deterministically por constrainedness/criticality baseline.

Permitir seed/config/replay.

Não usar LLM no inner loop.

---

## 11. Corridor reservation

Criar first-class runtime representation coerente com Domain/docs:

```text
ReservedCorridor
  net/group
  candidateLayers
  geometry/grid path
  capacityDemand
  priority
  confidence
  source
```

Quando corridor for explicit user rule, intent vem do PRDX; quando calculado pelo global router, permanece derived/run artifact.

Fast evaluator deve penalizar placement candidate que consome corredor crítico.

---

## 12. RouteFailure diagnostics

Falha global deve retornar dados estruturados, por exemplo:

```text
netId
failureKind
blockedRegion
requiredCapacity
availableCapacity
blockingObjects
attemptedLayers
alternativeLayers
hotspotRefs
suggestedRepairTargets
```

Não retornar apenas `false` ou exception.

---

## 13. Incremental recomputation

Integrar com `EditImpactPlanner`/DependencyGraph do PLAN-03.

Mover um componente deve permitir:

- atualizar cells afetadas;
- invalidar guides das nets relacionadas e guides que atravessam cells impactadas;
- reroute scope local/global conforme impact;
- preservar guides independentes.

Pode existir full rebuild fallback para casos ambíguos, mas deve ser diagnosticável e não padrão para todo edit.

---

## 14. Candidate metrics normalization foundation

Criar estrutura para métricas com:

- raw value;
- normalized value quando policy disponível;
- direction better/worse;
- weight/profile;
- provenance/evaluator version.

Não fixe score weights “perfeitos”. Use baseline explícito e configurável/benchmark-gated.

---

## 15. Benchmark harness mínimo

Criar pequeno harness headless para comparar placements/fixtures.

Registrar pelo menos:

- HPWL;
- global route completion;
- overflow;
- max congestion;
- guide length;
- estimated vias;
- compute time;
- seed/config.

Objetivo é verificar que placement obviamente melhor/pior seja distinguível e preparar comparação com detailed routing do PLAN-07.

---

## 16. Testes mínimos

1. HPWL/RMST fixture conhecido;
2. obstacle reduz capacity correta qualitativamente;
3. A*/Dijkstra guide evita blocked cells;
4. negotiated congestion resolve caso simples onde greedy falha;
5. impossible corridor retorna RouteFailure útil;
6. moving component invalida apenas scope relevante em fixture;
7. fixed seed produz resultado reproduzível;
8. Required corridor/user reservation é respeitado.

---

## 17. Fora de escopo

- exact track/via geometry;
- exact DRC pós-rota;
- differential pair detailed coupling;
- LNS/SA placement;
- AI;
- UI obrigatória.

---

## 18. Critérios de aceitação

Plano concluído quando:

- fast evaluator produz métricas úteis e versionadas;
- global router gera guides/reservations por layer;
- negotiated congestion funciona;
- congestion/hotspots são mensuráveis;
- failures têm blockers/capacity evidence;
- incremental invalidation integra com PLAN-03;
- benchmark headless executa em fixtures;
- build/test alvo passa.

### Demonstração mensurável

Em duas variantes de placement do mesmo board:

```text
Candidate A: completion 100%, max congestion 0.72
Candidate B: overflow 6 edges, max congestion 1.34
```

E mover um blocker deve alterar guides/metrics afetados sem reconstruir semanticamente todo o projeto por padrão.

---

## 19. Relatório final

Informar topology estimator usado, grid derivation, global search strategy, negotiated congestion behavior, benchmark fixtures/resultados e limitations deliberadas para PLAN-07.