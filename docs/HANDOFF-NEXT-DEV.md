# WTK.Place&Router — Handoff para o próximo Dev

**Data:** 2026-08-16  
**HEAD analisado:** `fb73a618691acb9300ac22bea3b702f4e8dfb64c` — `Support baseline Specctra DSN structure`

Este documento é um handoff operacional. Ele registra o estado real do repositório no momento da transferência, o que já foi entregue, o que foi deliberadamente validado, o que ainda não deve ser refeito e qual é a próxima unidade de trabalho.

> **Regra:** este documento não substitui `/AGENTS.md`, os ADRs, a documentação nem os planos aprovados. O próximo agente deve tratá-los como fonte de verdade e usar este arquivo apenas para orientação sobre o estado atual.

---

## 1. Primeira leitura obrigatória

Antes de alterar código:

1. `/AGENTS.md` inteiro;
2. `README.md`;
3. `plan/00-ROADMAP-MESTRE-V0.1.md`;
4. `plan/01-Bootstrap-Core-e-PRDX-Runtime.md`;
5. `plan/01R-Consolidacao-e-Hardening-Fundacao-PRDX.md`;
6. `plan/02-Geometry-Spatial-Index-e-Constraint-Engine.md`;
7. `plan/02R-Hardening-da-Verdade-Fisica-e-Constraints.md`;
8. `plan/03-Importacao-Project-Lifecycle-Transactions-e-Invalidation.md`;
9. `plan/04-Desktop-Shell-e-Experiencia-Basica-de-Projeto.md`;
10. `plan/05-Constraint-Workspace-e-Enriquecimento-Automatico.md`;
11. documentação de interoperabilidade, domínio/constraints, UI, decisões/terminologia, processamento determinístico e lifecycle/PRDX em `docs/`;
12. schemas relevantes em `schemas/`.

Não começar pelo código sem entender os contracts acima.

---

## 2. Estado atual do projeto

O projeto já possui uma fundação funcional em quatro grandes áreas:

- **Core/PRDX:** modelo canônico, persistência e lifecycle do projeto;
- **Geometry/Constraints:** geometria física, spatial index e constraint engine determinístico;
- **Import/Lifecycle:** importer, transactions, Undo/Redo, journal/recovery, dependency invalidation e Save/Save As;
- **Desktop:** Avalonia + MVVM, docking, viewport, selection, inspector/navigator/workbench e workspace persistence.

O último commit adicionou suporte ao baseline estrutural de Specctra DSN, substituindo o parser que só entendia o fixture sintético inline anterior por um importer que também entende a separação `library/image/padstack` + `placement/component/place` típica do baseline Specctra. A alteração está concentrada no adapter DSN e nos testes/fixture associados.

O commit `fb73a6` adicionou:

- leitura de `LIBRARY/IMAGE`;
- leitura de `PADSTACK`;
- associação `IMAGE -> Footprint`;
- associação `PLACE.component_id -> Component/RefDes`;
- pads derivados de `IMAGE/PIN/PADSTACK`;
- múltiplos componentes usando o mesmo image/footprint;
- `resolution` quando `unit` não está presente;
- fixture `specctra-baseline.dsn`;
- testes de import do novo baseline.

A comparação com `80b6183` mostra somente três arquivos alterados neste último passo:

```text
src/PlaceRouter.DesignExchange/Specctra/SpecctraDsnImporter.cs
tests/PlaceRouter.DesignExchange.Tests/Fixtures/specctra-baseline.dsn
tests/PlaceRouter.DesignExchange.Tests/Plan03ImportLifecycleTests.cs
```

---

## 3. O que NÃO deve ser refeito agora

Não iniciar uma nova refatoração ampla de:

- `ProjectSession`;
- Undo/Redo;
- recovery journal;
- Save/Save As;
- dirty lifecycle;
- `DependencyGraph`/`EditImpactPlanner`;
- Avalonia shell;
- Dock.Avalonia;
- floating docks;
- viewport selection/hit testing;
- Fit/ratsnest/pad identity/holes;
- custom title bar;
- AutomationIds.

