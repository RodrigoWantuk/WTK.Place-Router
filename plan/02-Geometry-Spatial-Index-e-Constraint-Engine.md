# PLAN-02 — Geometry Kernel, Spatial Index e Constraint Engine

**Status:** APPROVED  
**Pré-requisito obrigatório:** PLAN-01 concluído  
**Desbloqueia:** PLAN-03 e PLAN-06

---

## 1. Instrução ao agente

Você está implementando a camada determinística que define a verdade física do WTK.Place&Router. Esta entrega deve fornecer geometria reproduzível, consultas espaciais e avaliação de constraints; não é permitido delegar validade física à IA.

Antes de codificar:

1. leia `/AGENTS.md` e `/plan/00-ROADMAP-MESTRE-V0.1.md`;
2. confirme no branch que PLAN-01 está funcional: solution compila e PRDX runtime existe;
3. leia este plano inteiro;
4. leia os documentos obrigatórios;
5. verifique licença/versão de qualquer biblioteca geométrica antes de adicioná-la;
6. execute este plano como uma unidade completa.

### Documentos obrigatórios

- `docs/00-Visao-Geral-e-Principios.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/adr/0003-Processamento-Local-e-Estrategia-Algoritmica.md`
- `docs/adr/0004-Licenciamento-de-Dependencias-Algoritmicas.md`
- `plan/01-Bootstrap-Core-e-PRDX-Runtime.md`

---

## 2. Objetivo mensurável

Ao final deve ser possível carregar um `PhysicalDesignState` simples e responder deterministicamente:

```text
Onde estão os pads transformados de U1?
U1/U2 colidem?
Qual a distância exata relevante entre A e B?
Um componente está fora do board/keepout?
Quais objetos podem estar próximos desta região?
Quais Required constraints passam/falham/ficam Unknown?
Qual ManufacturingProfile está sendo aplicado?
O projeto está READY / READY_WITH_WARNINGS / BLOCKED para o próximo estágio?
```

---

## 3. Geometry project e boundary

Criar/ativar projeto dedicado equivalente a `PlaceRouter.Geometry`.

Definir `IGeometryKernel` ou boundary equivalente para impedir que tipos de biblioteca externa vazem para Domain/Application.

O kernel deve trabalhar sobre tipos canônicos do projeto e oferecer inicialmente:

- transforms de point/polygon por pose;
- bounding boxes/AABB;
- polygon intersection;
- containment;
- distance relevante;
- polygon offset/inflation;
- segment/polygon interaction necessária aos constraints iniciais;
- board/region/courtyard tests.

### Biblioteca

Avaliar e, se compatível, usar Clipper2 conforme documentação existente. Se outra biblioteca for escolhida, registrar objetivamente por que ela atende melhor o contract e a licença.

Não reimplementar boolean polygon clipping sem necessidade.

---

## 4. Coordenadas e transforms

Usar `Int64` e 1 µm como fonte canônica.

Implementar transforms para:

```text
Footprint local coordinates
+ ComponentPose(x,y,rotation,side)
→ absolute geometry
```

Cobrir:

- 0/90/180/270 graus obrigatoriamente;
- rotations adicionais somente se o domínio/schema permitirem e sem quebrar determinismo;
- top/bottom mirroring;
- pad geometry;
- body/courtyard;
- holes quando relevantes.

Centralizar regras de transformação; não duplicar lógica em viewport/router/constraints futuros.

---

## 5. Geometria derivada e caches controlados

Definir serviços para obter geometria absoluta sem persistir caches no PRDX.

Pode haver cache runtime por state/revision, desde que:

- seja invalidável;
- não faça parte do canonical state;
- resultado seja reproduzível;
- não seja requerido para salvar/reabrir projeto.

---

## 6. Spatial index

Implementar broad phase dinâmico baseado em envelopes.

Candidato inicial: `NetTopologySuite.Index.Quadtree.Quadtree<T>` conforme documentação atual, atrás de boundary do projeto.

Deve suportar:

- insert;
- remove/update;
- query por envelope;
- rebuild quando necessário;
- identificar candidates, nunca substituir exact geometry test.

Fluxo obrigatório:

```text
query AABB
→ candidate objects
→ exact geometry evaluation
```

Criar benchmark/smoke simples que demonstre que a consulta não varre todos os objetos em um cenário razoável, sem transformar este plano em otimização prematura.

---

## 7. ManufacturingProfile runtime

Materializar regras de fabricação necessárias à v0.1:

- minimum track width;
- minimum spacing/clearance;
- minimum drill;
- minimum via diameter;
- annular ring quando modelado;
- copper-to-edge;
- layer count/type compatibility;
- allowed via types básicos;
- minimum component/courtyard spacing quando aplicável.

O profile pode vir de PRDX/user/default, sempre com provenance apropriado.

Não hardcode um fabricante específico no Domain.

---

## 8. Constraint model runtime

Implementar/solidificar constraint types e selectors documentados suficientes para v0.1.

Mínimo Required:

