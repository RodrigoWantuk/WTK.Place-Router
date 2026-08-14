PLAN-01R — Consolidação e Hardening da Fundação PRDX antes do PLAN-02

Status: APPROVED
Base obrigatória: PLAN-01 implementado no commit f304c2d658dae8f9d6887d7cb7350244c87f29a4
Bloqueia: PLAN-02 até conclusão integral deste plano
Tipo de entrega: refatoração funcional e correção de contracts; não é nova feature de produto

1. Instrução ao agente

Você está corrigindo e consolidando a fundação do WTK.Place&Router imediatamente após a primeira implementação do PLAN-01.

O objetivo não é reescrever por preferência estilística. O objetivo é impedir que PLAN-02 e seguintes sejam construídos sobre contracts frágeis.

Antes de alterar código:

Leia /AGENTS.md.
Leia integralmente:
plan/00-ROADMAP-MESTRE-V0.1.md
plan/01-Bootstrap-Core-e-PRDX-Runtime.md
este plano inteiro.
Leia:
docs/00-Visao-Geral-e-Principios.md
docs/03-Modelo-de-Dominio-e-Constraints.md
docs/09-Decisoes-Arquiteturais-e-Terminologia.md
docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md
docs/adr/0002-Stack-Desktop-e-Fronteiras-Arquiteturais.md
docs/adr/0005-PRDX-Persistencia-Lifecycle-e-Exportacao.md
Leia os dois schemas PRDX e todos os fixtures.
Inspecione integralmente a implementação produzida no PLAN-01.
Execute este plano como uma única entrega. Não pare depois de refatorar o Domain, nem depois de ajustar o schema, nem depois de fazer os testes.

Ao final, o PLAN-02 deve poder consumir o Domain sem precisar manipular JsonNode, nomes de propriedades JSON ou detalhes ZIP.

2. Problemas concretos que esta entrega deve resolver
2.1 O canonical model ainda não é realmente um modelo de domínio

Atualmente CanonicalProject mantém um JsonObject Root e praticamente toda a estrutura é acessada por strings como:

Root["logicalDesign"]?["components"]
Root["physicalDesignState"]?["routes"]

Isso precisa acabar antes do PLAN-02.

Geometry, constraints, routing e optimizer não devem nascer fazendo:

project.Root["board"]["layers"]

Eles precisam trabalhar com:

project.Board.Layers
project.LogicalDesign.Components
project.PhysicalDesignState.Routes
2.2 CreateEmpty() inventa uma placa física

Hoje um projeto vazio nasce com:

outline = quadrado 10 × 10 mm
layers = Top Copper + Bottom Copper

Isso viola a regra do projeto:

dado desconhecido permanece desconhecido.

Um projeto novo sem board definido deve ser incompleto, não uma placa fictícia de 10 × 10 mm.

2.3 Save As pode perder conteúdo do container

O writer preserva source/, assets/ e attachments/ lendo o arquivo que já existe no path de destino.

Portanto:

Open A.prdx
→ Save As B.prdx

não tem como preservar corretamente os extras de A.prdx porque o load não transporta contexto do container original.

Isso precisa ser resolvido agora.

2.4 Manifest perde dados

PrdxManifest possui:

featureFlags
sourceFingerprints

no schema, mas o writer sempre os recria vazios.

Isso é perda de informação.

2.5 Semântica de diagnostics/result está inconsistente

Diagnostic possui:

bool Blocking = true

por default.

Mas OperationResult<T>.Success considera uma Warning/Info bloqueante ainda como sucesso:

!d.Blocking || severity is Info or Warning

Enquanto ProjectLoadResult usa simplesmente:

Diagnostics.All(d => !d.Blocking)

Uma única definição deve valer no produto inteiro.

2.6 O suporte declarado a JSON Schema 2020-12 não está suficientemente garantido

O schema declara Draft 2020-12, mas o runtime atualmente faz substituição textual:

"$defs" → "definitions"
#/$defs/ → #/definitions/

antes de entregá-lo ao NJsonSchema.

Isso pode até funcionar para nosso subset atual, mas não deve ser considerado prova de conformidade de dialect.

A solução precisa ser validada por comportamento, não por esperança.

2.7 Reader ainda não cruza manifest com payload suficientemente