Essas áreas receberam hardening nos commits anteriores e a rodada de revisão anterior considerou os blockers identificados nelas resolvidos.

Se uma nova mudança nesses componentes for necessária para o plano atual, alterar somente o que for exigido pelo contract e adicionar a regressão correspondente.

---

## 4. Histórico imediato relevante

Sequência final relevante:

```text
1388e49  Implement PLAN-03 import lifecycle
39033d2  Fix PLAN-02R selector and soft constraint gaps
 e1ba568 Implement PLAN-04 desktop shell
0addb8c  Harden PLAN-03 lifecycle and PLAN-04 shell
80b6183  Fix PLAN-03 lifecycle recovery edge cases
fb73a61  Support baseline Specctra DSN structure   <-- HEAD
```

O `80b6183` corrigiu, entre outros pontos, monotonicidade de revisions em Undo/Redo, journal/recovery após Undo, diffs de deletes, dependency expansion, validação estrutural, dirty flow, closing race, unidades DSN e atualização de `FileContext` após Save/Save As.

O `fb73a61` é o passo seguinte: corrigir a principal lacuna que ainda impedia considerar o handoff DSN suficientemente comprovado.

---

## 5. Estado do PLAN-03

### 5.1 Lifecycle/transactions

Considerar a implementação como concluída para fins de continuidade, salvo regressão encontrada.

Capacidades relevantes já existentes:

- `ProjectSession`;
- `PhysicalDesignTransaction`;
- transaction actions para placement/rotation/side/lock;
- constraints;
- groups/regions;
- metadata/manufacturing;
- `TransactionDiff` com affected scope;
- Undo/Redo;
- recovery journal;
- revision-safe replay;
- structural validation;
- dependency-driven invalidation;
- dirty lifecycle;
- async project I/O;
- Save/Save As com atualização de `FileContext`.

### 5.2 Importação DSN

O importer agora suporta dois caminhos:

1. **fixture inline legado**, útil para compatibilidade/regressão;
2. **baseline Specctra**, com `LIBRARY`, `IMAGE`, `PADSTACK`, `PIN`, `PLACEMENT`, `COMPONENT` e `PLACE`.

O fixture atual está em:

```text
tests/PlaceRouter.DesignExchange.Tests/Fixtures/specctra-baseline.dsn
```

Ele demonstra:

```text
IMAGE R_0603
  -> U1
  -> U2
```

com o mesmo footprint para dois componentes.

Também usa:

```text
(resolution um 1)
```

em vez de depender de `(unit ...)`.

---

## 6. Importante: o fixture atual NÃO é uma exportação real do EasyEDA

Este é o principal ponto que o próximo Dev precisa entender.

`specctra-baseline.dsn` é um **fixture Specctra sintético e controlado pelo projeto**. Ele foi criado para validar a estrutura baseline do formato e a relação image/component/place/padstack.

Ele **não prova ainda** que um arquivo efetivamente exportado pelo EasyEDA Pro entra no importer sem adaptações.

O objetivo declarado do projeto continua sendo:

```text
EasyEDA Pro
  -> Export Autorouter DSN
  -> WTK.Place&Router
  -> canonical design
```

Portanto, o próximo gate de interoperabilidade é validar um DSN real exportado pelo EasyEDA Pro.

### Não assumir

Não assumir que o fixture sintético representa todos os detalhes emitidos pelo EasyEDA.

Não assumir que todos os campos opcionais do Specctra precisam ser suportados agora.

Não ampliar o importer indiscriminadamente. Primeiro obter um arquivo real e implementar apenas o baseline necessário para produzir um canonical project correto e diagnostics honestos.

---

## 7. Próxima tarefa recomendada — DSN real do EasyEDA

