# PLAN-05 — Constraint Workspace e Enriquecimento Automático

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-02, PLAN-03 e PLAN-04 concluídos  
**Desbloqueia:** experiência de preparação completa e PLAN-10

---

## 1. Instrução ao agente

Você está implementando a principal camada de authoring do WTK.Place&Router. O objetivo é permitir que um usuário, inclusive não especialista em PCB avançado, prepare o design sem preencher formulários enormes: o sistema importa, deriva e sugere antes de perguntar.

Antes de codificar:

1. leia `/AGENTS.md`;
2. leia o plano mestre;
3. confirme que geometry/constraints, project lifecycle e desktop shell estão funcionais;
4. leia este plano inteiro;
5. leia os documentos obrigatórios;
6. implemente authoring real usando os services do Domain/Application, sem lógica paralela na UI.

### Documentos obrigatórios

- `docs/01-Interoperabilidade-e-Modelo-Canonico.md`
- `docs/02-Interface-e-Constraint-Authoring.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/07-Arquitetura-da-Interface.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- PLAN-02, PLAN-03 e PLAN-04

---

## 2. Objetivo mensurável

Ao final, o usuário deve conseguir:

```text
Open/import project
→ inspect automatic classifications/unknowns
→ edit component/net properties
→ create groups and regions
→ select manufacturing profile
→ define board/stackup
→ author Required/Preferred/Goal constraints
→ bulk edit multiple entities
→ detect conflicts immediately
→ receive readiness report
→ answer only material missing-information questions
→ save everything into PRDX
```

---

## 3. Deterministic enrichment pipeline

Executar automaticamente após import/load quando necessário, de forma incremental:

### Net/name normalization candidates

Reconhecer de maneira conservadora padrões como:

- GND/DGND/AGND;
- VCC/VDD/VBAT;
- CLK/CLOCK;
- SPI/I2C/UART naming;
- USB_D+/USB_D-;
- *_P/*_N;
- TX/RX pairs quando naming/topology suportar.

Não fundir nets por nome semelhante.

### Power/ground candidates

Usar:

- names;
- imported net classes;
- pin names;
- fanout/topology;
- imported plane metadata.

Resultado é classification candidate com provenance/confidence, não corrente inventada.

### Differential-pair candidates

Detectar naming/topology complementar e gerar relationship candidate.

Não inventar impedance/skew target.

### Connectivity-strength graph

Construir/usar weighted component graph baseado em connectivity, criticality conhecida e relações semânticas confirmadas.

Esse grafo será consumido por grouping/placement e não precisa ser exposto cru ao usuário.

---

## 4. Provenance UX

Toda propriedade editável relevante deve indicar origem:

```text
Imported
User Defined
Deterministically Inferred/Derived
AI Inferred (futuro)
Default/Profile
Unknown
```

Ao usuário substituir valor inferido/default:

- persistir como UserDefined;
- não voltar a sobrescrever silenciosamente em enrichment posterior;
- permitir reset para inherited/default quando fizer sentido.

---

## 5. Net Properties editor

Implementar edição real de campos suportados pelo schema/domain, incluindo quando aplicável:

- signal type;
- nominal/max voltage;
- continuous/peak current;
- frequency/bitrate/bandwidth;
- edge rate/classification;
- impedance/differential impedance;
- high impedance;
- clock/switching/power flags;
- aggressor/susceptibility;
- return-path criticality;
- routing priority;
- length/skew limits;
- max vias;
- preferred/forbidden layers.

UI deve esconder/despriorizar campos irrelevantes por tipo e deixar Unknown explícito.

Não exigir todos os campos para salvar/otimizar.

---

## 6. Component Properties editor

Editar:

- category/functional role;
- power/thermal metadata disponível;
- EMI aggressor/susceptibility;
- preferred/forbidden region;
- side;
- allowed rotations;
- lock/fixed policy;
- height/assembly restrictions;
- semantic role/provenance.

Mudanças físicas/restritivas devem usar transactions do PLAN-03 e produzir invalidation apropriada.

---

## 7. Groups

Entregar CRUD funcional para:

- ComponentGroup;
- NetGroup;
- Functional/Mixed group quando suportado pelo domain;
- hierarchy/group-of-groups.

Operações:

- create/rename/delete;
- add/remove selected entities;
- tree navigation;
- bulk assign;
- constraints targeting group;
- effective rule preview nos membros.

Evitar duplicar constraints fisicamente para cada membro quando resolver dinamicamente por selector/group é suficiente.

---

## 8. Regions e Board Setup

Board Setup deve permitir editar/importar:

- outline/dimensions;
- layer count/order;
- thickness/material metadata;
- copper thickness/weight;
- holes/fixed connectors;
- keepouts;
- stackup básico.

Regions:

- criar polygon/rectangle visual;
- nome/tipo;
- scope por layer quando suportado;
- Required/Preferred/Forbidden associations.

Drawing de region deve usar Board Workspace e transaction, não gravar pixel coordinates.

---

## 9. Manufacturing Profile UI

Entregar pelo menos:

- perfil custom;
- um ou mais sample/default profiles claramente identificados como template, sem assumir fabricante se não houver fonte confiável embutida;
- editor de minimum width/spacing/drill/via/copper-to-edge/layers/via types básicos;
- provenance/source;
- save no project ou profile library local conforme contract definido.

Alterar profile dispara invalidation global apropriada via PLAN-03.

---

## 10. Constraint Composer

Implementar painel real:

```text
FROM source selector
TO target selector
constraint type
parameters/value
scope/layers
Required | Preferred | Optimization Goal
reason/notes
Add/Update
```

Deve suportar relações documentadas:

- Component→Component;
- Component→Net;
- Net→Component;
- Net→Net;
- Group→Group;
- Group→Net/Component;
- Region associations.

A lista de constraint types deve vir do registry/capabilities, não de strings duplicadas na UI.

---

## 11. Constraint visualization

Board Workspace deve mostrar overlays úteis:

- min-separation halos;
- Required/Preferred/Forbidden regions;
- keepouts;
- relationship links seletivos;
- affected entities;
- violation highlight.

Não renderizar todas as relações o tempo todo se degradar legibilidade/performance.

---

## 12. Conflict diagnostics

Ao criar/editar uma constraint:

```text
apply candidate transaction
→ resolve EffectiveConstraintSet
→ run pre-solve/conflict validation
→ show result
```

Required conflict deve mostrar:

- rule IDs;
- affected entities;
- explanation/evidence;
- navigation para rules.

Não auto-resolver contraditórios alterando uma regra humana sem consentimento.

---

## 13. Bulk editing

Entregar seleção múltipla + edição em lote para casos úteis:

- assign group;
- common net properties;
- aggressor/susceptibility;
- routing priority;
- allowed layers;
- lock policy;
- common constraint creation.

Mostrar mixed values de forma clara.

Não aplicar propriedade incompatível silenciosamente a tipos diferentes.

---

## 14. Missing-information dependency UX

Consumir readiness/dependency analysis do engine.

A UI deve separar:

```text
Blocking Questions
Warnings
Optional/Unknown Data
```

Perguntar apenas quando material.

Cada pergunta deve explicar:

- qual campo falta;
- qual decisão depende dele;
- impacto;
- fallback possível;
- opção para manter Unknown quando suportado.

Exemplo:

```text
VIN_MOTOR maximum current is unknown.
Needed by: automatic current-width rule.
[Enter value] [Use manufacturing minimum only] [Keep unknown]
```

---

## 15. Readiness report completo

Entregar tela/painel com:

- board/stackup status;
- footprint/pad mapping status;
- components/nets classification summary;
- Required/Preferred/Goal counts;
- conflicts;
- blocking missing information;
- warnings;
- manufacturing profile;
- result `READY / READY_WITH_WARNINGS / BLOCKED`.

Cada item navegável deve focar entidade/regra relacionada.

---

## 16. Persistência

Todas as alterações acima devem:

- passar por Application/transactions quando material;
- marcar project dirty;
- persistir em PRDX;
- reabrir preservando provenance/Unknown/groups/regions/constraints;
- não depender de workspace UI para existir.

---

## 17. Testes mínimos

1. deterministic enrichment não sobrescreve UserDefined;
2. diff-pair candidate é detectado em naming fixture e permanece candidate;
3. group constraint resolve membros corretamente;
4. bulk edit aplica somente alvos válidos;
5. Required conflict aparece antes de commit inválido quando policy exigir;
6. manufacturing edit invalida readiness/rules;
7. missing-info UI model mostra somente blockers/warnings materiais;
8. save/reopen preserva groups/regions/constraints/provenance;
9. smoke UI: criar constraint via composer e ver overlay/violation.

---

## 18. Fora de escopo

- DeepSeek suggestions reais;
- optimizer/routing;
- physical manual drag/reroute;
- Gerber;
- datasheet retrieval automático;
- thermal/SI solver.

Deixe UI de `Suggestions` preparada apenas se isso não virar scaffold vazio; PLAN-10 pode adicioná-la depois.

---

## 19. Critérios de aceitação

Plano concluído quando:

- usuário consegue preparar um projeto sem editar JSON;
- net/component/group/region/constraint authoring é funcional;
- board/manufacturing setup é funcional;
- provenance/Unknown são visíveis e persistem;
- conflict/readiness usam engine real;
- bulk edit funciona;
- perguntas são dependency-driven;
- overlays básicos refletem constraints reais;
- save/reopen mantém todo o intent;
- testes alvo passam.

### Demonstração mensurável

Em um projeto importado:

```text
Create group POWER_BUCK
→ add U7/L3/C17/C18
→ set SW_NODE aggressor=High
→ create Required separation ADC_BLOCK ↔ SW_NODE = 8 mm
→ define ANALOG preferred region
→ select manufacturing profile
→ readiness updates
→ save/reopen
→ all definitions preserved
```

---

## 20. Relatório final

Informar enrichment rules entregues, editors/constraints suportados, bulk actions, readiness behavior, persistence demonstration e testes executados.