Hoje são validados schema, hash e várias referências internas. Isso é bom.

Mas ainda precisam ser verificados:

manifest.projectId == project.projectId
manifest.projectRevision == project.projectRevision
manifest timestamps coerentes
sourceFingerprints correspondem a sourceImports
format/schema versions são realmente suportadas
featureFlags são conhecidas
3. Migrar a fundação para .NET 10 LTS

A implementação atual usa net9.0 e SDK 9.0.316.

Faça a migração agora para .NET 10 LTS, não depois do crescimento da solução. A Microsoft atualmente classifica .NET 10 como LTS ativo até novembro de 2028, enquanto .NET 9 está em manutenção e encerra suporte em novembro de 2026.

Implementar
TargetFramework → net10.0;
global.json → SDK estável .NET 10 disponível/compatível;
CI → 10.0.x;
manter C# estável correspondente, sem preview;
preferencialmente centralizar TargetFramework em Directory.Build.props se todos os projetos atuais compartilharem a versão;
evitar repetir ImplicitUsings/Nullable em todo .csproj quando já puderem ser centrais.

Não introduzir .NET 11 preview.

4. Corrigir a semântica global de Diagnostics e Results

Adote uma regra única:

Blocking == true
→ operação não pode ser considerada sucesso


Blocking == false
→ pode coexistir com sucesso

A severidade serve para apresentação/priorização, não para contradizer Blocking.

Ajustar

Preferencialmente:

Diagnostic(
    ...,
    bool Blocking = false)

Factories:

Info       → non-blocking
Warning    → non-blocking por default
Error      → blocking por default
Fatal      → blocking

Quando existir um Warning realmente bloqueante, ele deve ser construído explicitamente.

Todos os resultados devem usar a mesma função/base:

Success = !Diagnostics.Any(x => x.Blocking);

ou helper compartilhado equivalente.

Não manter regras distintas em:

OperationResult
ProjectLoadResult
ProjectSaveResult
ProjectValidationResult

Adicionar testes diretos para isso.

5. Tornar PRDX capaz de representar projeto incompleto sem dados fictícios

O schema v0.1 ainda não foi distribuído como formato público estável. Portanto, corrija o 0.1 agora, sem criar uma migration artificial só para compatibilidade interna desta primeira semana.

5.1 Board incompleto

Permitir:

"outline": null

e:

"layers": []
"stackup": []

quando ainda não definidos/importados.

Não gerar outline falso.

5.2 Component sem footprint resolvido

Alterar:

"footprintId": "..."

para aceitar:

"footprintId": null

Isso é fundamental para futuro import de netlist incompleta.

5.3 Endpoint lógico sem pad físico resolvido

Hoje o endpoint exige componentId + padId.

Passe a suportar algo equivalente a:

{
  "componentId": "cmp_u1",
  "pinRef": "24",
  "padId": null
}

ou:

{
  "componentId": "cmp_u1",
  "pinRef": null,
  "padId": "pad_u1_24"
}

Regra:

deve existir identidade lógica suficiente para preservar a conexão, mesmo quando o mapping físico ainda não foi resolvido.

O schema deve exigir pelo menos uma referência de pin/pad adequada.

5.4 Physical state incompleto

Adicionar estado explícito, preferencialmente:

INCOMPLETE

para projeto que ainda não possui requisitos físicos suficientes.

Não usar UNROUTED para significar “não sabemos sequer qual é o board”.

5.5 Novo fixture

Adicionar:

incomplete-project.project.json

contendo deliberadamente:

nenhum board outline;
nenhuma layer;
componente sem footprint;
conexão lógica por pinRef;
estado INCOMPLETE.

Ele deve:

schema validate
→ deserialize
→ save .prdx
→ reopen

sem erro bloqueante.

Pode emitir diagnostics não bloqueantes de incompletude.

6. Substituir JsonObject CanonicalProject por modelo de domínio tipado

Este é o item mais importante da entrega.

6.1 Domain não deve expor Root

Remover como API pública:

CanonicalProject.Root
CanonicalProject.Parse(...)
CanonicalProject.ToJson(...)

Esses conceitos pertencem ao DesignExchange.

6.2 Estrutura mínima tipada

Criar modelos equivalentes a:

