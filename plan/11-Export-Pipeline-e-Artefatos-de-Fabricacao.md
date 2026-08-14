# PLAN-11 — Export Pipeline e Artefatos de Fabricação

**Status:** APPROVED  
**Pré-requisitos obrigatórios:** PLAN-03, PLAN-07 e PLAN-08 concluídos  
**Pode avançar em paralelo a:** PLAN-09 e PLAN-10  
**Participa de:** PLAN-12

---

## 1. Instrução ao agente

Você está implementando a saída de dados do WTK.Place&Router. O projeto PRDX e o `PhysicalDesignState` aceito são a fonte de verdade; exporters são projections desse estado para fabricação industrial, retorno ao EDA, documentação e fabricação artesanal.

Antes de codificar:

1. leia `/AGENTS.md` e `/plan/00-ROADMAP-MESTRE-V0.1.md`;
2. confirme que project lifecycle, detailed routing e accepted optimizer state existem;
3. leia este plano inteiro;
4. leia os documentos/ADRs obrigatórios;
5. verifique especificações/licenças de bibliotecas de export antes de incorporá-las;
6. prefira formatos padrões e output determinístico;
7. execute todos os targets obrigatórios deste plano, não somente Gerber ou somente imagens.

### Documentos obrigatórios

- `docs/01-Interoperabilidade-e-Modelo-Canonico.md`
- `docs/03-Modelo-de-Dominio-e-Constraints.md`
- `docs/09-Decisoes-Arquiteturais-e-Terminologia.md`
- `docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0004-Licenciamento-de-Dependencias-Algoritmicas.md`
- `docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md`
- `docs/adr/0006-EasyEDA-DSN-como-Handoff-Inicial.md` ou ADR equivalente vigente
- PLAN-03, PLAN-07 e PLAN-08

---

## 2. Objetivo mensurável

Ao final, para um projeto aceito/roteado, o sistema deve conseguir produzir:

```text
Manufacturing package
  → Gerber layer files
  → NC Drill
  → job/manifest metadata

EDA route round-trip
  → Specctra SES baseline for wires/vias

DIY transfer/artwork
  → PDF
  → SVG
  → PNG
  → TIFF
  → true 1:1 physical scale controls
  → mirror/polarity/registration/calibration options

Inspection/documentation
  → PNG/SVG/PDF board views
```

Todos os outputs devem partir do mesmo estado canônico e do mesmo `ExportProfile`.

---

## 3. Export architecture

Implementar ports/contracts equivalentes a:

```text
ExportRequest
ExportProfile
IDesignExporter / IArtifactExporter
ExportCapability
ExportResult
ExportDiagnostic
ExportArtifact
```

`ExportResult` deve informar:

- files/artifacts gerados;
- capabilities efetivamente exportadas;
- losses/unsupported features;
- warnings/errors;
- source project/state revision;
- profile/version;
- hashes quando útil.

ViewModels não geram arquivos diretamente.

---

## 4. Gate de exportabilidade

Antes de manufacturing export:

```text
accepted/current PhysicalDesignState
→ canonical integrity
→ required constraints
→ DRC/connectivity
→ export readiness
```

Por padrão, **blocking Required violations impedem fabrication export**.

Permitir somente uma ação avançada explícita de `Export With Blocking Warnings` se o produto/policy vigente autorizar, marcando o pacote e logs claramente; não transformar isso em default.

Documentation/inspection render pode ser permitido mesmo em state inválido.

---

## 5. ExportProfile

Persistir profiles de projeto quando representam intenção do usuário.

Campos/capabilities iniciais:

```text
targetType
profileName
units/output precision
layer selection
compatibility mode
mirror per layer
polarity
include board outline
include drill center marks
registration marks
calibration marks
margin/crop
raster DPI
monochrome/color
artifact naming
package as ZIP
```

Não persistir absolute output path portátil no PRDX como requisito; recent destination pode ficar no workspace.

---

# Parte I — Gerber / fabricação industrial

## 6. Gerber exporter v0.1

Implementar output compatível com o Gerber Layer Format vigente suportado pelo projeto.

### Layers obrigatórios quando existem no modelo

- top copper;
- bottom copper;
- board outline/profile.

### Layers adicionais quando dados suficientes existirem

- inner copper;
- top/bottom solder mask;
- top/bottom silkscreen;
- top/bottom paste;
- mechanical/user layers explicitamente mapeadas.

Não inventar solder mask/silk se o canonical model não possuir informação suficiente para gerá-los corretamente. Produzir capability/loss diagnostic.

---

## 7. Gerber geometry mapping

Mapear canonical geometry para primitives do exporter:

