# PRDX 0.1 schemas

Contracts formais iniciais do formato de projeto WTK.Place&Router.

## Arquivos

- `prdx-manifest.schema.json` — valida `manifest.json` do container `.prdx`;
- `prdx-project.schema.json` — valida o payload canônico `project.json`;
- `examples/minimal-2layer.project.json` — fixture completo mínimo de referência;
- `examples/incomplete-project.project.json` — fixture deliberadamente incompleto, sem board físico resolvido.

## Versão suportada

O runtime PRDX 0.1 suporta exatamente:

```text
formatVersion = 0.1.0
schemaVersion = 0.1.0
featureFlags  = []
```

Versões desconhecidas geram `PRDX-VERSION-UNSUPPORTED`; feature flags desconhecidas geram `PRDX-FEATURE-UNSUPPORTED`.

## Projetos incompletos

PRDX 0.1 permite representar conhecimento ausente sem inventar geometria:

- `board.outline` pode ser `null`;
- `board.layers` e `board.stackup` podem ser arrays vazios;
- `component.footprintId` pode ser `null`;
- `net.endpoints[]` pode preservar conectividade por `pinRef` enquanto `padId` é `null`;
- `physicalDesignState.status` aceita `INCOMPLETE`.

Essas condições podem gerar diagnostics não bloqueantes de readiness/integridade, mas não tornam o arquivo estruturalmente corrompido.

## Regras de implementação

A primeira solution deve possuir testes automatizados que:

1. validem todos os fixtures contra JSON Schema Draft 2020-12;
2. desserializem `project.json` para o Domain tipado;
3. serializem novamente;
4. validem o output;
5. comparem semanticamente o round-trip;
6. verifiquem referências internas (`ComponentId`, `PadId`, `NetId`, layer IDs etc.);
7. rejeitem IDs duplicados e references órfãs mesmo quando JSON Schema sozinho não puder expressar a regra de integridade;
8. testem a política explícita de versão suportada.

## JSON Schema não substitui domain validation

O schema valida estrutura/forma.

Regras como:

```text
PadId realmente pertence ao Component/Footprint referenciado
Net endpoints existem
Route.netId existe
Track.layerId é copper layer
Via transition é permitida pelo stackup
Required constraints são semanticamente válidas
```

são validações de domínio e precisam de `ProjectValidator`/`CanonicalIntegrityValidator` próprios.

## Runtime mapping

O JSON fica confinado ao DesignExchange:

```text
project.json
→ schema validation
→ PRDX mapper
→ CanonicalProject tipado
```

Código de Domain/Application/PLAN-02 não deve navegar `JsonObject`/`JsonNode` para acessar board, layers, nets ou PhysicalDesignState.

## Compatibility

Durante `0.x`, migrations continuam obrigatórias para mudanças persistentes.

Depois de `1.0`, breaking changes devem incrementar major version e possuir política explícita de abertura/migration/read-only.