CanonicalProject
├── ProjectId
├── ProjectRevision
├── ProjectMetadata
├── SourceImports
├── LogicalDesign
│   ├── Components
│   ├── Footprints
│   ├── Nets
│   ├── NetClasses
│   └── Groups
├── Board
│   ├── Outline
│   ├── Layers
│   ├── Stackup
│   ├── Regions
│   └── Keepouts
├── ManufacturingProfile
├── Constraints
├── Semantics
├── PhysicalDesignState
├── ReviewDecisions
└── ProjectSettings

O Domain deve representar todo o schema atual, não só os campos que já aparecem no summary.

6.3 Entidades tipadas

Tenha tipos próprios para pelo menos:

Component
Footprint
Pad
Net
NetEndpoint
NetClass
Group


BoardDefinition
BoardLayer
StackupEntry
Region
Keepout


ConstraintDefinition
ConstraintSelector
ConstraintScope


SemanticRelationship


PhysicalDesignState
ComponentPose
Route
TrackSegment
Via
CopperZone


SourceImport
ReviewDecision


Provenance
SourcedValue

Não é necessário criar uma classe por cada pequeno enum se isso só gerar boilerplate sem benefício.

7. Usar IDs realmente tipados

Os wrappers atuais existem, mas quase não são utilizados.

Criar/aplicar IDs adequados:

ProjectId
ComponentId
FootprintId
PadId
NetId
NetClassId
LayerId
GroupId
RegionId
KeepoutId
ConstraintId
SemanticRelationshipId
PhysicalStateId
RouteId
TrackSegmentId
ViaId
CopperZoneId
SourceImportId
ReviewDecisionId

Evitar APIs como:

FindComponent(string id)

quando deveria ser:

FindComponent(ComponentId id)
Regra

Reference designator:

U17
C42
R8

é atributo humano.

Não é identidade interna.

8. Usar os tipos de unidades no domínio

O PLAN-01 criou LengthUnits corretamente com escala de 1 µm.

Agora efetivamente utilize isso em:

Point
Polygon
pad position/size
board geometry
track points
via position/diameters
component pose

Criar, por exemplo:

readonly record struct Point2(LengthUnits X, LengthUnits Y);

ou equivalente.

Não deixar o PLAN-02 receber long x, long y espalhados sem semântica.

Rotation pode continuar como decimal/Angle type explícito.

9. Criar uma camada real PRDX DTO ↔ Domain

O JSON deve ficar no DesignExchange.

Estrutura sugerida:

PlaceRouter.DesignExchange
└── Prdx
    ├── Serialization
    │   ├── PrdxProjectDto
    │   ├── PrdxManifestDto
    │   ├── ...
    │   └── PrdxJsonSerializer
    │
    ├── Mapping
    │   └── PrdxProjectMapper
    │
    ├── PrdxProjectReader
    ├── PrdxProjectWriter
    └── Schema...

Não é obrigatório criar dezenas de arquivos pequenos; agrupe tipos relacionados quando isso melhorar produtividade.

Fluxo de leitura
project.json bytes
→ schema validation
→ deserialize PRDX DTO
→ map DTO → typed Domain
→ typed canonical integrity validation
Fluxo de escrita
typed Domain
→ domain integrity validation
→ map Domain → PRDX DTO
→ serialize deterministic JSON
→ schema validate serialized payload
→ container write

Isso protege os dois lados:

JSON válido que não mapeia corretamente;
Domain válido cujo serializer produz JSON inválido.
10. Confinar JSON flexível apenas onde ele é realmente necessário

Alguns campos são deliberadamente extensíveis:

extensions
sourceMetadata
constraint parameters
graphic custom geometry metadata
generic sourced property values

Esses podem usar um tipo JSON-like controlado ou JsonElement imutável.

Não permitir que isso vire justificativa para manter:

CanonicalProject = JsonObject

Regra:

JSON cru é permitido em folhas extensíveis; não como representação do aggregate inteiro.

11. Fortalecer a validação JSON Schema

Criar uma abstraction:

IPrdxSchemaValidator

ou equivalente.

O restante do runtime não deve depender diretamente de NJsonSchema.

11.1 Remover dependência lógica de rewriting textual

Não considerar isto suficiente:

