# AGENTS.md — WTK.Place&Router

Este arquivo define as regras operacionais para agentes de IA que planejam, implementam, revisam ou testam o WTK.Place&Router.

Estas instruções se aplicam ao repositório inteiro, salvo quando um `AGENTS.md` mais específico em um subdiretório definir regras adicionais compatíveis.

---

## 1. Regra principal: implementação sempre parte de um plano aprovado

Agentes que alteram código **não trabalham a partir de uma ideia informal, conversa solta, TODO isolado ou interpretação própria do roadmap**.

Toda implementação deve partir de um plano pré-escrito e aprovado localizado em:

```text
/plan
```

Antes de modificar código, o agente deve:

1. identificar o arquivo de plano que rege a tarefa;
2. ler o plano inteiro, não apenas o item aparentemente relacionado;
3. confirmar que ele está aprovado para execução;
4. identificar dependências, entregáveis e critérios de conclusão;
5. ler a documentação arquitetural e os ADRs relacionados;
6. somente então iniciar a implementação.

Se não existir plano aprovado para a tarefa, o agente de implementação **não deve inventar um plano implicitamente e começar a codificar**.

Um agente explicitamente encarregado de planejamento pode criar ou revisar arquivos em `/plan`, mas isso é uma atividade distinta de executar um plano ainda não aprovado.

---

## 2. Plano e documentação são contratos de implementação

O agente deve se manter fiel simultaneamente a:

1. instrução atual do usuário;
2. plano aprovado aplicável;
3. ADRs aceitos;
4. documentação Markdown do repositório;
5. schemas e contracts formais existentes;
6. comportamento já implementado que não contradiga os itens acima.

A documentação existente não é texto meramente informativo. Ela define a arquitetura e as invariantes do produto.

Arquivos especialmente relevantes incluem:

```text
README.md
/docs/00-Visao-Geral-e-Principios.md
/docs/01-Interoperabilidade-e-Modelo-Canonico.md
/docs/02-Interface-e-Constraint-Authoring.md
/docs/03-Modelo-de-Dominio-e-Constraints.md
/docs/04-Physical-Design-Optimizer.md
/docs/05-Agente-IA-Revisao-e-Memoria.md
/docs/06-Roadmap-e-Criterios-de-Sucesso.md
/docs/07-Arquitetura-da-Interface.md
/docs/08-Protocolo-de-Iteracoes-com-IA.md
/docs/09-Decisoes-Arquiteturais-e-Terminologia.md
/docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md
/docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md
/docs/adr/*
/schemas/*
```

O agente deve ler **os documentos relevantes ao escopo da tarefa**, e não precisa reler mecanicamente todos os arquivos em cada mudança trivial. Entretanto, não pode implementar uma área sem conhecer os contracts e decisões que a governam.

### Conflito entre plano e documentação

Não escolher silenciosamente um lado.

Se um plano aprovado contradizer uma decisão `Accepted`, ADR ou schema vigente de maneira material:

- não improvisar uma terceira solução;
- não alterar silenciosamente a arquitetura;
- identificar o conflito objetivamente;
- interromper somente o trecho realmente bloqueado;
- solicitar resolução do conflito quando ela for necessária para continuar corretamente.

Pequenas lacunas implementacionais podem ser resolvidas pelo agente quando não mudarem invariantes, contratos públicos ou arquitetura.

---

## 3. Princípios arquiteturais que não podem ser quebrados por conveniência

O agente deve preservar as decisões vigentes, entre elas:

- `PhysicalDesignState` é o estado físico canônico de placement + routing;
- placement e routing são codependentes e podem reabrir decisões anteriores;
- hard constraints definem validade e não são compensadas por score;
- geometria, DRC, routing, medidas e validade pertencem ao engine determinístico local;
- a IA cloud não executa o numerical inner loop e não é autoridade de validade física;
- interações com IA usam `AgentOperation` tipada/versionada e contracts estruturados;
- DeepSeek é o provider inicial, mas provider não pode contaminar Domain/Application;
- o engine deve permanecer headless e independente de Avalonia;
- MVVM pertence à Presentation Layer, não ao domínio;
- a UI não deve executar diretamente routing, geometry ou chamadas de provider;
- mudanças físicas relevantes usam `PhysicalDesignTransaction` e devem ser reversíveis/auditáveis;
- edição manual usa dependency-driven invalidation, não rollback cronológico cego;
- PRDX é o formato nativo de projeto e deve respeitar seus schemas/versionamento;
- workspace, project, run e cache são persistências diferentes;
- IDs internos estáveis não devem ser substituídos por reference designators como identidade;
- `Unknown` é um estado válido e o usuário só deve ser solicitado quando o dado for material;
- processamento local deve priorizar algoritmos clássicos e bibliotecas maduras compatíveis com o licenciamento do projeto;
- dependências incorporadas passam pelo gate de licença definido nos ADRs.

