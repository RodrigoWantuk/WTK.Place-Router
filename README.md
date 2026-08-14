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

## ADRs

- [ADR-0001 — DeepSeek como provider inicial de IA](docs/adr/0001-DeepSeek-como-Provider-Inicial.md)
- [ADR-0002 — Stack desktop e fronteiras arquiteturais](docs/adr/0002-Stack-Desktop-e-Fronteiras-Arquiteturais.md)
- [ADR-0003 — Processamento local e estratégia algorítmica](docs/adr/0003-Processamento-Local-e-Estrategia-Algoritmica.md)

## Estado

A documentação atual é um **plano conceitual e arquitetural em consolidação**. Ela registra decisões, hipóteses, contracts conceituais e direções de implementação discutidas antes do início do código principal. Itens marcados como candidatos/benchmark-gated devem ser validados experimentalmente antes de serem tratados como escolha final de implementação.