"$defs" → "definitions"

Se NJsonSchema permanecer, crie testes de conformidade para todos os recursos de schema que usamos.

No mínimo:

additionalProperties: false
const
enum
oneOf
anyOf
required
minItems/max
minimum/maximum
pattern
uniqueItems
nullable union types
references/$defs

E, se format: date-time for parte da política que queremos realmente enforcing, testar isso explicitamente.

Se a biblioteca atual não conseguir cumprir nosso subset Draft 2020-12 de forma confiável, substitua-a por biblioteca madura/licença compatível seguindo ADR-0004.

Não implemente JSON Schema manualmente.

12. Criar política explícita de versões PRDX

Neste momento, o runtime suporta exatamente a versão implementada.

Não abra silenciosamente qualquer:

0.1.x

só porque o regex aceita.

Criar algo como:

PrdxVersionPolicy
SupportedFormatVersions
SupportedSchemaVersions
SupportedFeatureFlags

Na primeira versão:

formatVersion: 0.1.0
schemaVersion: 0.1.0
featureFlags suportadas: nenhuma ou conjunto explicitamente definido

Arquivos de versão desconhecida devem gerar:

PRDX-VERSION-UNSUPPORTED

Feature flag não reconhecida:

PRDX-FEATURE-UNSUPPORTED

Nunca “tentar abrir mesmo assim” silenciosamente.

13. Validar coerência Manifest ↔ Project

Após desserializar o payload, verificar:

manifest.projectId == project.ProjectId
manifest.projectRevision == project.ProjectRevision
manifest.createdAt == project.Metadata.CreatedAt
manifest.modifiedAt == project.Metadata.ModifiedAt

Quando aplicável.

Adicionar diagnostic específico:

PRDX-MANIFEST-PROJECT-MISMATCH

Também validar:

sourceFingerprints[]
↔
project.SourceImports[]

Cada fingerprint deve apontar para SourceImportId conhecido e o SHA precisa corresponder ao registrado.

14. Fortalecer segurança e robustez do ZIP reader

O reader atual carrega entries inteiras em memória. Para project.json isso é aceitável em placas pequenas, mas não deve aceitar input ilimitado.

Criar:

PrdxReadLimits

com defaults altos o suficiente para placas reais.

Limitar pelo menos:

número total de entries
manifest uncompressed size
project.json uncompressed size
tamanho individual de supplementary entries quando aplicável

Detectar:

duplicate manifest.json
duplicate project.json

e rejeitar.

Adicionar diagnostics como:

PRDX-ENTRY-DUPLICATE
PRDX-ENTRY-TOO-LARGE

Não extrair caminhos ZIP para filesystem neste plano.

15. Validar UTF-8 estritamente

Trocar decodificação permissiva por UTF-8 estrito:

new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true)

Manifest/project com bytes inválidos devem falhar com diagnostic claro:

PRDX-UTF8-INVALID

Não aceitar silenciosamente �.

16. Melhorar o Canonical Integrity Validator

A implementação atual já é substancial e deve ser aproveitada, não jogada fora.

Refatore-o para operar sobre o modelo tipado.

Além do que já verifica, incluir:

IDs e estrutura
unicidade de todos os IDs;
pads únicos;
track IDs;
route IDs;
group IDs etc.
Groups
membros existem;
nested group references existem;
detectar ciclos se hierarquia permitir nesting.
Reference designator

Detectar refdes duplicado.

Para uma PCB normal isso deve ser pelo menos diagnostic forte; escolha severity consistente com a documentação.

Net endpoints

Quando padId != null:

pad existe;
pad pertence ao footprint do component.

Quando padId == null e pinRef != null:

estado é estruturalmente válido;
emitir eventualmente:
PRDX-PAD-MAPPING-UNRESOLVED

não bloqueante.

Footprints

footprintId == null:

PRDX-FOOTPRINT-UNRESOLVED

não bloqueante nesta camada.

Não confundir “projeto ainda não pronto para placement” com “arquivo estruturalmente inválido”.

Physical state

Verificar:

component pose máximo 1/componente
route net existe
via net existe
route via belongs to same net
track layer existe
route/via layers são copper-capable
basedOnProjectRevision não aponta para revisão futura

