# ADR-0006 — EasyEDA Pro: handoff inicial via Specctra DSN

**Status:** Accepted  
**Date:** 2026-08-14

## Contexto

O primeiro adapter real precisa funcionar com um EDA usado pelo projeto sem depender imediatamente de um formato nativo privado/instável.

EasyEDA Pro oferece oficialmente:

- export de netlist;
- save local de projeto `.epro`/archive;
- export de autorouter em Specctra DSN;
- import de autorouter session SES;
- export Gerber/PDF/Image.

A documentação oficial do PCB source format ainda não fornece uma especificação completa suficiente para tratarmos o formato nativo como contrato externo estável.

## Decisão

### 1. Baseline de entrada física

Primeiro workflow suportado:

```text
EasyEDA Pro
  ↓ create/update PCB from schematic
PCB with footprints + outline
  ↓ Export → Autoroute (DSN)
Specctra DSN
  ↓ PlaceRouter.Import.Specctra
PRDX
```

DSN é preferido como primeiro handoff porque foi criado para EDA → external router e transporta contexto físico necessário a autorouting.

### 2. Placement importado

Placement existente no DSN pode ser:

```text
LOCKED
PRESERVE_PREFERRED
MOVABLE
```

conforme policy/import options.

Para um projeto que deseja placement autônomo, posições iniciais normalmente entram como `MOVABLE` ou `PRESERVE_PREFERRED`, exceto objetos mecânicos/fixos.

### 3. Netlist supplemental

EasyEDA Pro netlist export pode ser aceito como source adicional para:

- metadata;
- device/package attributes;
- connectivity cross-check;
- diagnostics.

O DSN continua sendo a fonte física baseline quando disponível.

### 4. Output inicial

O Place&Router não depende do EasyEDA para fabricar a placa.

Após design em PRDX pode gerar diretamente:

```text
Gerber + NC drill
DIY transfer artwork
inspection/documentation
```

### 5. SES

Support para SES é planejado e útil para retornar wires/vias ao EasyEDA.

Porém, o primeiro implementation contract **não presume que SES seja um round-trip completo de placement**.

A documentação oficial do EasyEDA descreve a importação SES como geração de wires e vias; portanto placement alterado pelo Place&Router precisa de outro mecanismo para round-trip completo.

### 6. Full EasyEDA round-trip futuro

Opções a investigar/testar:

```text
native .epro/project archive adapter
file-source adapter
EasyEDA extension/plugin bridge
other officially supported interchange format
```

Nenhuma delas entra como dependency da primeira prova de physical design.

## Consequências

### Positivas

- primeiro importer baseado em formato de autorouter conhecido;
- não bloqueia placement/routing autônomo por falta de native format spec;
- permite fabricação direta mesmo antes de full EasyEDA round-trip;
- mantém caminho futuro para SES e adapter nativo.

### Limitações

- usuário precisa ter um PCB no EasyEDA com footprints/outline antes de exportar DSN;
- round-trip completo de placement para EasyEDA ainda requer adapter adicional;
- DSN importer precisa de conformance fixtures reais gerados por EasyEDA Pro.

## Testes obrigatórios

Capturar fixtures reais EasyEDA Pro de:

```text
2-layer simple board
fixed connector + movable components
through-hole + SMD
pre-existing routes
keepouts/outline
net classes where possible
```

Validar:

```text
component identity
footprints/pads
nets
board outline
layers
placement
existing route behavior
units/origin
loss report
```

## Referências oficiais

- EasyEDA Pro User Guide — Export Autorouter DSN;
- EasyEDA Pro User Guide — Import Autorouting SES;
- EasyEDA Pro User Guide — Export Netlist;
- EasyEDA Pro User Guide — Project Save as Local / Export EasyEDA Pro.
