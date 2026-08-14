# 02 — Interface e Constraint Workspace

## 1. Objetivo da interface

A interface do Place&Router não deve ser apenas um visualizador da placa. Antes de executar a IA, ela precisa permitir que o usuário expresse, de forma visual e simples, o conhecimento de engenharia que uma netlist pura não contém.

A etapa central de preparação do projeto é o **Constraint Workspace**.

O usuário importa o circuito, inspeciona componentes e nets, cria grupos, informa propriedades elétricas e físicas quando necessário, define relações de proximidade/afastamento, configura placa e fabricação e somente depois dispara o optimizer.

A UX segue um princípio adicional obrigatório:

> **Importar → derivar deterministicamente → aplicar defaults de perfil → inferir/sugerir → perguntar somente se a informação restante for material para a decisão atual.**

O fato de o schema permitir muitos campos não significa que o usuário precise preencher todos.

Fluxo principal:

```text
Importar design/netlist
      ↓
Resolver footprints/pins
      ↓
Automatic deterministic enrichment
      ↓
Exibir Components + Nets
      ↓
Classificar / agrupar / enriquecer
      ↓
Definir constraints e objetivos adicionais
      ↓
Definir board / stackup / fabricação
      ↓
Validar conflitos e informações ausentes materialmente necessárias
      ↓
Ready for Physical Design
      ↓
Start Optimization
```

## 2. Estrutura de tela sugerida

```text
┌───────────────────────────────────────────────────────────────┐
│ Project | Board | Rules | Optimize | Review | Export          │
├───────────────┬──────────────────────────────┬────────────────┤
│ DESIGN TREE   │        BOARD / GRAPH         │  PROPERTIES    │
│               │                              │                │
│ Components    │                              │ Selected: U7   │
│ Nets          │                              │                │
│ Groups        │                              │ Electrical     │
│ Regions       │                              │ Placement      │
│ Constraints   │                              │ Routing        │
│               │                              │ Thermal        │
│               │                              │ Rules          │
│               │                              │ Relations      │
├───────────────┴──────────────────────────────┴────────────────┤
│ Diagnostics | Conflicts | Missing data | Suggestions         │
└───────────────────────────────────────────────────────────────┘
```

A composição final pode mudar, mas esses papéis precisam existir.

## 3. Design Tree

A árvore lateral deve permitir navegar por:

- Components;
- Nets;
- Component Groups;
- Net Groups;
- Mixed/Functional Groups, se suportados;
- Regions;
- Constraints;
- warnings/findings.

Busca e filtros são essenciais para placas maiores.

Filtros úteis:

- reference designator;
- part number;
- value;
- net name;
- electrical type;
- aggressor/susceptibility;
- unclassified;
- missing data;
- fixed/movable;
- high-current;
- high-frequency;
- analog/digital/power;
- constraint violations.

`Missing data` deve distinguir:

```text
Missing but currently irrelevant
Missing and reduces confidence
Missing and currently blocks a calculation
```

Só a última categoria deve normalmente interromper o workflow.

## 4. Seleção unificada

Components e nets devem ser selecionáveis individualmente ou em grupo.

O sistema de constraints deve aceitar relações como:

```text
Component → Component
Component → Net
Net       → Component
Net       → Net
Group     → Group
Group     → Net
Group     → Component
Region    → Component/Group
```

Exemplo principal:

> “U12 precisa manter pelo menos 8 mm da net SW_NODE.”

Fluxo de UI:

```text
Select U12
→ Add Constraint
→ Separation
→ Target: SW_NODE
→ Minimum: 8 mm
→ Scope: all relevant layers
→ Required
```

## 5. Properties de net

Uma net deve possuir uma ficha elétrica editável.

Exemplo:

```text
NET: MOTOR_PHASE_A

Electrical
────────────────────────
Type                 Power
Nominal voltage      24 V
Maximum voltage      30 V
Continuous current   3.2 A
Peak current         7.0 A

Frequency / dynamics
────────────────────────
Fundamental          20 kHz
Edge rate            Fast
Harmonics relevant   Yes

Routing
────────────────────────
Priority             High
Preferred layer      L1/L2
Max vias             2
Length limit         Unknown

EMI
────────────────────────
Aggressor            High
Susceptibility       Low
```