Quando houver dúvida, a fonte de verdade é a documentação vigente do repositório, não a memória do agente.

---

## 4. Entregas devem ser grandes, mensuráveis e completas

Agentes de implementação **não devem trabalhar em microentregas**.

Não é aceitável encerrar uma execução com apenas:

- criação de interfaces sem implementação;
- criação de pastas/projetos vazios;
- um único DTO de uma cadeia maior prevista no plano;
- scaffold sem fluxo funcional;
- um primeiro teste isolado quando o plano pede uma feature;
- implementação parcial seguida de uma lista de “próximos passos” que já estavam no plano;
- microfeedback pedindo aprovação entre etapas que já foram previamente aprovadas no plano.

O plano aprovado já é a autorização para executar o escopo descrito nele.

O agente deve seguir o plano **do início ao fim da unidade de entrega aprovada**, implementando todas as partes necessárias para produzir resultado observável e mensurável.

### Exemplo

Se o plano aprovado disser:

```text
Implementar PRDX loader v0.1:
- abrir container ZIP;
- validar manifest;
- validar project JSON;
- desserializar;
- executar integrity validation;
- expor diagnostics;
- adicionar round-trip fixture;
```

não é aceitável parar depois de criar:

```text
IPrdxLoader
PrdxLoader.cs
```

A entrega termina quando o fluxo planejado estiver funcional no nível definido pelo plano.

---

## 5. Trabalhar continuamente até concluir o plano

Durante uma tarefa aprovada, o agente deve avançar autonomamente por todas as etapas executáveis.

Não pedir confirmação entre passos já previstos pelo plano.

Não transformar cada arquivo alterado em um checkpoint de decisão humana.

Não interromper a implementação apenas porque:

- uma primeira parte compilou;
- o primeiro teste passou;
- um componente foi criado;
- uma etapa intermediária ficou demonstrável.

Interromper antes do fim somente por um bloqueio real, por exemplo:

- contradição arquitetural que exige decisão humana;
- credencial/secret não disponível e indispensável;
- dependência externa inacessível sem alternativa razoável;
- operação destrutiva ou de alto risco não autorizada;
- informação ausente que muda materialmente o contrato ou comportamento esperado.

Quando um bloqueio atingir apenas parte do escopo, concluir primeiro tudo que puder ser feito corretamente sem ele.

---

## 6. Prioridade: funcionalidade e velocidade de entrega

Salvo quando o plano ou o usuário solicitar explicitamente uma fase de testes, hardening, benchmark ou QA aprofundado, a prioridade é:

```text
correção arquitetural
+ funcionalidade integrada
+ velocidade de entrega
```

Não transformar uma tarefa de implementação em um projeto de testes.

Evitar gastar a maior parte do tempo em:

- matrizes enormes de testes para comportamento trivial;
- mocks excessivos;
- abstrações criadas apenas para facilitar testes sem necessidade arquitetural;
- centenas de casos redundantes;
- cobertura percentual como objetivo por si só;
- refactors não relacionados ao plano.

---

## 7. Testes: obrigatórios, mas proporcionais

Testes devem existir para comportamento novo ou alterado, mas por padrão devem ser **rápidos, diretos e de alto valor**.

Priorizar:

- happy path principal;
- um ou poucos failure paths relevantes;
- invariantes arquiteturais importantes;
- contracts públicos;
- schemas/serialização;
- regressões prováveis da mudança;
- casos determinísticos do geometry/constraint/routing engine quando pertinentes.

Preferir testes pequenos e determinísticos.

Não duplicar a mesma garantia em muitas camadas sem necessidade.

### Ao concluir uma implementação

Executar, na medida do possível:

1. build dos projetos afetados;
2. testes diretamente relacionados à mudança;
3. smoke/integration test do fluxo entregue quando existir;
4. conjunto mais amplo somente quando houver risco razoável de regressão transversal ou quando solicitado pelo plano.

Se o plano for explicitamente de testes/QA/benchmark, esta regra muda: nesse caso o foco passa a ser a profundidade de validação definida no plano.

---

## 8. Não criar arquitetura especulativa fora do plano

Não implementar antecipadamente features futuras apenas porque parecem úteis.

Não criar abstrações genéricas para cenários hipotéticos se o plano atual não exige isso e a documentação não estabelece o contract.

O projeto deve permanecer extensível, mas a implementação deve resolver o problema aprovado de forma concreta.

Exemplos a evitar:

- plugin framework antes de existir necessidade de plugins;
- distributed optimization antes de existir execução local completa;
- MCTS/ML antes dos estágios definidos no roadmap;
- provider-specific types vazando para o domínio “para facilitar agora”;
- sistemas de eventos/frameworks pesados onde domain events simples bastam.

---

## 9. Reuso e dependências

Antes de implementar algoritmos fundamentais do zero, verificar a documentação de processamento local e ADRs relacionados.

A estratégia preferida é:

```text
algoritmo consolidado / biblioteca madura e compatível
→ adaptação pequena e encapsulada
→ algoritmo próprio somente quando houver motivo mensurável
```

