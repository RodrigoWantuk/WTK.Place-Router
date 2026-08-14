# 01 — Interoperabilidade, importação e modelo canônico

## 1. Objetivo

O Place&Router deve ser independente do EDA usado para criar o circuito eletrônico.

A ferramenta recebe um design externo e o converte para um modelo interno estável. Esse modelo deve preservar o máximo possível de:

- componentes;
- part numbers;
- valores;
- footprints;
- pads e pin mapping;
- nets;
- net classes;
- atributos elétricos disponíveis;
- board outline, quando fornecido;
- stackup, quando fornecido;
- keepouts;
- mechanical constraints;
- placements existentes, se houver;
- routes existentes, se houver;
- metadata de origem.

O princípio é:

```text
External EDA formats
      ↓ adapters
Place&Router Canonical Model
      ↓
rest of the application
```

O optimizer nunca deve depender diretamente de estruturas específicas do EasyEDA, KiCad ou Altium.

## 2. Netlist como ponto de entrada mínimo

Uma netlist tradicional é suficiente para representar o núcleo lógico de conectividade:

```text
COMPONENTS
U1 = STM32F103...
C1 = 100nF
R1 = 10k

NETS
VDD:
  U1.24
  C1.1

GND:
  U1.23
  C1.2
```

Porém, netlist sozinha não necessariamente contém tudo que é necessário para physical design.

Também precisamos resolver:

- geometria do footprint;
- posição relativa dos pads;
- courtyard/body;
- lado permitido;
- height, quando relevante;
- holes;
- layer/pad types;
- mechanical information;
- pin names, quando disponíveis;
- board outline e stackup.

Assim, o conceito de importação deve aceitar um **design package**, não apenas um arquivo de netlist isolado.

## 3. Formatos considerados

### 3.1 Netlists de EDA

Adapters específicos podem importar formatos de netlist usados por ferramentas como:

- EasyEDA;
- Altium/Protel;
- Allegro;
- PADS;
- outros formatos relevantes.

O formato exato suportado por cada EDA pode variar entre versões e deve ser validado quando o adapter for implementado.

### 3.2 EDIF

EDIF é um formato histórico de intercâmbio eletrônico e pode ser considerado como importer adicional quando fornecer conectividade útil.

Não é escolhido como formato canônico do Place&Router.

### 3.3 IPC-D-356

IPC-D-356 é útil principalmente para conectividade/teste de bare-board e validação de fabricação.

Pode futuramente servir como fonte complementar ou de comparação, mas não possui riqueza suficiente para ser o formato principal de entrada do physical design.

### 3.4 IPC-2581

IPC-2581 é relevante por transportar dados ricos de PCB/PCBA e pode ser um excelente formato de interoperabilidade para designs físicos mais completos.

Deve ser tratado como candidato importante a import/export, especialmente para projetos que já possuam parte do physical design.

Não se assume, porém, que todo EDA consiga produzir um IPC-2581 completo antes do layout.

### 3.5 Specctra DSN

Specctra DSN é particularmente interessante porque sua finalidade histórica é transferir dados de PCB para um router externo.

Isso é conceitualmente próximo ao Place&Router.

O projeto deve considerar suporte a:

```text
EDA
  ↓ .dsn
Place&Router
  ↓ routed/modified result
EDA
```

Quando disponível, DSN pode trazer mais contexto físico que uma netlist simples.

### 3.6 Gerber

Gerber não deve ser considerado formato primário de entrada para o optimizer.

É um formato de fabricação e não representa adequadamente toda a intenção de projeto necessária para placement/routing autônomos.

Pode ser usado futuramente como:

- output de fabricação;
- referência geométrica;
- comparação visual;
- validação complementar.

## 4. EasyEDA como primeiro adapter prático

Como o EasyEDA faz parte do workflow pretendido, ele é um candidato natural ao primeiro adapter real.

A estratégia deve permitir dois níveis:

### 4.1 Importação por netlist + assets

Primeira implementação mais simples:

```text
EasyEDA netlist
+ footprint information
+ board definition
        ↓
Place&Router
```

### 4.2 Importação nativa/source

Depois, um adapter mais rico pode consumir o formato/source nativo do projeto quando isso permitir recuperar:

- metadata adicional;
- nomes de pins;
- propriedades de symbols/footprints;
- board data;
- existing placement;
- constraints existentes;
- outras informações que uma netlist simples perca.

O adapter nativo não deve contaminar o domínio central com tipos EasyEDA.

## 5. KiCad e outros EDAs

O projeto deve ser preparado desde o início para adapters independentes.

Exemplos planejados:

```text
PlaceRouter.Import.EasyEDA
PlaceRouter.Import.KiCad
PlaceRouter.Import.Specctra
PlaceRouter.Import.IPC2581
PlaceRouter.Import.Altium
```

Os nomes finais podem mudar, mas a separação de responsabilidades deve permanecer.

## 6. Modelo canônico — PRDX

É útil ter um formato persistível próprio do Place&Router para:

- desacoplar o produto de formatos externos;
- salvar constraints adicionadas pelo usuário;
- salvar inferências semânticas;
- registrar provenance;
- reproduzir experimentos;
- trocar estado entre CLI, GUI e engine;
- versionar projetos e runs.

Nome conceitual provisório:

**PRDX — PlaceRouter Design Exchange**.

Esse nome não precisa se tornar um padrão público e pode ser alterado. O conceito importante é possuir um modelo canônico versionado.