Antes de liberar o PLAN-05, executar uma unidade curta de validação/compatibilidade:

### Entrada

Um `.dsn` realmente exportado pelo **EasyEDA Pro**, preferencialmente uma PCB pequena contendo:

- pelo menos dois componentes;
- pelo menos dois componentes compartilhando o mesmo footprint/image;
- pads SMD;
- pelo menos duas nets;
- board boundary;
- Top e Bottom quando disponível;
- regras de fabricação/autorouter presentes no export.

### Fluxo

```text
EasyEDA Pro
 -> Export Autorouter DSN
 -> importer
 -> canonical project
 -> PRDX save
 -> PRDX reopen
```

### Verificar explicitamente

- referência dos componentes;
- image/footprint mapping;
- pad numbers;
- padstack geometry;
- absolute component placement;
- side;
- rotation;
- nets e endpoints;
- board boundary;
- layers;
- units/resolution;
- regras que forem suportadas;
- diagnostics/losses para tudo que ainda não for suportado.

### Critério de sucesso

Nenhuma informação necessária para o canonical design pode ser inventada silenciosamente.

Informação não suportada deve resultar em:

```text
Unknown
ou
Diagnostic/Capability = unsupported/partial
```

conforme o contract vigente.

O importer não deve fingir que suportou um campo que apenas ignorou.

---

## 8. O que deve ser feito se o DSN real não encaixar

Não reescrever o parser inteiro.

1. adicionar o arquivo real como fixture de integração se a licença/conteúdo permitir;
2. identificar a diferença estrutural concreta;
3. adaptar o adapter Specctra;
4. preservar o fixture baseline atual como regressão mínima;
5. adicionar teste para o caso real;
6. verificar round-trip para PRDX;
7. atualizar documentação de interoperabilidade somente se o comportamento suportado tiver mudado.

Se o arquivo real contiver recursos que estão fora do baseline aprovado, registrar diagnostics e não inventar suporte.

---

## 9. Invalidation graph — situação conhecida

`DependencyGraph` já é consultado pelo `EditImpactPlanner`, mas a expansão atual é deliberadamente conservadora.

Ela pode atravessar:

```text
Component -> Net -> Component -> Net -> ...
```

e produzir um affected scope maior que o mínimo necessário.

Isso é aceitável neste estágio porque é conservador: pode causar recomputação adicional, mas não deve produzir resultado fisicamente incorreto.

**Não transformar isso em uma refatoração agora.**

Antes do optimizer/routing pesado, deverá existir um plano específico para invalidation stage-aware se medições demonstrarem que a expansão é problemática.

---

## 10. Próximo plano após o gate DSN

O próximo plano aprovado é:

`plan/05-Constraint-Workspace-e-Enriquecimento-Automatico.md`

Ele está marcado como `APPROVED` e exige PLAN-02, PLAN-03 e PLAN-04 concluídos.

O PLAN-05 é a próxima unidade grande de implementação.

Objetivo resumido:

```text
Open/import project
 -> inspect classifications/unknowns
 -> edit component/net properties
 -> create groups/regions
 -> manufacturing profile
 -> board/stackup
 -> Required/Preferred/Goal constraints
 -> bulk editing
 -> conflict diagnostics
 -> readiness report
 -> material missing-information questions
 -> save/reopen preserving intent
```

O plano exige authoring real na camada Domain/Application, com UI MVVM apenas como apresentação/orquestração.

### Não antecipar PLAN-06/07/08

Não começar global routing, detailed routing ou joint optimizer dentro do PLAN-05.

---

## 11. Arquitetura que o próximo Dev deve preservar

```text
Presentation / Avalonia
        |
        v
Application / coordinators / services
        |
        v
Domain / canonical project / geometry / constraints / transactions
        |
        v
Infrastructure / PRDX / importers / external providers
```

Regras importantes:

- Domain não conhece Avalonia;
- UI não executa geometry/DRC/routing diretamente;
- AI provider não entra no Domain;
- DeepSeek será provider inicial de IA em planos futuros, mas PLAN-05 é determinístico e não deve chamar DeepSeek;
- hard constraints continuam sendo autoridade de validade;
- IA não substitui DRC/geometry/validity;
- `Unknown` é legítimo;
- provenance deve ser preservada;
- mudanças físicas relevantes usam transactions;
- PRDX é persistência nativa do projeto;
- workspace não deve conter intent físico do projeto.

---

## 12. Estado da interface

A UI atual usa Avalonia + MVVM e foi construída deliberadamente com referência ao WTK.MediaForge.

Referência solicitada pelo projeto:

https://github.com/RodrigoWantuk/WTK.MediaForge

A estrutura de docking usa ToolDock/DocumentDock e possui suporte a floating tools, workspace persistence, navigator, inspector, bottom workbench e viewport.

Não trocar a biblioteca de docking nem redesenhar o shell no PLAN-05.

O PLAN-05 deve adicionar authoring aos painéis existentes e criar os novos painéis estritamente necessários ao contract.

---

## 13. Estado de testes/CI

Os commits anteriores tinham CI verde com build Release e testes unitários direcionados. Para o HEAD `fb73a61`, o conector GitHub consultado não retornou workflow run/status associado ao commit no momento deste handoff.

Portanto o próximo Dev deve **executar o build/test atual antes de declarar o trabalho concluído** e não assumir que o status do commit anterior vale automaticamente para o HEAD.

O foco de testes deve permanecer proporcional:

- importer baseline Specctra;
- fixture real EasyEDA;
- component/image mapping;
- shared footprint;
- units/resolution;
- nets/pads;
- PRDX save/reopen;
- regressão do fixture inline.

Não criar uma matriz enorme de testes de Specctra antes de existir necessidade concreta.

---

## 14. Checklist de handoff

### Antes de começar

- [ ] Ler `/AGENTS.md`.
- [ ] Ler documentação e planos obrigatórios.
- [ ] Confirmar HEAD `fb73a61`.
- [ ] Buildar/testar o estado atual.
- [ ] Entender `SpecctraDsnImporter` e seus dois caminhos de importação.
- [ ] Ler o fixture `specctra-baseline.dsn`.

### Antes de liberar PLAN-05

- [ ] Obter DSN real exportado pelo EasyEDA Pro.
- [ ] Importar sem exceptions indevidas.
- [ ] Conferir componentes/reference designators.
- [ ] Conferir shared footprint/image.
- [ ] Conferir pad numbers/geometria.
- [ ] Conferir placement/side/rotation.
- [ ] Conferir nets/endpoints.
- [ ] Conferir board/layers/boundary.
- [ ] Conferir units/resolution.
- [ ] Registrar diagnostics de dados não suportados.
- [ ] Salvar PRDX.
- [ ] Reabrir PRDX.
- [ ] Confirmar preservação do canonical design.
- [ ] Rodar testes relevantes.
- [ ] Só então liberar PLAN-05.

### Ao concluir o handoff DSN

O agente deve entregar um resumo objetivo contendo:

- arquivo real usado;
- recursos DSN efetivamente suportados;
- recursos deliberadamente não suportados;
- diagnostics gerados;
- resultado import -> PRDX -> reopen;
- testes executados;
- confirmação explícita de `PLAN-05 READY` ou `PLAN-05 BLOCKED`.

---

## 15. Regra final para o próximo Dev

Não trate o estado atual como um protótipo descartável.

A fundação de Domain, PRDX, constraints, transactions e Desktop já foi construída para sustentar as próximas etapas. O objetivo agora é **avançar**, não recomeçar.

A próxima unidade de trabalho é pequena e objetiva: **provar/corrigir a interoperabilidade DSN real do EasyEDA**. Depois disso, executar o PLAN-05 integralmente, do início ao fim, conforme `/AGENTS.md`.
