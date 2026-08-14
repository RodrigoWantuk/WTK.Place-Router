# WTK.Place&Router

WTK.Place&Router é um projeto de automação de **PCB physical design** com foco em otimização conjunta e iterativa de **component placement + routing**, orientada por regras explícitas de engenharia, busca geométrica e um agente de IA com ferramentas.

O projeto **não pretende, inicialmente, substituir o EDA usado para criar o circuito eletrônico**. O esquemático, a seleção de componentes e a conectividade continuam sendo definidos em ferramentas externas como EasyEDA, KiCad, Altium, OrCAD/Allegro ou equivalentes. O Place&Router recebe esses dados, permite ao usuário enriquecer o projeto com constraints elétricas, físicas, térmicas, mecânicas e de fabricação e, só então, executa o physical design.

## Princípios centrais

- Placement e routing são tratados como um único problema físico codependente, não como duas etapas independentes.
- A IA não é a autoridade de validade da PCB; regras, geometria, DRC e métricas determinísticas são a fonte de verdade.
- O estado completo da placa fica fora do prompt. O agente consulta e modifica o `BoardState` através de tools estruturadas.
- Cada alteração relevante pode ser tratada como uma transação, avaliada por regressão e aceita, reparada ou revertida.
- Hard constraints nunca são transformadas apenas em penalidades de score: uma solução inválida continua inválida.
- O usuário deve conseguir expressar intenção elétrica e física visualmente, sem editar arquivos de regras manualmente.
- O primeiro objetivo é demonstrar um ciclo autônomo completo e verificável em placas pequenas/médias, não substituir um layout engineer em qualquer classe de PCB desde a primeira versão.

## Documentação inicial

1. [Visão geral, escopo e princípios](docs/00-Visao-Geral-e-Principios.md)
2. [Interoperabilidade, importação e modelo canônico](docs/01-Interoperabilidade-e-Modelo-Canonico.md)
3. [Interface e Constraint Workspace](docs/02-Interface-e-Constraint-Authoring.md)
4. [Modelo de domínio e sistema de constraints](docs/03-Modelo-de-Dominio-e-Constraints.md)
5. [Physical Design Optimizer: placement + routing conjuntos](docs/04-Physical-Design-Optimizer.md)
6. [Agente de IA, revisão, memória e explainability](docs/05-Agente-IA-Revisao-e-Memoria.md)
7. [Roadmap técnico, experimento inicial e critérios de sucesso](docs/06-Roadmap-e-Criterios-de-Sucesso.md)

## Estado

A documentação atual é um **plano conceitual inicial**. Ela registra decisões, hipóteses e direções de arquitetura discutidas antes do início da implementação. Itens ainda não validados experimentalmente devem ser tratados como hipóteses de engenharia, não como garantias de produto.