Outra net poderia ser:

```text
NET: ADC_IN
Type                 Analog
Amplitude            0..50 mV
Bandwidth            20 kHz
High impedance       Yes
Aggressor            Low
Susceptibility       Very High
```

A UI deve deixar claro o que foi:

- importado;
- informado pelo usuário;
- inferido deterministicamente;
- inferido/sugerido pela IA;
- derivado;
- deixado unknown.

## 6. Propriedades elétricas sugeridas para nets

Campos candidatos:

- signal type;
- nominal voltage;
- maximum voltage;
- current nominal/continuous;
- peak current;
- frequency;
- bitrate;
- bandwidth;
- edge rate / rise/fall classification;
- impedance target;
- differential impedance;
- high-impedance flag;
- clock flag;
- switching-node flag;
- power rail flag;
- aggressor level;
- susceptibility level;
- routing priority;
- return-path criticality;
- length/skew limits;
- via budget;
- preferred/forbidden layers.

Nem todo campo se aplica a toda net.

Nem todo campo aplicável é obrigatório antes de uma run.

## 7. Propriedades de componentes

Componentes também precisam de propriedades além do footprint.

Exemplo:

```text
U17
Category              Switching Regulator
Thermal               High
EMI Aggressor          High
EMI Susceptibility     Low
Placement              Power region preferred
```

Outro:

```text
U23
Category              ADC
Thermal               Low
EMI Aggressor          Low
EMI Susceptibility     Very High
Placement              Quiet region preferred
```

Campos candidatos:

- functional category;
- power dissipation;
- thermal sensitivity;
- EMI aggressor;
- EMI susceptibility;
- preferred region;
- forbidden region;
- side restriction;
- rotation restrictions;
- height;
- fixed/locked status;
- assembly restrictions;
- semantic role.

## 8. Fenômeno elétrico versus regra geométrica

A UI não deve obrigar o usuário a converter todo conhecimento elétrico diretamente em milímetros.

Exemplo:

```text
SW_NODE
  type = switching power
  frequency = 500 kHz
  edge rate = fast
  aggressor = high

ADC_IN
  type = analog
  amplitude = 0..50mV
  susceptibility = very high
```

Com base nisso, o sistema pode sugerir:

```text
- aumentar separação
- evitar longos trechos paralelos
- evitar overlap em layers adjacentes
- manter reference/ground adequado entre regiões
```

A sugestão não se torna automaticamente uma regra obrigatória. O usuário pode:

- aceitar;
- editar;
- rejeitar;
- promover para Required;
- manter como Preferred.

## 9. Grupos como entidade de primeira classe

Grupos são essenciais para reduzir centenas de constraints individuais.

Exemplo de grupo funcional:

```text
POWER_BUCK
 ├── U7
 ├── L3
 ├── D4
 ├── C17
 ├── C18
 ├── R21
 └── R22
```

Exemplo de grupo de nets:

```text
HIGH_CURRENT_NETS
 ├── VIN_RAW
 ├── BUCK_SW
 ├── MOTOR_A
 └── MOTOR_B
```

Grupos podem ser hierárquicos:

```text
POWER
 ├── INPUT_PROTECTION
 ├── BUCK_5V
 └── LDO_3V3
```

Então o usuário pode dizer:

```text
ANALOG_FRONTEND
Required separation >= 15 mm
from
BUCK_5V
```

O sistema pode sugerir grupos com base em connectivity graph e semantic enrichment; o usuário não precisa montar toda hierarquia manualmente.

## 10. Bulk editing

A interface deve suportar edição em lote.

Exemplo:

```text
Select:
SPI_CLK
SPI_MOSI
SPI_MISO
SPI_CS0
SPI_CS1

Create Net Group: SPI_BUS
Set:
  Frequency = 25 MHz
  Aggressor = Medium
  Susceptibility = Medium
```

Outro exemplo:

```text
Select 20 power nets
Set default current class = 2 A
```

Sem bulk editing, a preparação de placas maiores se torna inviável.

Bulk editing é fallback para quando import/inference não bastarem; não deve ser o fluxo obrigatório normal.

## 11. Herança de regras

Regras precisam de herança/especificidade.

Exemplo:

