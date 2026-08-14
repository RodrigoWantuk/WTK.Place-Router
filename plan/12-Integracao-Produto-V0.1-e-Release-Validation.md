# PLAN-12 — Integração, Produto v0.1 e Release Validation

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-01 a PLAN-11 concluídos  
**Resultado:** primeira versão integrada do WTK.Place&Router

---

## 1. Instrução ao agente

Você está fechando a primeira versão integrada do **WTK.Place&Router**. Esta não é uma fase para criar uma nova arquitetura ou substituir algoritmos já aprovados. Sua função é integrar, corrigir gaps reais, validar fluxos completos, tornar a aplicação distribuível e demonstrar que a tese do produto funciona do import ao export.

Antes de codificar:

1. leia `/AGENTS.md`;
2. leia `/plan/00-ROADMAP-MESTRE-V0.1.md` por completo;
3. confirme que PLAN-01 a PLAN-11 estão realmente implementados no branch base, e não apenas scaffoldados;
4. leia este plano inteiro;
5. leia os documentos arquiteturais centrais listados abaixo;
6. execute primeiro um inventário funcional rápido do estado atual e mapeie gaps contra os critérios deste plano;
7. corrija os gaps dentro das decisões existentes;
8. execute o ciclo completo de validação e produza artefatos distribuíveis.

### Documentos obrigatórios

- `README.md`
- `docs/00-Visao-Geral-e-Principios.md`
- `docs/01-Interoperabilidade-e-Modelo-Canonico.md`
- `docs/02-Interface-e-Constraint-Authoring.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/04-Physical-Design-Optimizer.md`
- `docs/05-Agente-IA-Revisao-e-Memoria.md`
- `docs/06-Roadmap-e-Criterios-de-Sucesso.md`
- `docs/07-Arquitetura-da-Interface.md`
- `docs/08-Protocolo-de-Iteracoes-com-IA.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/10-Processamento-Local-e-Algoritmos-Deterministicos.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/*`
- `schemas/prdx/0.1/*`
- todos os planos `01` a `11`

### Referência desktop

Para qualquer correção de Avalonia/docking/layout durante esta fase, usar também como referência:

`https://github.com/RodrigoWantuk/WTK.MediaForge`

A documentação do Place&Router continua prevalecendo.

---

## 2. Objetivo mensurável

A v0.1 precisa executar, em uma instalação limpa e com um projeto de referência suportado:

```text
Launch
→ Create/Open/Import
→ DSN → PRDX
→ inspect board/components/nets
→ configure/confirm manufacturing + constraints
→ save/reopen
→ run deterministic enrichment
→ run placement + global routing + detailed routing
→ joint repair when routing blocks placement
→ inspect findings/metrics/review
→ optional DeepSeek semantic/strategic assistance
→ manually move/edit placement/routing
→ selective invalidation + recovery
→ save accepted design
→ export Gerber/drill/SES/artwork
```

O resultado precisa ser observável pela GUI e reproduzível em partes críticas via CLI/headless.

---

## 3. Inventário inicial obrigatório

Antes de alterar código, executar uma passagem curta verificando cada capacidade dos PLAN-01..11.

Criar checklist de execução temporário ou issue/log de trabalho com status:

```text
PASS
PARTIAL
BROKEN
MISSING
```

Não replanejar o produto; usar o inventário para corrigir integração.

Exemplos de gaps aceitáveis para esta fase:

- Application service existe, mas GUI não chama corretamente;
- export profile não aparece em project save;
- route recovery perde selection;
- CLI não expõe optimizer existente;
- AI finding não navega ao board;
- package publish perde schemas embedded.

Gaps estruturais que contradizem docs devem ser corrigidos, não contornados.

---

## 4. Fluxo de primeiro uso

A aplicação deve iniciar sem exigir configuração técnica massiva.

Entregar uma experiência clara:

```text
New/Open/Import
→ project created
→ import summary
→ readiness
→ only material questions
→ board workspace
```

Requisitos:

