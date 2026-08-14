# Plano Mestre — WTK.Place&Router v0.1

**Plan ID:** MASTER-V0.1  
**Status:** APPROVED  
**Objetivo:** ordenar as entregas de software necessárias para chegar à primeira versão integrada do produto, definindo dependências reais entre planos e evitando microentregas.

---

## 1. Instrução ao agente

Você está trabalhando no **WTK.Place&Router**, uma aplicação desktop de PCB physical design que importa um circuito criado em EDA externo, internaliza-o em PRDX, permite enriquecer regras/semântica, posiciona e roteia a placa de forma conjunta, revisa/regenera alterações e exporta resultados para fabricação, documentação e processos artesanais.

Antes de executar qualquer plano deste diretório:

1. leia `/AGENTS.md` por completo;
2. leia este plano mestre;
3. identifique o plano executável correspondente à tarefa;
4. confirme que todos os planos marcados como pré-requisito estão concluídos no branch de trabalho;
5. leia todos os documentos obrigatórios listados no plano específico;
6. execute o plano específico do início ao fim da unidade de entrega;
7. não transforme o plano em microcommits/microentregas ou checkpoints de aprovação intermediários.

A numeração indica uma ordem recomendada, mas **o grafo de dependências abaixo é a autoridade real**. Planos sem dependência entre si podem ser executados em paralelo em branches/agentes separados quando os pré-requisitos comuns estiverem integrados.

---

## 2. Definição de primeira versão do produto

A primeira versão integrada, chamada neste roadmap de **v0.1**, precisa permitir a um usuário realizar o fluxo completo:

```text
Create/Open Project
→ Import external PCB/netlist design
→ Canonicalize into PRDX
→ Inspect components/nets/board
→ Define/confirm board + manufacturing + constraints
→ Save/reopen project without loss
→ Generate initial placement/routing plan
→ Run local placement/routing optimization
→ Receive deterministic DRC/findings/metrics
→ Use AI for semantic/strategic assistance where appropriate
→ Manually move component/route when desired
→ Automatically invalidate/recover only affected design stages
→ Review accepted physical state
→ Export manufacturing and artwork outputs
```

A v0.1 não precisa resolver toda classe de PCB existente. O alvo inicial permanece placas pequenas/médias, especialmente 2 layers, aproximadamente 30–50 componentes, sem exigir RF/DDR/extreme high-speed como condição de aceite.

---

## 3. Sequência de planos

### PLAN-01 — Bootstrap, Core e PRDX Runtime

Cria a solution real, boundaries entre projetos, tipos fundamentais, modelo canônico inicial, leitura/escrita do container `.prdx`, validação dos schemas existentes e CLI mínima de validação.

**Desbloqueia:** toda a implementação restante.

### PLAN-02 — Geometry Kernel, Spatial Index e Constraint Engine

Implementa geometria física determinística, transforms, broad/exact phase, evaluators Required/Preferred/Goal, manufacturing rules e readiness/constraint diagnostics fundamentais.

**Pré-requisito:** PLAN-01.

### PLAN-03 — Importação, Project Lifecycle, Transactions e Invalidation

Implementa import pipeline com capabilities/loss diagnostics, primeiro adapter EasyEDA/Specctra DSN, project services, transactions/diffs, dependency graph, selective invalidation, save/recovery e run baseline semantics.

**Pré-requisitos:** PLAN-01, PLAN-02.

### PLAN-04 — Desktop Shell e Experiência Básica de Projeto

Entrega a aplicação Avalonia real: shell/docking, abrir/criar/importar/salvar projeto, board viewport, navigator, inspector básico, diagnostics e status. Ainda não exige optimizer completo.

**Pré-requisitos:** PLAN-01, PLAN-03.

### PLAN-05 — Constraint Workspace e Enriquecimento Automático

Entrega authoring de components/nets/groups/regions/rules, bulk edit, board/stackup/manufacturing editor, deterministic enrichment, readiness orientada a dependências e integração de provenance/Unknown.

**Pré-requisitos:** PLAN-02, PLAN-03, PLAN-04.

### PLAN-06 — Fast Evaluation e Global Routing

Entrega routability proxies, candidate metrics, resource grid, corridor reservation, net ordering, global route guides e negotiated congestion com diagnostics estruturados.

**Pré-requisitos:** PLAN-02, PLAN-03.  
**Pode avançar em paralelo a:** PLAN-04 e PLAN-05 após os pré-requisitos comuns.

### PLAN-07 — Detailed Routing, DRC e Rip-up/Reroute

Entrega pin access, A* 2.5D, tracks/vias, obstacle inflation, cleanup, exact post-route checks, incremental routing e escalada local rip-up/reroute.

**Pré-requisito:** PLAN-06.

### PLAN-08 — Placement Search, Joint Optimizer e Regression

Entrega initial placement seed/legalization, LNS + Simulated Annealing, multi-fidelity candidate funnel, joint placement/routing repair, candidate comparison, regression engine L0–L2 e cenário mínimo que prova a tese.

**Pré-requisitos:** PLAN-06, PLAN-07.

### PLAN-09 — Edição Física Interativa e Recovery

Entrega move/rotate manual de componentes e edição de routing, undo/redo transacional, EditImpactPlanner conectado à UI, selective invalidation/recovery, reroute/recheck automático e review de diffs/findings.

**Pré-requisitos:** PLAN-03, PLAN-04, PLAN-05, PLAN-07, PLAN-08.

### PLAN-10 — Semantics e Agent IA DeepSeek

Entrega semantic graph operacional, deterministic enrichment final, AgentOperation contracts, provider abstraction, DeepSeek adapter, semantic/constraint suggestions, focus selection, repair diagnosis/plan e reviews suportados pelo estágio atual.