- tracks → strokes/draws com aperture compatível;
- pads/vias → flashes ou regions quando necessário;
- copper zones → regions/polygons;
- board profile → closed contour;
- holes não viram copper arbitrariamente; drill é separado;
- rounded/rectangular/custom pads devem preservar geometria suportada ou usar region/aperture macro adequado.

Usar coordenadas canônicas e conversão explícita de unidades/precision.

Não gerar Gerber a partir de screenshot/viewport.

---

## 8. Gerber attributes / compatibility profiles

Criar strategy/profile para pelo menos:

```text
GERBER_CURRENT
GERBER_X2_COMPATIBILITY (quando semanticamente distinto no writer usado)
CONSERVATIVE_CAM
```

O nome exato pode mudar conforme especificação/biblioteca vigente, mas deve existir separação entre:

- output moderno rico em attributes;
- modo compatível/conservador quando CAM antigo exigir.

Attributes de file/function/net/pad/component podem ser emitidos quando o canonical model e writer suportarem de forma correta.

Não hardcode quirks de um fabricante dentro do exporter central; use compatibility profile.

---

## 9. NC Drill exporter

Gerar drill files com:

- tool diameter mapping;
- coordinates;
- plated/non-plated distinction quando conhecida;
- through holes e vias;
- units/precision explícitas;
- deterministic tool ordering.

Quando necessário, separar plated e NPTH em files distintos conforme profile.

Drill não deve depender de rendered image.

---

## 10. Gerber Job / manufacturing manifest

Gerar job/manifest quando suportado pelo target/profile, contendo ao menos:

- board identity/revision;
- layer/file roles;
- stackup summary quando apropriado;
- generated artifacts;
- project/state revision;
- exporter version/profile.

Além disso gerar `PlaceRouter-export-report.json` ou equivalente interno com diagnostics/hashes/capabilities do pacote.

---

## 11. Manufacturing package

Oferecer export folder e ZIP package.

Naming consistente, por exemplo:

```text
Board-F_Cu.gbr
Board-B_Cu.gbr
Board-Edge_Cuts.gbr
Board-PTH.drl
Board-NPTH.drl
Board-job.gbrjob
PlaceRouter-export-report.json
```

A nomenclatura final pode seguir convenções mais adequadas do profile, mas precisa ser determinística e clara.

---

# Parte II — EDA round-trip

## 12. Specctra SES route exporter

Implementar baseline de retorno de **routing** para o fluxo EasyEDA/DSN:

```text
Imported DSN source identity
+ accepted PRDX routes
→ SES session/routes
```

Escopo v0.1:

- wires/tracks;
- vias;
- net association;
- layer mapping;
- route status suficiente para import por EDA compatível.

Não afirmar que SES retorna placement de componentes se o formato/workflow alvo não suportar isso de forma confiável.

Produzir diagnostic quando placement no PRDX divergir do source DSN de uma forma que o SES não consiga representar.

---

## 13. DSN/SES identity preservation

Para round-trip preservar:

- source component/net identifiers quando necessários;
- layer mapping;
- unit conversion;
- source fingerprint/reference;
- warnings se source mudou desde import.

Se source fingerprint não corresponde ao baseline original, marcar `SOURCE_MISMATCH` e não fingir round-trip seguro.

---

# Parte III — DIY transfer/artwork

## 14. Artwork renderer independente de viewport

Criar rendering pipeline de export baseado em canonical geometry, separado do `PcbViewportControl`.

Ele deve gerar uma cena vetorial/print model com:

- copper geometry por layer;
- pads/vias;
- board outline opcional;
- drill centers;
- registration/calibration marks;
- labels opcionais apenas quando profile permitir.

Não capturar a tela como imagem de fabricação.

---

## 15. Physical scale model

Definir transformação de unidades canônicas para unidades físicas de saída.

### PDF/SVG

Preservar dimensão física real na página/documento.

### Raster

```text
pixelCount = physicalInches × DPI
```

DPI deve estar no metadata quando formato permitir e constar no export report.

Oferecer calibration mark, por exemplo barra/quadrado com dimensão conhecida, para o usuário verificar impressão 1:1.

---

## 16. Mirror e polarity

Profile por layer deve suportar explicitamente:

```text
mirror = NONE | HORIZONTAL | VERTICAL / canonical mode definido
polarity = POSITIVE | NEGATIVE
```

UI deve mostrar claramente resultado/orientação, especialmente bottom layer.

Não aplicar mirror implicitamente sem mostrar/registrar a decisão.

Para toner transfer/photoresist, oferecer presets amigáveis que apenas preencham settings explícitos.

---

## 17. PDF exporter

Gerar documento com:

- escala 1:1 por default de fabricação artesanal;
- page size adequada/selecionável;
- no fit-to-page silencioso;
- margins controlados;
- optional crop/tile quando board exceder página;
- calibration mark;
- layer name/profile summary fora da arte quando possível sem interferir.