- sem wizard obrigatório de dezenas de campos;
- unknowns não materiais ficam como warnings/opcionais;
- falta de DeepSeek key não bloqueia uso local;
- errors de source/import têm remediation compreensível;
- sample/demo project pode ser oferecido sem contaminar fluxo real.

---

## 5. Settings e secrets

Entregar settings funcionais para:

- provider/model policy quando exposto;
- DeepSeek API key via armazenamento local seguro/plataforma apropriada ou mecanismo explicitamente definido;
- workspace preferences;
- default export destination/profile quando apropriado;
- performance/advanced diagnostics apenas em área avançada.

API key nunca entra em:

- PRDX;
- `.prdxrun` em texto aberto por default;
- logs comuns;
- fixtures;
- repository.

---

## 6. End-to-end reference projects

Montar conjunto pequeno de referência para v0.1, preferencialmente cobrindo categorias documentadas:

- MCU + decoupling/connectors;
- analog/op-amp or ADC frontend;
- buck/simple power block;
- board que força joint routing→placement repair.

Não é necessário ter muitos boards; selecionar poucos com valor alto.

Cada fixture deve declarar:

```text
source
expected import counts
board/layers
known Required constraints
expected supported features
known limitations
```

Evitar fixtures artificiais demais como única validação.

---

## 7. End-to-end test principal

Criar pelo menos um teste/harness automatizado ou semi-automatizado que execute:

```text
DSN fixture
→ import
→ PRDX save
→ reopen
→ readiness
→ optimizer
→ accepted PhysicalDesignState
→ export manufacturing/artwork
```

Validar:

- component/net counts;
- stable IDs/references;
- zero blocking Required violations no estado final esperado;
- routing completion do caso suportado;
- export files gerados e basicamente válidos;
- save/reopen não altera semanticamente o resultado aceito.

Esse fluxo deve rodar headless onde possível para CI.

---

## 8. Joint repair proof obrigatório

Executar novamente o cenário central do PLAN-08 em ambiente integrado.

Resultado precisa registrar algo equivalente a:

```text
initial routing: placement blockage
repair A: routing success + Required regression → REJECT
repair B: routing success + no hard regression → COMMIT
```

Exibir o resultado também pela UI/Optimization/Review quando a run for iniciada pela aplicação.

Esse é o principal critério técnico da tese v0.1.

---

## 9. Manual edit + selective recovery proof

Executar caso integrado:

```text
open routed project
→ move one component
→ only related nets/routes become stale
→ local recovery attempts reroute
→ unrelated routes remain valid
→ findings update
→ undo restores prior state
```

Registrar affected scope antes/depois.

Full-board invalidation só é aceitável quando o engine declarar motivo/fallback explícito.

---

## 10. AI proof

Com Fake provider sempre e DeepSeek real quando secret de teste estiver disponível:

```text
routing/optimization finding
→ AgentOperation
→ valid structured response
→ deterministic action/search
→ measured outcome
```

Também validar modo offline/no key:

- projeto abre;
- import/save/edit/router/optimizer/export funcionam;
- AI commands ficam disabled/unavailable de forma clara.

Não tornar integração real DeepSeek requisito para CI público sem secret.

---

## 11. Export proof

Para estado roteado válido:

### Manufacturing

- Gerber copper/profile;
- drill;
- export report/job metadata conforme profile.

### EDA

- SES wires/vias baseline.

### DIY

- SVG/PDF 1:1;
- PNG/TIFF em DPI explícito;
- bottom mirror;
- polarity;
- calibration mark.

Validar dimensões/coordinates com fixtures.

---

## 12. CLI v0.1 final

Consolidar comandos headless úteis, sem tentar replicar toda GUI.

Baseline desejado:

```text
placerouter validate project.prdx
placerouter inspect project.prdx
placerouter import-dsn source.dsn --out project.prdx
placerouter readiness project.prdx
placerouter optimize project.prdx --out optimized.prdx [--seed ...]
placerouter route project.prdx --out routed.prdx
placerouter export project.prdx --profile <name> --out <dir>
```

Pode haver nomes melhores, mas capabilities devem existir.

### Requisitos