**Pré-requisitos:** PLAN-05, PLAN-08.  
**Pode avançar em paralelo a:** PLAN-09 e PLAN-11 depois dos pré-requisitos.

### PLAN-11 — Export Pipeline e Artefatos de Fabricação

Entrega exporters tipados a partir do estado aceito: Gerber + NC Drill, documentação PNG/SVG/PDF e DIY transfer PDF/SVG/PNG/TIFF 1:1 com mirror/polarity/marks. Inclui export profiles e gate de fabrication validity.

**Pré-requisitos:** PLAN-03, PLAN-07, PLAN-08.  
**Pode avançar em paralelo a:** PLAN-09 e PLAN-10.

### PLAN-12 — Integração, Produto v0.1 e Release Validation

Integra todos os fluxos, fecha gaps funcionais, completa CLI útil, packaging desktop, exemplos, smoke tests end-to-end, benchmark básico e valida o cenário real de primeira versão desde import até export.

**Pré-requisitos:** PLAN-01 a PLAN-11.

---

## 4. Grafo de dependências

```text
PLAN-01 Bootstrap/Core/PRDX
   ↓
PLAN-02 Geometry/Constraints
   ↓
PLAN-03 Import/Lifecycle/Transactions
   ├──────────────→ PLAN-04 Desktop Shell ─→ PLAN-05 Constraint Workspace
   │                                           │
   └──────────────→ PLAN-06 Fast/Global Route ─┴→ PLAN-07 Detailed Router
                                                   ↓
                                             PLAN-08 Joint Optimizer
                                              ├───────────────┐
                                              │               │
                           PLAN-05 ─────────→ PLAN-10 AI      │
                                              │               │
PLAN-04 + PLAN-05 + PLAN-03 + PLAN-07 + PLAN-08 → PLAN-09    │
                                                              │
PLAN-03 + PLAN-07 + PLAN-08 ───────────────→ PLAN-11 Export  │
                                                              │
                         PLAN-01 .. PLAN-11 ──────────────────┘
                                      ↓
                              PLAN-12 Product v0.1
```

---

## 5. Regra para considerar um pré-requisito concluído

Um plano é considerado concluído quando o branch base utilizado pelo próximo agente contém a capacidade funcional e os critérios de aceitação definidos naquele plano.

O agente que inicia um plano dependente deve verificar:

- arquivos/projetos esperados existem;
- fluxo principal do plano anterior está implementado, não apenas scaffoldado;
- build relevante passa;
- testes/fixtures mínimos definidos no plano anterior existem e passam;
- contracts públicos necessários estão disponíveis.

Não é necessário que o plano anterior tenha cobertura de testes extensa. É necessário que a capacidade da qual o novo plano depende exista de fato.

---

## 6. Princípios transversais para todos os planos

Todos os planos devem preservar:

- engine headless independente da UI;
- `PhysicalDesignState` como estado físico canônico;
- PRDX como formato de projeto;
- hard constraints fora do score;
- transactions/diffs para mudanças relevantes;
- invalidation baseada em dependências;
- processamento numérico local;
- IA fora do inner loop e sem autoridade de validade;
- provider abstraction;
- `Unknown` como estado válido;
- UX baseada em importar/derivar/inferir antes de perguntar;
- dependências de terceiros atrás de boundaries apropriadas e com licença compatível;
- entregas integradas e mensuráveis.

---

## 7. Estratégia de testes do roadmap

PLAN-01 a PLAN-11 devem seguir a política padrão do `AGENTS.md`: testes diretos, rápidos e suficientes para garantir contracts e comportamento essencial.

O foco de testes mais profundo fica concentrado no **PLAN-12**, quando já existe produto integrado e faz sentido gastar tempo em:

- end-to-end;
- recovery/crash paths;
- cross-module regressions;
- import/save/reopen/export;
- benchmark dos algoritmos principais;
- smoke de UI;
- release packaging.

Algoritmos determinísticos podem e devem possuir testes pequenos desde seus próprios planos porque regressões geométricas/routing são caras de diagnosticar posteriormente.

---

## 8. Definition of Done da v0.1

A v0.1 está concluída quando, em uma instalação limpa e usando um projeto de referência suportado, é possível:

1. iniciar a aplicação;
2. importar um design suportado (baseline EasyEDA/Specctra DSN);
3. gerar PRDX válido e persistente;
4. visualizar componentes, pads, nets e board;
5. configurar/confirmar manufacturing/constraints relevantes sem questionário massivo;
6. executar placement + routing local conjuntos;
7. obter rota física com DRC/hard constraints suportados válidos ou diagnóstico explícito quando não houver solução;
8. receber findings/regression metrics;
9. utilizar assistência DeepSeek para tarefas semânticas/estratégicas previstas;
10. mover manualmente componente/route e observar recomputação seletiva/recovery;
11. salvar e reabrir sem perder intent/placement/routing;
12. exportar Gerber + drill e ao menos um formato vetorial/raster 1:1;
13. executar o mesmo núcleo via fluxo headless/CLI suficiente para fixtures/benchmark;
14. completar pelo menos um caso de prova de joint place/route em que routing reabre placement e um repair válido é aceito.

---

## 9. O que não bloqueia a v0.1

Não é necessário para concluir este roadmap:

- suporte pleno a todos os EDAs;
- round-trip perfeito de placement para EasyEDA via SES;
- IPC-2581 completo;
- push-and-shove avançado;
- full SI/PI/EM field solver;
- DDR/RF extremo;
- MCTS/ML/learned router;
- colaboração multiusuário;
- execução distribuída/cloud do engine;
- sistema de plugins;
- cobertura de testes exaustiva.

Esses itens podem virar roadmaps posteriores depois que a primeira versão comprovar a arquitetura.