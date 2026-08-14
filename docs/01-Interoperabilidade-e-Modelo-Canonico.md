# 01 — Interoperabilidade, importação e modelo canônico

## 1. Objetivo

O Place&Router deve ser independente do EDA usado para criar o circuito eletrônico.

A ferramenta recebe um design externo e o converte para um modelo interno estável, preservando o máximo possível de:

- componentes;
- part numbers e valores;
- footprints;
- pads e pin mapping;
- nets/net classes;
- atributos elétricos disponíveis;
- board outline;
- stackup;
- keepouts e mechanical constraints;
- placements existentes;
- routes existentes;
- metadata de origem.

O princípio é:

```text
External EDA formats
      ↓ adapters
PRDX canonical model
      ↓
rest of the application
```

O optimizer nunca depende diretamente de estruturas EasyEDA, KiCad, Altium ou equivalentes.

---

## 2. Netlist como entrada lógica mínima

Uma netlist representa o núcleo de conectividade:

```text
COMPONENTS
U1 = STM32F103...
C1 = 100nF

NETS
VDD:
  U1.24
  C1.1
```

Porém, physical design exige também:

- geometria de footprint;
- posição relativa dos pads;
- courtyard/body;
- holes;
- pad/layer types;
- pin names quando disponíveis;
- board outline;
- stackup;
- mechanical data.

Portanto, o import contract aceita um **design package**, não apenas uma netlist isolada.

---

## 3. Formatos de origem e intercâmbio

### Netlists específicas de EDA

Adapters podem consumir formatos usados por:

- EasyEDA;
- Altium/Protel;
- Allegro;
- PADS;
- outros EDAs.

O suporte exato é versionado por adapter.

### EDIF

Formato histórico de intercâmbio eletrônico. Pode servir como importer complementar quando trouxer conectividade útil, mas não é formato canônico do Place&Router.

### IPC-D-356

Útil principalmente como netlist de fabricação/teste e validação complementar. Não é rico o suficiente para ser entrada principal de physical design.

### IPC-2581 / IPC-DPMX

Formato rico de troca de dados PCB/PCBA. É target planejado de import/export para designs físicos mais completos e manufacturing exchange.

### Specctra DSN/SES

Especialmente relevante por representar o workflow clássico de EDA ↔ router externo.

Arquitetura planejada:

```text
EDA
  ↓ DSN
Place&Router
  ↓ SES/routing result
EDA
```

### Gerber

Gerber é prioritariamente output de fabricação e não deve ser fonte primária para reconstrução da intenção completa de design.

Pode ser usado como:

- fabrication output;
- referência geométrica;
- comparação visual;
- validation input complementar.

---

## 4. EasyEDA como primeiro adapter prático

EasyEDA continua sendo o primeiro adapter real provável.

Dois níveis:

### 4.1 Netlist + assets

Primeira opção simples:

```text
EasyEDA netlist
+ footprint information
+ board definition
        ↓
PRDX
```

### 4.2 Source/native adapter

Posteriormente um adapter mais rico pode recuperar:

- metadata adicional;
- pin names;
- symbol/footprint properties;
- board data;
- placement existente;
- constraints disponíveis;
- outras informações perdidas por netlist simples.

Tipos específicos do EasyEDA permanecem dentro do adapter.

---

## 5. Arquitetura de adapters

```text
DesignExchange
 ├── Importers
 │    ├── EasyEDA
 │    ├── KiCad
 │    ├── Specctra
 │    ├── IPC2581
 │    └── ...
 │
 ├── Exporters
 │    ├── EasyEDA
 │    ├── KiCad
 │    ├── Specctra
 │    ├── Gerber
 │    ├── IPC2581
 │    └── ...
 │
 ├── Canonicalization
 ├── Validation
 ├── CapabilityReporting
 └── LossDiagnostics
```

Import/export nunca é presumido lossless. Cada adapter declara capabilities e perdas.

---

## 6. PRDX é o formato nativo aceito

A decisão atual está formalizada em `11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md` e ADR-0005.

```text
extension              .prdx
container              ZIP
manifest               manifest.json
canonical payload      project.json
schema                 JSON Schema Draft 2020-12
```

PRDX não é apenas um exchange format temporário; é o **arquivo nativo de projeto** da primeira implementação.

Ele persiste:

```text
logical design
components/footprints/pads
netlist
board/stackup
manufacturing profile snapshot
constraints
semantics
groups/regions
accepted PhysicalDesignState
placement
routing/vias/copper zones
persistent user decisions
project-level profiles
```

Schemas iniciais:

- `schemas/prdx/0.1/prdx-manifest.schema.json`;
- `schemas/prdx/0.1/prdx-project.schema.json`.