Não implementar clearance/overlap/DRC geométrico aqui.

17. Criar ProjectDocument/file context na Application Layer

Hoje um load retorna somente:

CanonicalProject
+
Diagnostics

Isso é insuficiente para preservar o container.

Criar conceito equivalente a:

ProjectDocument
├── CanonicalProject Project
└── ProjectFileContext FileContext

ProjectFileContext deve carregar informações necessárias para salvar corretamente:

SourcePath
PRDX format version
feature flags
source fingerprints
supplementary entries

Não colocar ZipArchive aberto ou streams persistentes dentro dele.

Supplementary entries podem ser descritas por metadata + source container/path e copiadas sob demanda.

18. Corrigir Save e Save As

O comportamento obrigatório é:

Load A.prdx
contains:
  source/original.dsn
  assets/reference.png
  attachments/note.txt


Save As B.prdx


B.prdx MUST contain byte-identical copies
of all three supplementary entries

Não depender de B.prdx já existir.

Implementação

O writer deve usar o ProjectFileContext da origem.

Preferir stream-copy:

source ZIP entry stream
→ target ZIP entry stream

Não carregar um asset de centenas de MB inteiro num byte[].

Manifest

Preservar feature flags suportadas.

Regerar sourceFingerprints a partir do canonical project/source imports ou preservá-los somente depois de validar consistência.

Não zerá-los automaticamente.

19. Tornar a serialização determinística

Definir explicitamente:

UTF-8 sem BOM;
newline consistente;
property ordering consistente;
collections que possuem semântica ordenada preservam ordem;
dictionaries/extension keys ordenadas para saída quando ordem não é semântica;
formatting estável.

O mesmo Domain inalterado salvo duas vezes deve produzir o mesmo project.json e o mesmo payload SHA, exceto campos que tenham sido explicitamente alterados.

Não atualizar modifiedAt automaticamente só porque “Save” ocorreu se o projeto semanticamente não mudou. A atualização de revision/timestamps deve pertencer ao lifecycle/transação do projeto, não ao ZIP writer.

20. Substituir o test hook público de atomic save

Hoje existe:

PrdxWriteOptions(Action<string>? BeforeCommit)

na Application API.

Isso é claramente um seam de teste vazando para produção.

Remover.

Criar uma dependência interna apropriada, por exemplo:

IAtomicFileCommitter

ou filesystem abstraction pequena.

Default implementation:

write temp in destination directory
→ validate temp
→ flush to disk
→ atomic replace when supported
→ safe fallback preserving original
Fallback

Se File.Replace não estiver disponível/for inadequado:

destination → backup
temp → destination
if failure:
    restore backup

O fallback pode não ser estritamente atômico, mas nunca deve sacrificar o arquivo anterior sem possibilidade de recovery.

21. Corrigir o teste de atomic save

O teste atual pode passar mesmo se a falha simulada nunca chegar ao ponto de commit.

Ele cria um CreateEmpty() e apenas verifica que a operação falhou e o arquivo original permaneceu.

Após este refactor:

use projeto sabidamente válido;
injete IAtomicFileCommitter que conta chamadas;
faça Commit() lançar depois do temp ter sido criado e validado;
assert:
committer was invoked == true
save failed
destination bytes unchanged
destination still loadable
temp/backup cleanup sane

Isso prova atomicidade real.

22. Limpar o boundary Application / DesignExchange / Infrastructure

Hoje DesignExchange referencia Application para implementar interfaces, o que é aceitável via Ports & Adapters, mas a composition está dentro de:

PrdxRuntime.CreateProjectService()

no próprio DesignExchange.

Enquanto Infrastructure está vazio.

Corrigir.

Estrutura desejada
Application
  ProjectService
  IProjectStore / persistence port
  ProjectDocument contracts


DesignExchange
  PrdxProjectStore / reader/writer
  DTOs
  mapper
  schema validator
  container implementation


Infrastructure
  composition root / service wiring


CLI
  Application + Infrastructure

O CLI não deve precisar:

using PlaceRouter.DesignExchange.Prdx;

como ocorre hoje.

Ele pede ao composition root um Application service.

Se depois desta correção Infrastructure ainda não tiver nenhuma responsabilidade real, remova o projeto em vez de mantê-lo vazio. Mas a preferência é utilizá-lo para composition.

