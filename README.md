# WTK.Place&Router

WTK.Place&Router é um projeto de automação de **PCB physical design** com foco em otimização conjunta e iterativa de **component placement + routing**, orientada por regras explícitas de engenharia, busca geométrica e um agente de IA com ferramentas.

O projeto **não pretende, inicialmente, substituir o EDA usado para criar o circuito eletrônico**. O esquemático, a seleção de componentes e a conectividade continuam sendo definidos em ferramentas externas como EasyEDA, KiCad, Altium, OrCAD/Allegro ou equivalentes. O Place&Router recebe esses dados, permite ao usuário enriquecer o projeto com constraints elétricas, físicas, térmicas, mecânicas e de fabricação e, só então, executa o physical design.

## Princípios centrais

- Placement e routing são tratados como um único problema físico codependente, não como duas etapas independentes.
- A IA não é a autoridade de validade da PCB; regras, geometria, DRC e métricas determinísticas são a fonte de verdade.
- O estado físico canônico (`PhysicalDesignState`) fica fora do prompt. O agente consulta views estruturadas e propõe ações através de contracts tipados.
- Cada alteração relevante pode ser tratada como uma transação, avaliada por regressão e aceita, reparada ou revertida.
- Hard constraints nunca são transformadas apenas em penalidades de score: uma solução inválida continua inválida.
- O usuário deve conseguir expressar intenção elétrica e física visualmente, sem editar arquivos de regras manualmente.
- O primeiro objetivo é demonstrar um ciclo autônomo completo e verificável em placas pequenas/médias, não substituir um layout engineer em qualquer classe de PCB desde a primeira versão.
- A interface é desktop-first, com C#/.NET + Avalonia + MVVM na Presentation Layer e workspace no estilo IDE/CAD, mantendo o physical-design engine headless e independente da UI.
- Interações com IA são operações tipadas e versionadas: preamble conciso + contexto JSON mínimo + response contract estrito; a IA permanece fora do inner loop numérico e toda proposta é validada pelo engine antes de alterar o estado.
- DeepSeek é o provider inicial de IA, através de uma abstração provider-agnostic; credenciais e secrets não pertencem ao PRDX/design.
- O processamento local deve preferir teoria consolidada e bibliotecas maduras antes de algoritmos custom: geometry/indexing determinísticos, LNS + Simulated Annealing para placement, global routing por capacidade/congestionamento e detailed routing baseado em graph search/rip-up-reroute.
- A UX segue a regra **importar/derivar/inferir antes de perguntar**: o usuário só deve ser interrompido por dados desconhecidos que sejam materialmente necessários para a decisão atual.
- Biblioteca incorporada, referência algorítmica e benchmark externo são categorias distintas; dependências de terceiros passam por gate explícito de licenciamento.
- O formato nativo de projeto é `.prdx`: container ZIP versionado com `manifest.json` + `project.json`, contendo logical design, constraints, semantics, manufacturing assumptions e o `PhysicalDesignState` aceito, incluindo placement e routing.
- Project, workspace, optimization run e cache são persistências distintas. Alterações manuais usam dependency-driven invalidation para reexecutar apenas o menor estágio/scope necessário.
- Export parte sempre do estado canônico e contempla fabricação industrial, round-trip EDA, imagens/documentação e arte 1:1 para processos artesanais/transferência.
- O primeiro handoff físico concreto com EasyEDA Pro usa **Specctra DSN** como entrada baseline; SES é tratado inicialmente como retorno de wires/vias, sem presumir round-trip completo de placement.
- Agentes de implementação trabalham exclusivamente a partir dos planos aprovados em `/plan`, obedecendo `/AGENTS.md`, dependências, documentação e ADRs.

## Documentação inicial