- exit codes claros;
- JSON output option para automação quando útil;
- cancellation via Ctrl+C para operações longas;
- nenhuma dependência de Avalonia para execução.

---

## 13. Run/replay artifact v0.1

Concluir `.prdxrun` ou run store suficientemente para:

- seed/config;
- algorithm versions;
- provider/model;
- AgentOperation logs;
- metric timeline resumida;
- transaction/repair summaries;
- final outcome;
- base project/state revision.

Não precisa armazenar full candidate snapshot para cada iteração.

Adicionar comando/diagnostic capaz de inspecionar uma run.

---

## 14. Performance baseline

Medir em hardware de desenvolvimento/referência disponível:

- project load/save;
- geometry/index build;
- global routing;
- detailed routing;
- optimizer reference run;
- UI responsiveness durante run;
- memory usage aproximada;
- AI calls/cost quando habilitado.

Não criar metas arbitrárias de mercado. Registrar baseline e corrigir apenas problemas claramente impraticáveis, como UI congelada ou explosão de memória em boards alvo.

---

## 15. Reliability / crash recovery

Validar:

- save atômico;
- corrupted/incomplete PRDX não sobrescreve original válido;
- journal recovery depois de crash simulado;
- cancel optimizer/router preserva estado consistente;
- app reopen após run interrompida;
- stale run não sobrescreve edição posterior;
- export failure não altera project state.

Focar nos paths de maior impacto, não fuzzing extenso.

---

## 16. Diagnostics consistency

Revisar diagnostics de:

- import;
- PRDX;
- constraints/readiness;
- routing;
- optimizer;
- AI;
- export.

Objetivo:

- códigos estáveis/claros;
- severity consistente;
- entity refs quando aplicável;
- remediation útil;
- UI navega aos objetos relevantes;
- exceptions inesperadas logadas sem substituir diagnostics esperados.

Não reescrever todo texto só por estilo.

---

## 17. UI integration polish funcional

Corrigir gaps que prejudiquem uso real:

- commands enabled/disabled pelo state correto;
- progress/cancel/pause;
- selection/focus entre panels;
- findings navigation;
- dirty-state indication;
- provider/local execution indication;
- export/readiness blocking status;
- docking restore;
- keyboard shortcuts essenciais;
- high-DPI/multi-monitor smoke.

Evitar redesign visual grande nesta fase.

---

## 18. Packaging

Produzir ao menos builds desktop distribuíveis para plataformas realmente suportadas pela toolchain/CI disponível.

Baseline preferencial da v0.1:

```text
Windows x64 self-contained package
Linux x64 self-contained package
```

macOS deve ao menos compilar quando runner/toolchain disponível e a arquitetura não pode ser deliberadamente quebrada, mas não bloquear a v0.1 se não houver ambiente de assinatura/package/teste.

Pode usar ZIP/tar.gz self-contained como primeiro artefato distribuível; instaladores sofisticados não são requisito desta versão.

Incluir:

- executable/app files;
- required schemas/resources;
- license/third-party notices;
- sample/reference project opcional;
- version metadata.

---

## 19. Versioning

Definir versão de produto v0.1 e garantir consistência entre:

- assemblies/package;
- PRDX writer `applicationVersion`;
- CLI `--version`;
- export report;
- run artifact;
- release artifact names.

Schema PRDX continua versionado independentemente do app.

---

## 20. CI de release

Consolidar workflow que execute no mínimo:

```text
restore
build
fast/unit/contract tests
PRDX schema fixtures
headless end-to-end reference flow
publish distributable artifacts
```

UI smoke automatizado é desejável se já houver infraestrutura estável, mas não transformar esta fase em projeto de UI automation.

Secrets de DeepSeek são opcionais/protegidos e não necessários para build normal.

---

## 21. Third-party notices/licensing

Gerar/revisar inventário das dependências incorporadas:

- package/library;
- version;
- license;
- reason/use;
- notice requirement.

Garantir que nenhuma implementação tenha incorporado código incompatível de benchmarks/referências.

Não iniciar política comercial/licença final do produto se ela ainda for decisão aberta; apenas garantir compliance das dependências usadas.