```text
ALL_NETS
  min clearance = 0.20 mm

POWER
  min clearance = 0.40 mm

HIGH_CURRENT
  min clearance = 0.80 mm

MOTOR_PHASES
  min clearance = 1.20 mm
```

A regra mais específica prevalece quando não houver conflito explícito.

A UI deve mostrar de onde veio o valor efetivo.

Exemplo:

```text
Effective min clearance: 0.80 mm
Inherited from: HIGH_CURRENT
```

## 12. Required, Preferred e Optimization Goal

A interface deve apresentar os três níveis de maneira clara.

### Required

Obrigatório.

Exemplo:

```text
ADC must be >= 8 mm from SW_NODE
```

### Preferred

Desejável.

Exemplo:

```text
ADC preferably >= 15 mm from SW_NODE
```

### Optimization Goal

Objetivo contínuo.

Exemplo:

```text
Minimize total distance between ADC and passive network
```

A UI deve evitar termos excessivamente acadêmicos quando houver nomenclatura mais intuitiva.

## 13. Regras direcionais e de intenção

Nem toda regra é simplesmente uma distância simétrica.

Exemplos:

```text
Connector J1 must be near board edge
LED group must face front edge
Power components prefer top-left region
ADC block must remain in quiet region
```

A orientação de componentes e pads também precisa ser expressável.

## 14. Board Definition

Antes da otimização, o sistema tenta importar e o usuário completa apenas o que faltar:

- board outline;
- dimensões;
- shape;
- number of copper layers;
- thickness;
- material, quando relevante;
- stackup;
- copper weight;
- mechanical holes;
- fixed connectors;
- edge requirements;
- keepouts.

Exemplo simples:

```text
Board
Width             100 mm
Height             70 mm
Shape              Rectangle
Layers             2
Thickness          1.6 mm
Material           FR-4
L1 Copper          1 oz
L2 Copper          1 oz
```

## 15. Stackup

Para placas multilayer, deve existir uma representação visual/estruturada:

```text
L1 Signal
Prepreg
L2 Ground
Core
L3 Power
Prepreg
L4 Signal
```

O stackup participa de routing, impedância, reference planes e futuramente estimativas de coupling/SI.

## 16. Manufacturing Profile

A fabricação deve ser configurável como perfil reutilizável.

Exemplos conceituais:

- JLCPCB Standard 2 Layer;
- fabricante local;
- CNC/fresadora caseira;
- prototype conservative;
- perfil customizado.

Campos candidatos:

```text
Minimum track width
Minimum spacing
Minimum drill
Minimum via diameter
Minimum annular ring
Copper-to-edge
Allowed layer count
Allowed via types
Blind/buried vias
Via-in-pad
Copper weight
Minimum component spacing
Assembly side restrictions
```

Constraints de fabricação são Required/hard constraints.

A aplicação nunca deve melhorar score violando a capacidade do fabricante selecionado.

Selecionar o profile deve preencher automaticamente as regras relacionadas; o usuário não deve redigitá-las em cada projeto.

## 17. Regiões visuais

O usuário deve poder desenhar regiões diretamente sobre a board.

Exemplo:

```text
┌────────────────────────────────────────────┐
│ CONNECTORS                                 │
├───────────┬────────────────────────────────┤
│ POWER     │ DIGITAL                        │
│           │                                │
│           ├─────────────────┬──────────────┤
│           │ ANALOG          │ RF           │
└───────────┴─────────────────┴──────────────┘
```

Uma associação pode ser:

```text
ANALOG_FRONTEND → preferred region ANALOG
RF_BLOCK         → required region RF
POWER            → preferred region POWER
```

Tipos de relação:

- Required;
- Preferred;
- Forbidden.

Regiões podem ser explicitamente desenhadas ou sugeridas por floorplanning/semantic grouping e depois editadas.

## 18. Suggest Constraints

O usuário deve poder selecionar um componente, net ou grupo e pedir sugestões.

Exemplo:

```text
Select U7
→ Suggest constraints
```

Antes da chamada cloud, o sistema já fornece ao agente:

- part number;
- pin names;
- net topology;
- deterministic tags/inferences;
- semantic graph atual;
- existing project rules;
- datasheet/application note quando disponível.

Sugestão possível:

```text
[ ] Keep input capacitor close to VIN/GND
[ ] Keep bootstrap capacitor close to BOOT/SW
[ ] Keep feedback network away from SW
[ ] Minimize switching-node copper area
[ ] Keep inductor close to SW/output stage
```

Essas sugestões devem ser revisáveis antes de entrarem no conjunto efetivo de regras.

## 19. Constraint conflict validation

Conflitos precisam ser detectados antes da otimização quando possível.

Exemplo:

```text
C17 must be <= 2 mm from U3
C17 must be >= 10 mm from Group DIGITAL
U3 is fixed inside DIGITAL
```

A UI deve produzir diagnóstico compreensível:

```text
CONSTRAINT CONFLICT
RULE-184 vs RULE-212

The required placement appears geometrically incompatible.
Affected objects:
C17, U3, DIGITAL
```

O usuário deve conseguir navegar diretamente para as regras conflitantes.

## 20. Physical Design Readiness

Antes de iniciar, a aplicação exibe um relatório automático.

Exemplo:

```text
Physical Design Readiness

Board
✓ Outline defined
✓ 2 copper layers
✓ Manufacturing profile selected

Components
✓ 47 components
✓ 47 footprints resolved
⚠ 3 components without semantic classification — non-blocking

Nets
✓ 62 nets
✓ 8 power nets classified
⚠ 14 nets without estimated frequency — currently non-blocking
⚠ 1 high-current net without width/current basis — action required

Rules
✓ 38 Required constraints
✓ 27 Preferences
✓ 4 Optimization Goals
✓ No known conflicts

Result:
READY AFTER 1 REQUIRED INPUT
```

Unknown data não deve automaticamente bloquear execução.

## 21. Perguntas automáticas de dados faltantes

Quando uma informação for necessária, a pergunta deve incluir:

1. **o que precisamos saber**;
2. **por que precisamos saber**;
3. **qual cálculo depende disso**;
4. **quais fallbacks são seguros, se existirem**.

Exemplo:

```text
VIN_MOTOR — corrente máxima

Por quê?
Precisamos dimensionar a largura mínima da rota.

Não afeta:
placement inicial e conectividade.

[Corrente máxima: ____ A]
[Usar net class importada]
[Usar apenas mínimo do fabricante]
[Manter Unknown]
```

A aplicação não deve transformar readiness em um questionário técnico obrigatório.

## 22. Perfis de otimização amigáveis

Parâmetros algorítmicos não devem ser a interface normal do produto.

O usuário comum pode escolher perfis como:

```text
Balanced
Routing-first
Compact
Low-via
EMI-conscious
Manufacturing-conservative
```

Esses perfis mapeiam internamente para parâmetros versionados do engine.

Configurações como:

- routing grid pitch;
- A* cost weights;
- negotiated-congestion factors;
- SA temperature/cooling;
- LNS neighborhood size;

ficam em Advanced/Diagnostics/Benchmark mode, não no fluxo principal.

## 23. Durante a otimização

A mesma GUI deverá posteriormente mostrar:

- candidate state atual;
- elementos locked;
- reserved corridors;
- congestion map;
- routes provisórias e finais;
- findings;
- regressions;
- transações aceitas/rejeitadas;
- razões de cada alteração;
- comparação entre candidatos;
- métricas globais.

A interface de authoring e a interface de review precisam compartilhar as mesmas entidades, evitando dois modelos mentais diferentes.

A explicação ao usuário deve preferir linguagem de engenharia a parâmetros internos do algoritmo.

## 24. Objetivo de UX

O sucesso da aplicação depende tanto da GUI quanto do optimizer.

O usuário deve conseguir expressar em poucos cliques intenções complexas como:

> “Esse bloco analógico é muito suscetível; mantenha-o afastado desse grupo de switching nets, preserve esta região silenciosa, limite as vias dessa interface e respeite as capacidades desta fábrica.”

Mas o objetivo ainda melhor é que, quando import/topologia/perfis já permitirem concluir parte disso, o sistema **não obrigue o usuário a repetir informação que pode ser obtida automaticamente**.

A GUI transforma intenção humana e dados importados em regras estruturadas. A IA e o optimizer recebem um problema bem definido em vez de tentar adivinhar silenciosamente todo conhecimento que o projetista já possui — ou exigir que o projetista preencha manualmente tudo que o software poderia derivar.