Toda dependência nova precisa:

- ter função clara no plano;
- possuir licença compatível com o projeto;
- ficar atrás da boundary adequada quando a documentação exigir substituibilidade;
- não contaminar o modelo canônico com tipos proprietários da biblioteca.

Não copiar código de projetos usados apenas como referência/benchmark quando a licença não permitir incorporação.

---

## 10. Persistência e contracts

Ao alterar PRDX, import/export, persistence ou schemas:

- preservar versionamento explícito;
- atualizar schema e fixtures quando necessário;
- não serializar caches/runtime internals no `.prdx`;
- não colocar credentials no projeto;
- manter migrations/backward compatibility conforme os contracts vigentes;
- validar referências cruzadas que JSON Schema sozinho não consegue validar;
- manter `project`, `workspace`, `run` e `cache` separados.

Mudanças incompatíveis de formato não devem ser introduzidas incidentalmente dentro de outra feature.

---

## 11. Alterações manuais e estado derivado

Implementações relacionadas a edição física devem respeitar:

```text
PhysicalDesignTransaction
→ EditImpactPlanner
→ DependencyGraph
→ selective invalidation
→ minimum recovery pipeline
→ regenerated findings/metrics
```

Não recalcular a placa inteira por simplicidade quando o contract exigir recomputação incremental, salvo numa primeira implementação explicitamente permitida pelo plano como fallback temporário mensurável.

Não desfazer automaticamente uma ação explícita do usuário apenas porque ela criou um estado temporariamente inválido. Gerar findings e bloquear sign-off/fabricação quando necessário.

---

## 12. Qualidade de código

Mesmo com prioridade de velocidade:

- não deixar código deliberadamente quebrado;
- não silenciar exceptions relevantes;
- não esconder TODOs críticos em paths considerados concluídos;
- não usar números mágicos quando já existe configuração/domain value apropriado;
- manter fronteiras entre Presentation, Application, Domain e Infrastructure;
- manter cancellation onde operações longas exigirem;
- preferir diagnostics tipados a exceptions genéricas para problemas esperados do design;
- evitar state global mutável sem necessidade;
- preservar determinismo onde o engine depende dele;
- registrar seed/config quando algoritmos estocásticos fizerem parte de uma run reproduzível.

---

## 13. Atualização da documentação

Não reescrever documentação arquitetural apenas para fazê-la coincidir com uma implementação divergente.

Se uma mudança aprovada alterar um contract documentado, atualizar o documento/ADR/schema correspondente como parte da mesma entrega quando o plano exigir ou quando a mudança não puder ser descrita corretamente sem isso.

Não transformar cada detalhe interno em documentação extensa.

Documentar decisões duráveis, contracts, formatos, invariantes e comportamento necessário para futuras implementações.

---

## 14. Critério de conclusão

Uma tarefa de implementação é considerada concluída quando:

- todos os itens executáveis do plano aprovado foram implementados;
- os critérios de aceitação do plano foram atendidos;
- o fluxo principal está integrado, não apenas scaffolded;
- build relevante passa;
- testes proporcionais à mudança passam;
- diagnostics/failure behavior essenciais existem;
- schemas/docs/contracts foram atualizados quando o plano exigia;
- não ficaram etapas conhecidas do próprio plano artificialmente empurradas para “próximo agente”.

Ao finalizar, o agente deve produzir um resumo **de entrega**, não um diário de microações.

O resumo final deve informar de forma objetiva:

- plano executado;
- capacidade funcional entregue;
- principais áreas/arquivos alterados;
- validações executadas e resultado;
- qualquer item realmente bloqueado ou deliberadamente fora do escopo.

---

## 15. Para agentes de revisão e teste

Agentes explicitamente encarregados de review/testes também devem começar pelo plano aprovado correspondente e pela documentação relevante.

Eles devem avaliar o software contra:

```text
approved plan
+ documented contracts
+ accepted ADRs
+ schemas
+ observable behavior
```

Não considerar uma implementação correta apenas porque compila.

Não abrir findings por preferências pessoais de estilo quando a implementação respeita o plano e os contracts.

Priorizar findings que sejam:

- funcionais;
- arquiteturais;
- de integridade de dados;
- de regressão;
- de determinismo/reprodutibilidade;
- de segurança/persistência;
- de performance material para o fluxo planejado.

---

## 16. Regra final

O objetivo dos agentes neste repositório é **entregar blocos funcionais completos seguindo planos previamente aprovados**, preservando a arquitetura estabelecida e usando testes suficientes para sustentar a entrega sem deixar o desenvolvimento preso em microiterações.

Em forma curta:

```text
READ APPROVED PLAN
→ READ RELEVANT DOCS
→ IMPLEMENT THE WHOLE APPROVED UNIT
→ RUN TARGETED VALIDATION
→ DELIVER MEASURABLE FUNCTIONALITY
```