23. Tornar a CLI testável e mais estrita

A CLI atual é funcional e deve permanecer simples.

Refatorar para:

Program.cs
→ composition
→ CliApplication

CliApplication deve poder receber:

ProjectService
stdout abstraction/TextWriter
stderr abstraction/TextWriter

para testes in-process.

Parsing

Aceitar:

placerouter validate file.prdx
placerouter validate file.prdx --json
placerouter inspect file.prdx
placerouter inspect file.prdx --json
placerouter --help
placerouter --version

Rejeitar:

--foo
argumentos posicionais extras
combinações inválidas

Não ignorar flags desconhecidas.

Exit codes

Manter:

0 success
2 invalid input/project/usage
3 internal failure

e documentar.

JSON mode

--json deve produzir somente JSON em stdout.

Incluir pelo menos:

{
  "valid": true,
  "diagnostics": [],
  "summary": {},
  "formatVersion": "0.1.0",
  "schemaVersion": "0.1.0"
}
24. Testes obrigatórios desta rodada

Não criar centenas de testes. Criar um conjunto pequeno que prove os contracts frágeis.

Obrigatórios:

Domain
Projeto incompleto não inventa board/layers.
Typed IDs e units são preservados.
Typed CanonicalProject contém corretamente dados do fixture completo.
Semantic equality depois de round-trip.
Schema
Fixture completo válido.
Fixture incompleto válido.
additionalProperties=false realmente rejeita extra inesperado.
const realmente rejeita valor errado.
oneOf/anyOf de endpoint funciona.
enum/pattern/minimum/uniqueItems usados no PRDX realmente funcionam.
Package/load
Hash inválido.
Manifest/project ID mismatch.
Revision mismatch.
Unsupported format version.
Unsupported project schema version.
Unsupported feature flag.
Duplicate project.json.
Invalid UTF-8.
Entry above configured size limit.
Missing embedded source referenced pelo project gera diagnostic apropriado.
Integrity
Duplicate IDs.
Missing component.
Wrong pad/footprint.
Missing layer.
Group member inexistente.
Group cycle.
route/via net mismatch.
unresolved footprint/pad é warning/nonblocking, não corrupção.
Save
Save/reopen full fixture.
Save As preserves source/, assets/, attachments/.
Source fingerprints remain correct.
Same project serialized twice produces identical payload hash.
Commit failure preserves original file.
Safe fallback path also preserves original on simulated failure.
CLI
validate valid = 0.
validate invalid = 2.
unknown option = 2.
JSON output é JSON parseável sem lixo.
inspect returns expected typed summary.

Isso parece bastante em número, mas muitos são testes de poucas linhas e parametrizáveis. Agrupe-os quando apropriado.

25. Parar de executar dotnet run dentro da maioria dos testes

Hoje o teste de CLI cria um processo e executa:

dotnet run --project ...

Isso é lento e dispara build aninhado.

Após extrair CliApplication, testar a maioria dos casos in-process.

Manter no máximo um smoke test real do executável se for útil.

26. Melhorar CI para provar portabilidade do core

O engine é arquiteturalmente multiplataforma/headless.

O CI atual já passa em Windows.

Transformar o job central em matrix:

windows-latest
ubuntu-latest

Não adicionar macOS agora.

Executar:

dotnet restore PlaceRouter.sln
dotnet build PlaceRouter.sln -c Release --no-restore
dotnet test PlaceRouter.sln -c Release --no-build

com .NET 10.

Isso é especialmente importante para:

path handling;
ZIP;
atomic file operations;
case sensitivity;
newline assumptions.

Não adicionar code coverage pesada ou pipeline de release.

27. Centralizar package/toolchain sem sobreengenharia

Se ainda não existir, usar:

Directory.Packages.props

para packages compartilhados ou pelo menos garantir versões centralizadas.

Não é obrigatório centralizar um package usado por exatamente um projeto se isso criar ruído maior.

Revisar dependências existentes e registrar licença da biblioteca de JSON Schema conforme ADR-0004.

28. Atualizar documentação somente onde os contracts mudaram

Atualizar:

schemas/prdx/0.1/README.md
docs/11-Formato-de-Projeto-Persistencia-Lifecycle-e-Exportacao.md