Exemplo conceitual:

```json
{
  "schemaVersion": "0.1",
  "source": {},
  "board": {},
  "stackup": {},
  "components": [],
  "footprints": [],
  "nets": [],
  "netClasses": [],
  "groups": [],
  "regions": [],
  "constraints": [],
  "semanticRelationships": [],
  "manufacturingProfile": {},
  "optimizationProfile": {},
  "metadata": {}
}
```

JSON é um candidato conveniente para as primeiras versões e debugging. A representação runtime não precisa ser JSON.

## 7. Provenance e origem dos dados

Cada propriedade importante deve, quando possível, carregar origem.

Exemplos:

```text
frequency = 25 MHz
source = USER_DEFINED
```

```text
role = DECOUPLING_CAPACITOR
source = AI_INFERRED
confidence = 0.94
```

```text
footprint = LQFP-48
source = IMPORTED
```

```text
minClearance = 0.20 mm
source = MANUFACTURING_PROFILE
```

Taxonomia inicial:

- Imported;
- UserDefined;
- Inferred;
- Derived;
- Default;
- Unknown.

Isso é fundamental para a UI e para auditoria.

## 8. Dados desconhecidos são válidos

A ferramenta não deve exigir que toda net tenha frequência, corrente ou susceptibility informadas.

Estados permitidos:

```text
Known
Inferred
Unknown
NotApplicable
```

A ausência de informação deve:

- aparecer na readiness report;
- reduzir confiança de algumas avaliações;
- poder gerar sugestões;
- não impedir arbitrariamente o uso da ferramenta.

Exemplo: uma UART simples pode ser otimizada mesmo se o usuário não preencher corrente estimada.

## 9. Resolução de footprints e pin mapping

O physical design depende criticamente de footprint correto.

O import pipeline deve validar:

- todo componente que participa do layout possui footprint;
- cada pad relevante pode ser associado ao pin elétrico correto;
- números/names de pins são coerentes;
- pads mecânicos/non-connected sejam distinguíveis;
- pad shapes/layers estejam disponíveis;
- courtyard/body estejam disponíveis ou possam ser derivados.

Falha de mapping não deve ser silenciosa.

## 10. Importação de design parcialmente físico

O produto deve suportar não apenas placas completamente vazias.

Um design importado pode conter:

- componentes fixos por necessidade mecânica;
- conectores já posicionados;
- mounting holes;
- keepouts;
- regiões;
- componentes parcialmente posicionados;
- rotas manuais que devem ser preservadas;
- rotas que podem ser rip-up;
- copper pours;
- restrições de layer.

Cada elemento importado deve poder ter uma política como:

```text
LOCKED
PRESERVE_PREFERRED
MOVABLE
REROUTABLE
```

Os nomes finais são provisórios.

## 11. Exportação de volta ao EDA

O output ideal é um design que possa continuar sendo editado no EDA original.

Portanto, adapters devem evoluir para duas direções:

```text
External → PRDX
PRDX → External
```

A primeira versão pode suportar apenas um subconjunto, mas a arquitetura não deve assumir export one-way.

O output precisa preservar, quando possível:

- referências de componentes;
- footprints;
- nets;
- positions/rotations/sides;
- tracks;
- vias;
- layer assignments;
- board geometry;
- constraints compatíveis com o formato de destino.

## 12. Constraint enrichment após importação

O import termina quando temos uma representação estrutural confiável. A etapa seguinte é enriquecer o projeto.

```text
Imported design
      ↓
Constraint Workspace
      ↓
User-defined electrical intent
      ↓
AI-assisted semantic enrichment
      ↓
Validated PRDX
      ↓
Physical Design Optimizer
```

O Place&Router não deve confundir dados importados com inferências posteriores.

## 13. Circuit Semantic Graph

Além do grafo de conectividade, o modelo canônico deve permitir um grafo semântico.

Exemplos:

```text
C17 --decouples--> U3.VDD
R17/R18 --feedback-network-of--> U7.FB
L3 --switching-output-of--> U7.SW
ADC_REF --susceptible-to--> SWITCHING_GROUP
```

Esse grafo pode ser preenchido por:

- dados importados;
- regras explícitas do usuário;
- heurísticas;
- datasheets/application notes;
- LLM;
- casos anteriores.

O grafo semântico nunca deve sobrescrever silenciosamente uma definição explícita do usuário.

## 14. Estrutura sugerida da Design Exchange Layer

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
 │    └── ...
 │
 ├── Canonicalization
 ├── Validation
 └── Diagnostics
```

O importer deve produzir diagnósticos explícitos:

```text
47 components imported
62 nets imported
47/47 footprints resolved
3 pin-name mappings unavailable
1 component marked mechanical-only
board outline missing
stackup missing
```

## 15. Requisitos iniciais de interoperabilidade

1. O core não deve referenciar tipos específicos de nenhum EDA.
2. O formato canônico deve ser versionado.
3. Todo dado inferido precisa de provenance.
4. Unknown é um estado válido.
5. Import deve produzir diagnóstico de perda de informação.
6. Footprint/pad mapping é requisito obrigatório para placement/routing.
7. O usuário pode completar dados ausentes pela GUI.
8. Designs parcialmente posicionados precisam ser suportáveis no futuro.
9. Elementos locked devem ser preservados.
10. Export de volta ao EDA deve ser uma meta arquitetural desde o início, ainda que implementada depois.
