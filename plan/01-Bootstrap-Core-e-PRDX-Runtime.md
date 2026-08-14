# PLAN-01 — Bootstrap, Core e PRDX Runtime

**Status:** APPROVED  
**Pré-requisitos:** nenhum plano de implementação  
**Desbloqueia:** PLAN-02, e indiretamente todos os demais  
**Tipo de entrega:** fundação executável completa, não scaffold

---

## 1. Instrução ao agente

Você está implementando a fundação do **WTK.Place&Router**, ferramenta desktop de PCB physical design. O circuito eletrônico nasce em um EDA externo; o Place&Router internaliza o design no formato nativo PRDX e executa o physical design em engine headless.

Antes de alterar código:

1. leia `/AGENTS.md`;
2. leia `/plan/00-ROADMAP-MESTRE-V0.1.md`;
3. leia integralmente os documentos obrigatórios abaixo;
4. inspecione `/schemas/prdx/0.1/` e seus fixtures;
5. trate schemas e ADRs Accepted como contracts;
6. execute este plano inteiro. Não encerre após criar solution/interfaces/scaffold.

### Documentos obrigatórios

- `README.md`
- `docs/00-Visao-Geral-e-Principios.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0002-Stack-Desktop-e-Fronteiras-Arquiteturais.md`
- `docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md`
- `schemas/prdx/0.1/prdx-manifest.schema.json`
- `schemas/prdx/0.1/prdx-project.schema.json`
- `schemas/prdx/0.1/README.md`
- todos os exemplos em `schemas/prdx/0.1/examples/`

---

## 2. Objetivo mensurável

Ao final, o repositório deve possuir uma solution C#/.NET real capaz de:

```text
open .prdx
→ validate ZIP/manifest/hash/schema
→ deserialize canonical project
→ validate basic cross references
→ expose typed diagnostics
→ modify simple project metadata/state
→ save atomically to .prdx
→ reopen
→ preserve semantic equivalence
```

Também deve existir CLI/headless path que valide um PRDX sem iniciar Avalonia.

---

## 3. Estrutura de projetos

Criar uma solution coerente com os boundaries documentados. Nomes podem ser refinados se necessário, mas a separação mínima deve ficar equivalente a:

```text
src/
  PlaceRouter.Core
  PlaceRouter.Domain
  PlaceRouter.Application
  PlaceRouter.Infrastructure
  PlaceRouter.DesignExchange
  PlaceRouter.Cli

tests/
  PlaceRouter.Core.Tests
  PlaceRouter.Domain.Tests
  PlaceRouter.DesignExchange.Tests
```

Não criar ainda projetos vazios para todos os módulos futuros apenas para “preparar”. Crie os projetos necessários para entregar este plano e boundaries que já são estáveis.

### Dependências permitidas conceitualmente

```text
Core                → nada específico do produto/UI/provider
Domain              → Core
Application         → Domain/Core + ports
DesignExchange      → Domain/Core
Infrastructure      → ports de Application/Domain quando necessário
CLI                 → Application + composition root
Tests               → projetos alvo
```

Domain não referencia Avalonia, DeepSeek ou formato EasyEDA.

---

## 4. Fixar toolchain inicial

Escolher versões estáveis/suportadas de .NET e packages necessários no momento da implementação, registrar de forma central e consistente.

Criar:

- `global.json` quando útil;
- central package management se fizer sentido para a solution;
- nullable enabled;
- analyzers básicos apenas se não prejudicarem velocidade;
- deterministic builds quando disponível;
- formatting/editor settings mínimos.

Não gastar o plano montando uma infraestrutura de lint excessiva.

---

## 5. Core primitives

Implementar tipos fundamentais usados pelo PRDX e pelo restante do domínio:

- stable strongly-typed IDs ou wrappers equivalentes;
- length coordinate based on `Int64` com unidade canônica 1 µm;
- angle/rotation representation explícita;
- layer identifiers;
- timestamps/versions onde pertinente;
- result/diagnostic primitives;
- provenance + value status (`Known`, `Inferred`, `Unknown`, `NotApplicable` ou representação equivalente consistente com schemas/docs).

Evitar `double` sem semântica para coordenada física canônica.

Não sobreengenheirar unidades elétricas ainda; implemente o necessário para desserializar e preservar o schema v0.1 de forma segura.

---

## 6. Canonical model v0.1

Criar tipos de domínio/serialization models suficientes para representar todo o `prdx-project.schema.json`, incluindo ao menos:

- project metadata/revision;
- source imports;
- components;
- footprints;
- pads;
- nets/net endpoints;
- board/outline;
- stackup/layers;
- manufacturing snapshot/profile;
- groups/regions;
- constraints/selectors;
- semantics/relationships;
- accepted `PhysicalDesignState`;
- component poses;
- routes/tracks/vias/copper zones;
- review decisions;
- project settings/export/optimization profiles presentes no schema.

### Regra

O runtime model não precisa espelhar cegamente JSON property por property se isso prejudicar o domínio. Porém:

- o formato persistente precisa round-trip sem perda;
- IDs/referências precisam permanecer estáveis;
- unknown/provenance precisam ser preservados;
- tipos de transporte não devem contaminar todo o domínio.

Use DTO/serialization layer quando necessário, mas não crie duplicação massiva sem motivo.

---

## 7. PRDX container reader

Implementar `IPrdxProjectReader` ou contract equivalente.

Fluxo obrigatório:

```text
file path/stream
→ verify ZIP structure
→ read manifest.json
→ validate manifest schema
→ locate canonical payload
→ hash payload and compare manifest
→ validate project JSON schema
→ deserialize
→ run canonical integrity validation
→ return ProjectLoadResult + diagnostics
```

