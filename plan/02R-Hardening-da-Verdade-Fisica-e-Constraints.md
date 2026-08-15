# PLAN-02R — Hardening da verdade fisica e constraints

**Status:** APPROVED  
**Pre-requisito obrigatorio:** PLAN-02 implementado em `7c27ca7`  
**Desbloqueia:** PLAN-03 e PLAN-06

---

## 1. Objetivo

Consolidar o PLAN-02 antes de permitir que import lifecycle, global routing ou detailed routing dependam da camada geometrica/constraint como verdade fisica deterministica.

Esta rodada corrige pontos em que a infraestrutura existe, mas ainda pode responder de forma fisicamente errada, mascarar `Unknown` como valor default, ou aplicar constraints fora do seu selector/scope.

---

## 2. Escopo obrigatorio

1. Corrigir `ManufacturingProfile` para distinguir `KNOWN`, `DEFAULT` e `UNKNOWN`, sem transformar dado ausente em hard rule silenciosa.
2. Implementar `EffectiveConstraintSet` real para responder a regra efetiva por tipo/alvo/scope, com refinamento por especificidade e conflitos Required.
3. Fazer `REQUIRED + UNKNOWN` material bloquear validade/readiness.
4. Gerar geometria exata inicial para pad shapes suportados, tracks diagonais, vias e arcos quando possivel; quando houver aproximação poligonal, ela deve ser deterministica e documentada no codigo.
5. Aplicar layer mirroring para componentes no bottom side.
6. Associar pads a nets no `PhysicalGeometryModel`.
7. Incluir pads nas avaliacoes de clearance e component-to-net separation.
8. Preservar e testar semantica basica de holes/cutouts em containment/intersection/offset.
9. Respeitar selectors, scopes e layers nos evaluators, sem ampliar selector vazio para "todos" salvo selector `ALL`.
10. Implementar keepout por `layerIds` e `appliesTo` para componentes, tracks, vias e copper zones.
11. Completar constraints de manufacturing iniciais: annular ring, allowed via types, layer count/type compatibility e minimum component spacing.
12. Tornar finding IDs estaveis e unicos por constraint + entidades afetadas + classe/status.
13. Substituir testes que passavam por coincidencia e adicionar regressões especificas para os itens acima.

---

## 3. Criterios de aceite

- Ausencia de capability manufacturing material gera `UNKNOWN` e readiness/finding coerentes, nao default silencioso.
- `minimumTraceWidth` do PRDX e aliases documentados sao lidos corretamente.
- Uma consulta de regra efetiva para net/track/entity retorna a constraint efetiva, nao uma lista sem resolucao.
- Nenhum estado com Required material `UNKNOWN` eh `CandidateValid`.
- Pads bottom ficam nas layers bottom equivalentes quando existir par top/bottom.
- Pads carregam `NetId` quando endpoints de net apontam para eles.
- Clearance considera pad/track/via/zone conforme selector/scope/layer.
- Tracks diagonais e vias usam geometria poligonal fisica, nao AABB como exact geometry.
- Keepouts respeitam appliesTo/layers.
- Build e testes passam localmente e no CI Windows/Ubuntu.
