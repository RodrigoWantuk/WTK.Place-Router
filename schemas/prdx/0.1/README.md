# PRDX 0.1 schemas

Contracts formais iniciais do formato de projeto WTK.Place&Router.

## Arquivos

- `prdx-manifest.schema.json` — valida `manifest.json` do container `.prdx`;
- `prdx-project.schema.json` — valida o payload canônico `project.json`;
- `examples/minimal-2layer.project.json` — fixture mínimo de referência.

## Regras de implementação

A primeira solution deve possuir testes automatizados que:

1. validem todos os fixtures contra JSON Schema Draft 2020-12;
2. desserializem `project.json` para Domain/Application DTOs;
3. serializem novamente;
4. validem o output;
5. comparem semanticamente o round-trip;
6. verifiquem referências internas (`ComponentId`, `PadId`, `NetId`, layer IDs etc.);
7. rejeitem IDs duplicados e references órfãs mesmo quando JSON Schema sozinho não puder expressar a regra de integridade;
8. testem migrations de toda versão antiga suportada para a versão corrente.

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

## Compatibility

Durante `0.x`, migrations continuam obrigatórias para mudanças persistentes.

Depois de `1.0`, breaking changes devem incrementar major version e possuir política explícita de abertura/migration/read-only.