A representação runtime não precisa espelhar a árvore JSON 1:1.

---

## 7. Provenance

Cada propriedade importante pode carregar origem:

```text
IMPORTED
USER_DEFINED
AI_INFERRED
DETERMINISTIC_INFERENCE
DETERMINISTIC_MEASUREMENT
DERIVED
MANUFACTURING_PROFILE
DEFAULT
UNKNOWN
```

Exemplo:

```text
frequency = 25 MHz
source = USER_DEFINED
```

```text
role = DECOUPLING_CAPACITOR
source = AI_INFERRED
confidence = 0.94
```

Dados importados, inferidos e definidos pelo usuário não podem ser confundidos.

---

## 8. Unknown é válido

Knowledge status:

```text
KNOWN
INFERRED
UNKNOWN
NOT_APPLICABLE
```

Ausência de informação:

- entra na readiness/dependency analysis;
- reduz confiança quando relevante;
- pode gerar suggestion;
- só bloqueia quando realmente necessária para uma decisão suportada.

O sistema segue `importar/derivar/inferir antes de perguntar`.

---

## 9. Footprint e pin mapping

Import precisa validar:

- footprint resolvido para componente físico;
- pads associados ao pin elétrico correto;
- pad numbers/names coerentes;
- pads mecânicos/NC separados;
- shapes/layers disponíveis;
- courtyard/body disponível ou derivável.

Falha de mapping é diagnóstico explícito e pode bloquear physical design daquele componente.

---

## 10. Design parcialmente físico

Import pode conter:

- fixed components/connectors;
- mounting holes;
- keepouts;
- regions;
- placement existente;
- routes existentes;
- copper zones;
- layer restrictions.

Políticas canônicas iniciais:

```text
MOVABLE
PRESERVE_PREFERRED
LOCKED
MECHANICAL_FIXED
REROUTABLE
```

Dados válidos importados devem ser preservados até que uma decisão explícita permita modificá-los.

---

## 11. SourceImport e capability report

Cada operação de import gera metadata persistente com:

```text
adapter/version
source type/name/hash
embedded source path when applicable
capabilities
loss diagnostics
```

Exemplo:

```text
components        COMPLETE
nets              COMPLETE
footprints        COMPLETE
pinNames          PARTIAL
boardOutline      COMPLETE
stackup           MISSING
existingPlacement PARTIAL
existingRoutes    NOT_AVAILABLE
```

Isso permite saber exatamente o que veio do EDA e o que foi completado depois.

---

## 12. Reimport incremental

Evolução prevista:

```text
external design changed
      ↓
reimport diff
      ↓
identity matching
      ↓
dependency/invalidation analysis
      ↓
preserve still-valid user work
```

O sistema tenta preservar:

- stable component IDs;
- confirmed semantics;
- constraints;
- valid placement;
- unaffected routing.

Footprint/pin/net changes disparam o mesmo `EditImpactPlanner` usado pelas alterações internas.

---

## 13. Exportação

Toda exportação é projection do PRDX/`PhysicalDesignState`.

Classes:

```text
EDA round-trip
Gerber + NC drill manufacturing
IPC-2581 rich exchange
DIY transfer artwork
inspection/documentation renders
machine-specific outputs in future
```

Output nativo de EDA deve preservar, quando possível:

- component identity/reference;
- footprints;
- nets;
- positions/rotations/sides;
- tracks;
- vias;
- layer assignments;
- board geometry;
- constraints compatíveis.

Qualquer perda é reportada em `LossReport`.

Detalhes completos no documento `11`.

---

## 14. Circuit Semantic Graph

Além da conectividade, PRDX persiste relações semânticas relevantes:

```text
C17 --decouples--> U3.VDD
R17/R18 --feedback-network-of--> U7.FB
L3 --switching-output-of--> U7.SW
ADC_REF --susceptible-to--> SWITCHING_GROUP
```

Pode ser alimentado por:

- import;
- user rules;
- deterministic heuristics;
- datasheets/application notes;
- LLM;
- case retrieval.

Semântica inferida nunca sobrescreve silenciosamente uma definição explícita.

---

## 15. Requisitos atuais de interoperabilidade

1. Core não referencia tipos de EDA.
2. PRDX é versionado e possui migration path.
3. Dados inferidos possuem provenance.
4. Unknown é estado válido.
5. Import produz capability/loss diagnostics.
6. Footprint/pad mapping é requisito de physical design.
7. Usuário pode completar/confirmar informação pela GUI.
8. Designs parcialmente físicos são suportados arquiteturalmente.
9. Locks/preserve policies são respeitados.
10. Round-trip EDA é objetivo desde o início.
11. Routing aceito faz parte do project file.
12. Export industrial, artesanal e documental partem da mesma fonte canônica.
