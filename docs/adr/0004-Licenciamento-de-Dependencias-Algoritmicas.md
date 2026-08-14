# ADR-0004 — Gate de licenciamento para dependências algorítmicas

## Status

Accepted.

## Contexto

O Place&Router pretende reutilizar bibliotecas e algoritmos maduros sempre que isso reduzir risco e trabalho próprio. Entretanto, reutilizar teoria, usar um projeto como benchmark e incorporar código são decisões diferentes.

## Decisão

Toda dependência algorítmica passa por um gate explícito antes de entrar no produto.

Classificação:

```text
A. Incorporated library
B. Algorithmic/reference material only
C. External benchmark only
```

### Incorporated library

Exige:

- licença compatível com a política futura do projeto;
- análise de distribuição/linking;
- versão pinada;
- boundary arquitetural substituível;
- tests próprios;
- registro de provenance/licença.

### Algorithmic/reference material

Papers, documentação e código público podem orientar implementação própria somente dentro das permissões legais aplicáveis. Não copiar implementação incompatível por conveniência.

### External benchmark

Ferramentas podem ser executadas externamente para comparar resultados sem se tornarem dependência do produto.

## Decisões iniciais

- Clipper2: strong candidate; confirmar licença/versão no bootstrap antes de incorporar.
- NetTopologySuite: candidate; confirmar licença/versão no bootstrap antes de incorporar.
- Google OR-Tools: optional candidate; confirmar licença/versão no bootstrap antes de incorporar.
- Freerouting: benchmark/reference only por default; o repositório consultado declara GPL-3.0, portanto seu código não deve ser incorporado sem decisão explícita de licenciamento.
- NJsonSchema 11.6.1: incorporated library para validação JSON Schema no boundary PRDX; licença MIT; versão pinada em `Directory.Packages.props`; isolada atrás de `IPrdxSchemaValidator` para permitir substituição caso o subset Draft 2020-12 exigido deixe de ser atendido.

## Consequência

A arquitetura usa interfaces próprias (`IGeometryKernel`, spatial/search strategies etc.) para evitar que a troca de uma biblioteca altere o Domain Model.