1. [Visão geral, escopo e princípios](docs/00-Visao-Geral-e-Principios.md)
2. [Interoperabilidade, importação e modelo canônico](docs/01-Interoperabilidade-e-Modelo-Canonico.md)
3. [Interface e Constraint Workspace](docs/02-Interface-e-Constraint-Authoring.md)
4. [Modelo de domínio e sistema de constraints](docs/03-Modelo-de-Dominio-e-Constraints.md)
5. [Physical Design Optimizer: placement + routing conjuntos](docs/04-Physical-Design-Optimizer.md)
6. [Agente de IA, revisão, memória e explainability](docs/05-Agente-IA-Revisao-e-Memoria.md)
7. [Roadmap técnico, experimento inicial e critérios de sucesso](docs/06-Roadmap-e-Criterios-de-Sucesso.md)
8. [Arquitetura da interface desktop, docking e workspaces](docs/07-Arquitetura-da-Interface.md)
9. [Protocolo de iterações com IA e fronteira determinística](docs/08-Protocolo-de-Iteracoes-com-IA.md)
10. [Decisões arquiteturais e terminologia](docs/09-Decisoes-Arquiteturais-e-Terminologia.md)
11. [Processamento local e algoritmos determinísticos](docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md)
12. [Formato de projeto, persistência, lifecycle e exportação](docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md)

## Planos de implementação aprovados

A implementação da v0.1 é governada por [`plan/README.md`](plan/README.md) e pelo [`plan/00-ROADMAP-MESTRE-V0.1.md`](plan/00-ROADMAP-MESTRE-V0.1.md).

Sequência de entregas:

1. [Bootstrap, Core e PRDX Runtime](plan/01-Bootstrap-Core-e-PRDX-Runtime.md)
2. [Geometry Kernel, Spatial Index e Constraint Engine](plan/02-Geometry-Spatial-Index-e-Constraint-Engine.md)
3. [Importação, Project Lifecycle, Transactions e Invalidation](plan/03-Importacao-Project-Lifecycle-Transactions-e-Invalidation.md)
4. [Desktop Shell e Experiência Básica de Projeto](plan/04-Desktop-Shell-e-Experiencia-Basica-de-Projeto.md)
5. [Constraint Workspace e Enriquecimento Automático](plan/05-Constraint-Workspace-e-Enriquecimento-Automatico.md)
6. [Fast Evaluation e Global Routing](plan/06-Fast-Evaluation-e-Global-Routing.md)
7. [Detailed Routing, DRC e Rip-up/Reroute](plan/07-Detailed-Routing-DRC-e-Ripup-Reroute.md)
8. [Placement Search, Joint Optimizer e Regression](plan/08-Placement-Search-Joint-Optimizer-e-Regression.md)
9. [Edição Física Interativa e Recovery](plan/09-Edicao-Fisica-Interativa-e-Recovery.md)
10. [Semantics e Agent IA DeepSeek](plan/10-Semantics-e-Agent-IA-DeepSeek.md)
11. [Export Pipeline e Artefatos de Fabricação](plan/11-Export-Pipeline-e-Artefatos-de-Fabricacao.md)
12. [Integração, Produto v0.1 e Release Validation](plan/12-Integracao-Produto-V0.1-e-Release-Validation.md)

A numeração ajuda na navegação, mas o plano mestre define o grafo real de dependências e quais workstreams podem avançar em paralelo.

## Schemas

- [PRDX 0.1 — manifest](schemas/prdx/0.1/prdx-manifest.schema.json)
- [PRDX 0.1 — canonical project](schemas/prdx/0.1/prdx-project.schema.json)
- [PRDX 0.1 — fixtures e validação](schemas/prdx/0.1/README.md)

## ADRs

- [ADR-0001 — DeepSeek como provider inicial de IA](docs/adr/0001-DeepSeek-como-Provider-Inicial.md)
- [ADR-0002 — Stack desktop e fronteiras arquiteturais](docs/adr/0002-Stack-Desktop-e-Fronteiras-Arquiteturais.md)
- [ADR-0003 — Processamento local e estratégia algorítmica](docs/adr/0003-Processamento-Local-e-Estrategia-Algoritmica.md)
- [ADR-0004 — Gate de licenciamento para dependências algorítmicas](docs/adr/0004-Licenciamento-de-Dependencias-Algoritmicas.md)
- [ADR-0005 — PRDX, persistência, lifecycle de edição e exportação](docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md)
- [ADR-0006 — EasyEDA Pro: handoff inicial via Specctra DSN](docs/adr/0006-EasyEDA-Pro-Handoff-Inicial-via-DSN.md)

## Estado

A documentação atual já inclui **arquitetura, contracts formais iniciais e planos de implementação aprovados para a v0.1**. Itens marcados como candidatos/benchmark-gated continuam sujeitos a validação experimental, mas agentes de implementação devem seguir os planos aprovados e os ADRs vigentes em vez de redefinir a arquitetura durante a execução.