---

## 22. Documentação do usuário mínima

Adicionar documentação curta e prática para v0.1:

- instalação/execução;
- importar EasyEDA Pro via DSN;
- abrir/salvar PRDX;
- configurar manufacturing/constraints;
- executar optimizer;
- interpretar findings;
- configurar DeepSeek opcional;
- editar placement/routing;
- exportar Gerber/transfer;
- limitações conhecidas.

Não duplicar a documentação arquitetural inteira.

---

## 23. Known limitations

Registrar explicitamente limitações da v0.1, por exemplo se aplicáveis:

- board classes não suportadas;
- differential pair restrictions;
- unsupported pad shapes;
- SES não retorna placement;
- no full IPC-2581;
- no push-and-shove avançado;
- no SI/PI solver completo.

Failure explícito é melhor que comportamento silenciosamente incorreto.

---

## 24. Testes e validação desta fase

Esta é a fase em que testes podem ser mais amplos que o padrão dos planos anteriores.

Obrigatórios:

1. PRDX load/save/migration baseline;
2. DSN import end-to-end;
3. geometry/constraint regression suite existente;
4. global+detailed route fixtures;
5. joint optimizer proof;
6. manual edit/recovery proof;
7. export dimension/conformance checks;
8. fake AI flow;
9. no-key/offline flow;
10. CLI end-to-end;
11. crash/journal scenario principal;
12. desktop smoke: launch/import/select/optimize/edit/export;
13. package launch smoke quando ambiente permitir.

Não perseguir coverage percentual como objetivo.

---

## 25. Critérios de aceitação final da v0.1

A versão está concluída somente quando:

- aplicação desktop inicia em package limpo suportado;
- DSN suportado importa corretamente para PRDX;
- projeto pode ser salvo/reaberto sem perda estrutural/intencional;
- user consegue definir constraints/manufacturing pela UI;
- readiness evita questionário desnecessário;
- local optimizer produz placement/routing em board de referência;
- routing pode reabrir placement e regression rejeita repair ruim;
- hard violations suportadas são zero no candidate aceito de referência;
- manual edit provoca selective recovery correto;
- DeepSeek é opcional e operation-typed;
- manufacturing/artwork exports funcionam;
- CLI oferece caminho headless útil;
- run/seed/config permitem reprodução básica;
- docs de uso/limitations existem;
- CI/release artifacts são produzidos;
- nenhum item BLOCKING do checklist inicial permanece.

---

## 26. Demonstração final obrigatória

Produzir uma execução/documentação curta de um fluxo completo real:

```text
1 Launch Place&Router
2 Import reference.dsn
3 Review import/readiness
4 Configure/confirm constraints
5 Run optimizer
6 Observe routing blockage trigger placement repair
7 Accept final valid candidate
8 Move one component manually
9 Observe selective reroute/findings
10 Undo/redo
11 Save/reopen project
12 Run one AI semantic/repair operation (quando key disponível; Fake flow sempre demonstrável)
13 Export Gerber + drill
14 Export bottom toner-transfer SVG/PNG 1:1
15 Validate outputs/headless project
```

Guardar métricas da run e outputs de exemplo quando apropriado ao repositório/licença/tamanho.

---

## 27. Fora de escopo

Não bloquear v0.1 por:

- MCTS/ML;
- full RF/DDR/high-speed support;
- distributed/cloud compute;
- collaborative editing;
- plugin marketplace;
- IPC-2581 completo;
- ODB++;
- sophisticated installers/signing;
- exhaustive automated visual testing;
- perfect EasyEDA placement round-trip.

---

## 28. Relatório final do agente

O resumo de entrega deve informar:

- versão produzida;
- plataformas/builds gerados;
- reference projects usados;
- fluxo end-to-end executado;
- joint repair result;
- manual recovery result;
- export outputs validados;
- CLI commands disponíveis;
- AI fake/real validation;
- performance baseline resumido;
- known limitations;
- testes/CI executados e resultado;
- qualquer item deliberadamente fora do escopo conforme este plano.