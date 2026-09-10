# ADR 0004 — UUIDv7 como identificador interno padrão

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0

## Contexto

O sistema tem tabelas que só crescem e são escritas em rajada: `posicoes`,
`eventos_da_entrega`, `alertas_operacionais`, `outbox`. A meta de engenharia é
500 motoristas enviando posição a cada 15 segundos, cerca de 33 gravações por segundo.

Identificador precisa ainda ser gerado pelo cliente antes de chegar ao banco — a PWA
do motorista cria operações enquanto offline e as envia depois.

## Contexto do problema com as alternativas óbvias

`int` sequencial exige ida ao banco para saber o identificador, vaza volume de negócio
na URL (`/entregas/1043` conta quantas entregas existem) e permite varredura.

UUIDv4 é aleatório. Em índice B-tree isso significa inserção em ponto aleatório da
árvore a cada linha: as páginas fragmentam, o índice cresce mais que o necessário e a
escrita perde localidade de cache.

## Decisão

UUIDv7 (`Guid.CreateVersion7`) é o identificador interno padrão.

Os 48 bits mais significativos carregam o instante de criação em milissegundos, então
identificadores criados em sequência ordenam em sequência — a inserção volta a ser no
fim do índice, como em um sequencial, sem deixar de ser gerável pelo cliente.

A geração passa pela abstração `IGeradorDeIdentificador`, ancorada em `IRelogio`, para
que simulador e teste determinístico controlem a sequência.

Entidades com exposição operacional ganham **também** um código humano
(`ENT-2026-004821`). Código humano nunca é chave primária.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| `bigint` identity | Exige round-trip ao banco; impede a PWA de criar identificador offline; expõe volume e permite enumeração |
| UUIDv4 | Fragmenta índice em tabela de alta escrita, que é justamente o perfil de `posicoes` |
| ULID | Resolve o mesmo problema, mas não é tipo nativo do .NET nem do PostgreSQL: precisaria de conversor e de biblioteca |
| UUIDv7 como único identificador, sem código humano | Ninguém dita `01a0892305427f24…` no telefone |

## Consequências

- Índice de tabela append-only mantém localidade de escrita.
- O identificador **revela o instante de criação** com precisão de milissegundo. Isso
  é aceitável para identificador interno e é um motivo adicional para o token de
  rastreamento público **não** ser um UUIDv7: token público é aleatório e guardado
  como hash (Fase 15).
- Ordenação por identificador é ordenação por criação aproximada — útil, mas não
  substitui carimbo de tempo explícito, que continua existindo nas entidades.

## Como isto é verificado

Testes de unidade:

- identificador gerado é versão 7 com a variante da RFC 9562;
- o instante embutido é preservado com precisão de milissegundo;
- 50 identificadores criados em milissegundos crescentes ordenam igual por texto —
  a propriedade que justifica a escolha;
- mil identificadores gerados com o relógio **parado** não se repetem;
- `Guid.NewGuid()` (versão 4) é recusado por `Uuid7.ExtrairInstanteDeCriacao`.
