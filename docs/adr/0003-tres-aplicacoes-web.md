# ADR 0003 — Três aplicações web separadas em vez de uma

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0

## Contexto

São três públicos com necessidades incompatíveis:

| Público | Contexto de uso | Autoridade |
|---|---|---|
| Operador, supervisor, administrador | desktop, mapa, informação densa | sessão administrativa |
| Motorista | celular, na rua, conexão instável, uma tarefa por vez | identidade de motorista |
| Destinatário da entrega | celular, link recebido, uso único | token público, sem login |

## Decisão

Três aplicações independentes no mesmo workspace npm:

```
apps/operacao       console operacional   porta 5173
apps/motorista      PWA do motorista      porta 5174
apps/rastreamento   rastreamento público  porta 5175
```

Stack comum: React 19, TypeScript em modo estrito, Vite, React Router, TanStack Query
para estado de servidor e Zod para validar configuração de entrada.

A porta de cada aplicação é fixa (`strictPort: true`) porque a política de CORS da API
lista origens exatas por ambiente. Porta escolhida automaticamente quebraria a
liberação sem deixar pista.

O rastreamento público não carrega TanStack Query nesta fase: não tem estado de
servidor ainda, e provider sem consumidor é peça decorativa.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Uma aplicação com layouts por perfil | O pacote do motorista carregaria o mapa e as telas administrativas; pior, a tela do motorista viraria o console comprimido — exatamente o que o CLAUDE.md proíbe |
| Uma aplicação para admin e outra juntando motorista com público | Motorista é sessão autenticada e rastreamento é anônimo por token; misturar as duas no mesmo pacote e na mesma origem é convidar vazamento de sessão |
| Três repositórios | Versionar junto o contrato da API e seus três clientes é mais simples do que sincronizar repositórios |
| Next.js com rotas por público | Renderização no servidor não resolve problema que este produto tenha; acrescentaria um runtime para hospedar e pagar |

## Consequências

- O pacote do motorista é pequeno, o que importa em rede móvel.
- A página pública pode ser servida de origem separada, com política própria.
- Há alguma duplicação de infraestrutura de cliente (leitura de ambiente, cliente
  HTTP). É aceita de propósito nesta fase: são poucas dezenas de linhas, e extrair
  pacote compartilhado antes de saber o que realmente se repete produziria abstração
  errada. A Fase 18 reavalia com código de verdade na mão.
- Três builds, três deploys. O pipeline já trata as três em paralelo.

## Como isto é verificado

- `npm run typecheck`, `npm run lint`, `npm run test` e `npm run build` rodam nas três
  aplicações e reprovam a CI individualmente.
- Teste do rastreamento público verifica que a página **não** expõe estado interno da
  operação — nenhum elemento com `role="status"`, nenhuma menção ao estado da operação.