para explicitar:

PRDX aceita projetos incompletos;
outline/layers/footprint/pad mapping podem estar unresolved;
INCOMPLETE;
manifest/version policy;
Save As preserva supplementary entries;
canonical runtime é typed Domain, não JSON tree.

Atualizar docs/09 apenas se necessário para refletir decisão durável.

Não reescrever os documentos de arquitetura inteiros.

29. Remover código obsoleto depois da migração

Ao terminar, não deixar dois mundos coexistindo.

Remover:

CanonicalProject.Root como API pública
JSON-path helpers antigos
CanonicalProject.Parse/ToJson no Domain
PrdxWriteOptions.BeforeCommit
PrdxRuntime no DesignExchange
mappers/validators antigos que ficaram mortos

Não deixar “legacy compatibility” para uma implementação que nunca foi lançada.

30. O que está explicitamente fora deste plano

Não implementar:

Clipper2
geometry boolean operations
spatial index
physical constraint evaluation
readiness completo
DSN importer
Avalonia
board renderer
placement
global routing
detailed routing
DeepSeek
Gerber
PRDX migration framework entre versões futuras

Tudo isso começa no PLAN-02 ou posterior.

Este plano serve para consolidar a superfície sobre a qual o PLAN-02 será construído.

31. Critério final de aceitação

O PLAN-01R só está concluído quando todos estes fluxos funcionarem:

Fluxo A — projeto incompleto
CreateProject("Test")
→ no fake outline
→ no fake copper layers
→ status INCOMPLETE
→ Save .prdx
→ Load
→ valid canonical project
→ nonblocking readiness-like diagnostics only
Fluxo B — fixture completo
Load minimal-2layer project
→ schema valid
→ typed Domain populated
→ component/net/layer IDs strongly typed
→ integrity valid
→ Save
→ Load
→ semantic Domain equivalent
Fluxo C — Save As
A.prdx
├ source/original.dsn
├ assets/reference.png
└ attachments/note.txt


Load A
→ Save As B
→ reopen B
→ same canonical project
→ supplementary entries present
→ byte-identical
→ source fingerprints correct
Fluxo D — corruption handling

Cada um deve falhar de forma tipada:

bad ZIP
missing manifest
wrong hash
unsupported version
unknown feature flag
manifest/project mismatch
duplicate project.json
bad UTF-8
invalid cross-reference
oversized payload

Sem stack trace cru como comportamento normal.

Fluxo E — atomicity
valid destination exists
→ save new version
→ injected commit failure after temp validation
→ destination remains byte-identical and loadable
Fluxo F — CLI
placerouter validate valid.prdx
→ exit 0


placerouter validate corrupt.prdx
→ exit 2


placerouter inspect valid.prdx --json
→ clean valid JSON


placerouter validate valid.prdx --unknown
→ exit 2
Fluxo G — CI
Windows Release build/test = PASS
Ubuntu Release build/test  = PASS
32. Condição para liberar PLAN-02

Depois desta entrega, um agente do PLAN-02 deve conseguir escrever código conceitualmente assim:

foreach (var component in project.LogicalDesign.Components)
{
    ComponentId id = component.Id;
    FootprintId? footprint = component.FootprintId;
}


var outline = project.Board.Outline;


foreach (var layer in project.Board.Layers)
{
    LayerId id = layer.Id;
}


PhysicalDesignState state = project.PhysicalDesignState;

e não isto:

project.Root["logicalDesign"]?["components"]

Esse é o principal gate técnico para considerar a fundação realmente concluída.

33. Relatório final obrigatório do agente

Ao terminar, o agente deve informar somente:

commit/base executado;
versão .NET final;
novo desenho Domain ↔ DTO ↔ PRDX;
alteração feita no schema para projetos incompletos;
comportamento de Save As e supplementary entries;
versão/feature policy;
atomic save implementation;
lista resumida de diagnostics novos;
resultados Windows/Ubuntu;
número de testes e resultado;
demonstração dos quatro fluxos principais;
confirmação explícita:
PLAN-01 foundation is ready for PLAN-02.
No production Domain code requires JsonNode/JsonObject navigation.

Não encerrar a execução enquanto algum item deste plano continuar pendente e executável.