- BoardBounds;
- ComponentOverlap/Courtyard;
- Keepout;
- FixedPosition/Locked;
- AllowedRotation;
- AllowedSide;
- MinimumSeparation component↔component;
- MinimumSeparation component↔net geometry quando geometry existir;
- InsideRegion/OutsideRegion;
- MinimumTrackWidth;
- MinimumClearance;
- CopperToEdge;
- MaximumVias/Length quando houver route para medir.

Além disso suportar `Preferred` e `OptimizationGoal` como classes de enforcement, mesmo que alguns evaluators só passem a ter métricas completas em planos futuros.

---

## 9. EffectiveConstraintResolver

Implementar pipeline de resolução coerente com documentação:

```text
Global
< Manufacturing/project class
< NetClass/ComponentClass
< Group
< Entity/Relationship
< Explicit transaction restriction
```

Regras:

- specificity refina quando compatível;
- Required contradictions não são resolvidas silenciosamente;
- conflict gera diagnostic tipado;
- provenance/source da regra efetiva deve ser consultável;
- `Unknown` permanece Unknown quando dado necessário não existe.

Evitar lógica dispersa em cada evaluator.

---

## 10. Constraint evaluator registry

Criar registry/data-driven dispatch equivalente a:

```text
Constraint + PhysicalDesignState + EvaluationContext
→ ConstraintEvaluation
```

Saída comum:

```text
PASS
FAIL
UNKNOWN
NOT_APPLICABLE
```

com:

- constraintId;
- affected entities;
- measured values quando aplicável;
- required/actual units;
- evidence;
- severity/enforcement;
- provenance/effective source;
- diagnostic/finding seed quando falhar.

Não criar inheritance OO profunda apenas para dispatch.

---

## 11. Constraint pre-solving/conflict validation

Antes de physical search, detectar pelo menos:

- missing footprint geometry necessária;
- fixed object fora do board;
- allowed rotations vazias;
- allowed side incompatível;
- region inexistente/vazia para Required rule;
- layer inexistente;
- selector nominal sem alvo;
- manufacturing profile impossível para board declarada;
- conflitos Required óbvios.

CP-SAT pode ser avaliado somente se houver um subproblema discreto claro e pequeno. Não introduza OR-Tools apenas para satisfazer documentação se heurística/regra direta resolve o escopo deste plano.

---

## 12. Readiness dependency analysis básico

Criar serviço que não gere questionário massivo.

Para unknown relevante, registrar:

```text
field
consumer/stage
impact
blocking or warning
fallback available?
```

Exemplos:

- footprint ausente para component physical → BLOCKING;
- frequency ausente numa UART sem rule dependente → non-blocking;
- current desconhecida quando uma width rule automática explicitamente depende dela → warning/block conforme policy disponível.

Saída agregada:

```text
READY
READY_WITH_WARNINGS
BLOCKED
```

---

## 13. Findings determinísticos iniciais

Usar entidade `Finding` coerente com docs para violações/diagnostics físicos.

Implementar conversão básica de constraint failures para findings persistíveis/visualizáveis, sem ainda implementar lifecycle avançado de repair.

Findings devem referenciar stable entity IDs e evidence.

---

## 14. Testes mínimos

Testes pequenos e determinísticos:

1. transform top/bottom e rotations;
2. courtyard overlap pass/fail;
3. polygon distance/clearance boundary exato;
4. spatial query retorna candidates e exact phase filtra falso positivo;
5. Required violation invalida candidate;
6. Preferred violation não invalida state;
7. specificity resolve regra efetiva correta;
8. Required contradiction produz conflict;
9. Unknown relevante aparece no readiness sem inventar valor;
10. manufacturing minimum é aplicado como regra efetiva.

Use fixtures simples criadas para esses casos; não crie uma suíte enorme.

---

## 15. Fora de escopo

- DSN parsing;
- UI;
- global/detailed routing;
- LNS/SA;
- AI semantics;
- Gerber;
- thermal/SI/PI completos;
- optimizer score final.

---

## 16. Critérios de aceitação

Plano concluído quando:

- Geometry kernel está integrado e testável headless;
- transforms produzem geometria absoluta correta;
- broad phase + exact phase funcionam;
- manufacturing rules iniciais são avaliadas;
- EffectiveConstraintResolver funciona com provenance/specificity/conflicts;
- Required/Preferred/Goal são semanticamente separados;
- readiness não exige preenchimento indiscriminado;
- findings iniciais possuem evidence;
- build/test alvo passa.

### Demonstração mensurável

Carregar fixture de board com alguns componentes/constraints e produzir relatório semelhante a:

```text
Geometry objects indexed: N
Required constraints: 12 PASS / 1 FAIL / 2 UNKNOWN
Preferences: 3 violations
Readiness: READY_WITH_WARNINGS
Blocking finding: component C3 outside BOARD
```

---

## 17. Relatório final

Informar kernel/library escolhida e licença, index usado, constraints realmente suportadas, readiness demonstrada, testes executados e limitações deliberadamente deixadas para PLAN-03/06.