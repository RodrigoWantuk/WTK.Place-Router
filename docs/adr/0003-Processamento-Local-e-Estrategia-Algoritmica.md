# ADR-0003 — Processamento local e estratégia algorítmica

## Status

Accepted as architectural direction; individual library/parameter choices remain benchmark-gated where indicated.

## Contexto

O Place&Router precisa minimizar input manual e não pode depender da IA cloud para operações geométricas, routing, DRC ou search numérico.

A documentação já separava IA e engine determinístico, mas ainda faltava declarar quais famílias de algoritmos locais seriam a base inicial e em que ordem seriam acionadas.

## Decisão

Adotar a estratégia detalhada em `../10-Processamento-Local-e-Algoritmos-Deterministicos.md`.

A ordem de preferência é:

```text
algoritmo clássico / biblioteca madura
        ↓
composição própria sobre primitives conhecidas
        ↓
algoritmo custom somente quando benchmark justificar
        ↓
LLM apenas para ambiguidade semântica/estratégica
```

### Fundação

- coordenadas físicas canônicas inteiras de 64 bits, inicialmente em micrômetros;
- geometry kernel abstrato, com Clipper2 como strong candidate para clipping/offsetting;
- spatial index abstrato, com Quadtree dinâmico do NetTopologySuite como candidate inicial.

### Placement

- fixed/mechanical-first;
- coarse seed orientado por regiões + conectividade;
- legalization;
- fast multi-fidelity evaluation;
- Large Neighborhood Search como estrutura de reotimização;
- Simulated Annealing como mecanismo inicial de aceitação/exploração dentro dos neighborhoods.

### Routing

- separar global routing de detailed routing;
- grid coarse de capacidade/reservas;
- HPWL → RMST/RSMT para estimativas/topologia;
- A*/Dijkstra para route guides;
- negotiated congestion PathFinder-like;
- pin-access analysis;
- A* 2.5D como detailed route search inicial;
- obstacle inflation para clearance;
- rip-up/reroute com escalada progressiva;
- placement repair quando o routing comprovar bloqueio estrutural.

### Constraints

- deterministic effective-constraint resolution;
- evaluator registry;
- hard validity separada de quality score;
- CP-SAT/OR-Tools apenas como solver opcional para subproblemas discretos bem delimitados.

### Usabilidade

- importar/derivar antes de perguntar;
- unknown permanece válido;
- missing-information dependency analysis decide o que realmente precisa ser perguntado;
- parâmetros internos de routing/search não são expostos ao usuário comum;
- perfis de intenção substituem tuning manual.

### IA

A IA não executa inner-loop geométrico/numerico. Ela pode:

- enriquecer semântica;
- sugerir constraints;
- escolher foco/neighborhood;
- diagnosticar failures;
- propor classe de repair;
- fazer review semântico.

Toda ação continua sujeita ao engine local.

## Consequências

### Positivas

- reduz custo e latency de IA;
- permite operação determinística/headless sem cloud;
- reduz formulários e tuning técnico para o usuário;
- reaproveita décadas de teoria de routing/search;
- cria benchmarks objetivos por Strategy.

### Custos

- será necessário construir e testar um router próprio;
- abstrações de geometry/spatial/search adicionam trabalho inicial;
- parâmetros de SA/LNS/global routing exigem calibração experimental;
- algumas bibliotecas candidatas podem ser substituídas após profiling.

## Licenciamento

Código de terceiros só pode ser incorporado depois de verificar compatibilidade de licença.

Freerouting é tratado como referência/benchmark; seu código GPL-3.0 não deve ser incorporado por default.

## Referência

Ver `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md` para a especificação operacional completa.