Se tiling for implementado, registration marks precisam ser consistentes.

---

## 18. SVG exporter

Gerar SVG vetorial com:

- viewBox coerente;
- physical width/height explícitos;
- paths/shapes fiéis;
- mirror/polarity transform explícito;
- monochrome manufacturing mode.

SVG serve também para inspection/documentation.

---

## 19. PNG/TIFF exporter

Renderizar a DPI configurável, com default alto apropriado ao uso artesanal, mas sem hardcode não documentado.

Requisitos:

- exact pixel dimensions derivados da escala;
- monochrome option;
- antialias policy controlada para artwork; evitar bordas cinza quando profile pede bitmap binário;
- polarity/mirror corretos;
- metadata de DPI quando suportado;
- TIFF opcionalmente bilevel/monochrome quando biblioteca suportar corretamente.

---

## 20. Presets amigáveis

Criar presets de UX como:

```text
Toner Transfer — Top Copper
Toner Transfer — Bottom Copper
Photoresist Positive
Photoresist Negative
Inspection — Color Top
Documentation — Board Overview
```

Preset não esconde settings; ele apenas seleciona mirror/polarity/layers/DPI/marks adequados e mostra preview/summary antes de exportar.

---

# Parte IV — Inspection/documentation

## 21. Inspection renders

Gerar PNG/SVG/PDF de:

- board top;
- board bottom;
- routing only;
- placement/components;
- selected/net-highlight quando request especificar;
- constraint/region overlay;
- before/after transaction diff quando dados fornecidos.

Esses renders podem usar cores/labels e não precisam ser fabricação-ready.

---

## 22. Export preview UI

Adicionar diálogo/panel de export com:

- target type;
- profile/preset;
- layers;
- dimensions/scale;
- mirror/polarity;
- DPI;
- expected files;
- blocking diagnostics;
- visual preview para artwork/documentation quando possível.

Não misturar export config com permanent board design settings sem intenção.

---

## 23. Determinism e hashes

Para o mesmo accepted state + profile + exporter version, output textual/geometry deve ser reproduzível tanto quanto o formato permitir.

Registrar hashes no export report.

Ordenar nets/apertures/tools/primitives deterministicamente onde possível.

---

## 24. Validation

Validar outputs por mecanismos apropriados:

- Gerber syntax/basic conformance por parser/library independente quando viável;
- drill parsing/basic geometry check;
- SES syntax/self-parse fixture se parser existir;
- SVG/XML parse + physical dimensions;
- PDF page physical dimensions;
- PNG/TIFF pixel dimensions/DPI;
- compare exported copper bbox/area/critical coordinates contra canonical model em fixtures.

Não depender apenas de “arquivo foi criado”.

---

## 25. Testes mínimos

1. Gerber track/pad fixture mantém coordinates/dimensions;
2. copper zone vira region sem perda grosseira;
3. drill tool mapping é determinístico;
4. fabrication export bloqueia state com Required violation;
5. SES export mantém net/layer/track/via mapping esperado;
6. SVG possui physical dimensions corretas;
7. PNG/TIFF em DPI X possuem pixel dimensions calculadas corretamente;
8. bottom mirror preset produz orientação esperada;
9. negative polarity inverte artwork corretamente;
10. PDF 1:1 contém calibration mark com dimensão correta;
11. save/reopen preserva export profiles do projeto.

---

## 26. Fora de escopo

- IPC-2581 completo;
- ODB++;
- G-code CNC/isolation routing;
- assembly pick-and-place/BOM manufacturing package completo;
- native EasyEDA placement patching;
- panelization sofisticada;
- CAM optimization.

Esses podem vir após v0.1.

---

## 27. Critérios de aceitação

Plano concluído quando:

- export architecture é independente da UI;
- Gerber copper/outline + NC Drill são gerados de estado aceito;
- manufacturing package possui report/capabilities;
- SES route export existe para baseline DSN/EasyEDA;
- PDF/SVG/PNG/TIFF artwork suportam escala 1:1, mirror, polarity e marks;
- inspection renders existem;
- blocking validity gate funciona;
- outputs possuem validação mínima real;
- UI oferece profiles/presets compreensíveis;
- build/test alvo passa.

### Demonstração mensurável

A partir de `sample-routed.prdx`:

```text
Export Manufacturing
→ F_Cu/B_Cu/Edge + drill + report generated

Export EasyEDA Route
→ .ses generated with expected wires/vias

Export Toner Transfer Bottom
→ SVG + PNG 1200 DPI
→ mirrored explicitly
→ 1:1 dimensions verified
→ 10 mm calibration mark measures 10 mm in vector output
```

---

## 28. Relatório final

Informar exporters/formats entregues, bibliotecas usadas/licenças, Gerber/drill capabilities, SES limitations, artwork scaling verification, presets e validation executada.