### Diagnostics

Problemas esperados não devem emergir apenas como exception crua.

Exemplos de codes/categorias:

```text
PRDX-CONTAINER-INVALID
PRDX-MANIFEST-MISSING
PRDX-MANIFEST-SCHEMA
PRDX-PAYLOAD-MISSING
PRDX-PAYLOAD-HASH
PRDX-PROJECT-SCHEMA
PRDX-REF-NOT-FOUND
PRDX-LAYER-NOT-FOUND
PRDX-PAD-FOOTPRINT-MISMATCH
```

Exceções inesperadas podem ser encapsuladas em failure técnico, preservando detalhes para log.

---

## 8. JSON Schema validation

Usar biblioteca .NET madura/licença compatível para JSON Schema Draft 2020-12 ou implementar boundary que permita substituição.

Não reimplementar JSON Schema do zero.

Carregar/embutir os schemas de forma que:

- CLI/tests encontrem-nos;
- aplicação futura não dependa do working directory;
- versionamento seja explícito.

Validar manifest e project antes de materializar um estado considerado confiável.

---

## 9. Canonical integrity validator

Implementar validações que JSON Schema não cobre bem:

- IDs únicos onde exigido;
- component.footprintId existe;
- net endpoints referenciam component/pad existentes;
- pad pertence ao footprint esperado;
- board/layer references existem;
- route.netId existe;
- track/via layers existem e são compatíveis;
- poses referenciam componentes existentes;
- selectors de constraints referenciam entidades válidas quando o selector for nominal;
- semantic relationships referenciam entidades existentes;
- duplicate/invalid internal IDs geram erro.

Não tentar implementar ainda DRC físico completo. Este validator é de **integridade estrutural canônica**.

---

## 10. PRDX writer e atomic save

Implementar writer que:

1. serializa canonical project;
2. gera payload determinístico o suficiente para hash/replay quando possível;
3. calcula SHA-256;
4. gera manifest;
5. escreve ZIP temporário;
6. reabre/valida estrutura essencial do arquivo temporário;
7. substitui destino atomicamente quando suportado, com fallback seguro;
8. não deixa projeto original corrompido em falha intermediária.

Preservar `source/`, `assets/` e attachments conforme API suportada, ainda que a primeira fixture não os use.

Não persistir workspace/cache/run internals no `.prdx`.

---

## 11. Project service mínimo

Criar use cases/Application services coarse-grained para:

```text
CreateProject
LoadProject
SaveProject
ValidateProject
```

Não expor details do ZipArchive/JSON library para Presentation/CLI.

Definir um `ProjectSession`/equivalente apenas se necessário para representar projeto carregado + revision/dirty state, sem implementar ainda toda a lifecycle de PLAN-03.

---

## 12. CLI mínima

Entregar comando headless funcional, por exemplo:

```text
placerouter validate <file.prdx>
placerouter inspect <file.prdx>
```

Saída humana simples e opção JSON se isso puder ser implementado sem desviar escopo.

Exit codes precisam distinguir pelo menos:

- success;
- invalid project/input;
- unexpected/internal failure.

Não construir framework CLI complexo.

---

## 13. Fixtures e testes mínimos

Usar os fixtures versionados em `/schemas/prdx/0.1/examples`.

Testes obrigatórios e diretos:

1. schema fixture válido passa;
2. load do fixture válido produz domínio esperado;
3. round-trip `load → save → load` preserva equivalência semântica;
4. hash incorreto falha com diagnostic correto;
5. referência cruzada inexistente falha no integrity validator;
6. atomic save não substitui projeto válido por arquivo incompleto quando uma falha simulável ocorrer;
7. CLI validate retorna exit code correto para válido e inválido.

Evitar dezenas de testes redundantes.

---

## 14. CI/build baseline

Adicionar workflow/command reproducível de build e testes rápidos se o repositório ainda não possuir.

Baseline:

```text
dotnet restore
dotnet build
dotnet test targeted solution/tests
```

A validação dos fixtures PRDX deve fazer parte do caminho automatizado normal.

Não criar pipeline de release neste plano.

---

## 15. Fora de escopo

Não implementar aqui:

- geometry boolean engine;
- constraint physical evaluation;
- DSN importer;
- Avalonia UI;
- routing;
- placement optimizer;
- DeepSeek;
- Gerber export;
- full migrations entre versões futuras.

Deixe ports naturais quando realmente necessários, sem criar implementações vazias para esses módulos.

---

## 16. Critérios de aceitação

O plano termina somente quando:

- solution compila limpa no ambiente suportado;
- fixture PRDX oficial é validado e carregado;
- canonical cross references são verificadas;
- writer produz `.prdx` reabrível e hash válido;
- round-trip preserva conteúdo semântico relevante;
- save é seguro/atômico conforme capacidade do SO;
- CLI valida projeto sem UI;
- testes mínimos passam;
- Domain/Core não dependem de Avalonia/provider/EDA.

### Demonstração mensurável

O agente deve conseguir executar algo equivalente a:

```text
placerouter validate schemas/.../minimal.prdx
→ VALID
→ components: N
→ nets: N
→ layers: 2
```

ou gerar um `.prdx` a partir do fixture JSON, salvar, reabrir e demonstrar equivalência.

---

## 17. Relatório final do agente

Ao concluir, informe:

- estrutura real da solution criada;
- versão .NET escolhida;
- reader/writer/validator disponíveis;
- fixture usado;
- comandos de build/test executados;
- resultado do round-trip;
- qualquer diferença inevitável entre schema e runtime model;
- nada de listar “próximos passos” que já pertencem ao PLAN-02.