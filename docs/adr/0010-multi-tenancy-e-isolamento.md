# ADR 0010 — Multi-tenancy por discriminador, com filtro global que falha fechado

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 1

## Contexto

Multi-tenancy é requisito estrutural (CLAUDE.md, seção 13), inclusive na demonstração. O critério
de aceite da Fase 1 é explícito: **tenant A não consegue ler, alterar ou inferir recurso do tenant B.**

"Inferir" é a parte que costuma escapar. Negar o acesso com `403` já revela que o recurso existe.
Uma mensagem de conflito diferente, um tempo de resposta diferente ou um erro `500` que só
acontece para registros reais também revelam.

## Decisão

### Discriminador `organizacao_id` em toda tabela de negócio

Um banco, um schema, uma coluna de organização em cada entidade pertencente a tenant.
Schema por tenant e banco por tenant ficam descartados para a v1 (ver alternativas).

### O tenant vem só da sessão

`IContextoDoTenant` é implementado na API lendo a reivindicação `org` do principal autenticado —
que só existe depois de assinatura, audiência, validade **e** conferência da sessão no banco
([ADR 0009](./0009-autenticacao-e-sessao.md)). Nenhuma rota, query string ou corpo carrega
organização.

A defesa contra o cliente mandar uma organização mesmo assim é estrutural: a API recusa campo
desconhecido no JSON (`UnmappedMemberHandling.Disallow`). `"organizacaoId": "…"` num cadastro vira
`400`, e não um campo descartado em silêncio que alguém um dia passe a ler.

### Filtro global que falha fechado

O `TorreLogisticaDbContext` aplica filtro de consulta em toda entidade de tenant:

```csharp
usuario => usuario.OrganizacaoId == OrganizacaoIdDoFiltro
// OrganizacaoIdDoFiltro = tenant da sessão ?? Guid.Empty
```

Sem sessão, o filtro compara com `Guid.Empty`, que nenhuma organização tem — a consulta não
enxerga nada. Esquecer de resolver o tenant produz lista vazia e `404`, nunca dado alheio.

Workers e demais processos sem requisição recebem `ContextoDeTenantAusente`, com o mesmo efeito.
Quando precisarem operar sobre uma organização, essa autoridade terá de chegar a eles de forma
explícita.

Os poucos fluxos que precisam atravessar organizações — login, renovação, logout e a conferência
de sessão, que acontecem **antes** de haver tenant — usam `IgnoreQueryFilters()` de forma explícita
e restrita ao identificador que já têm em mãos.

### Recurso de outro tenant é `404`, idêntico ao inexistente

Não há `403` para recurso de outra organização: para a consulta filtrada ele simplesmente não
existe. Os testes comparam a resposta sobre um recurso de B com a resposta sobre um identificador
inventado, campo a campo.

### E-mail único por organização, login com a organização

O índice de unicidade é `(organizacao_id, email_normalizado)`, não `email_normalizado`.

Com unicidade global, o administrador de A que tentasse cadastrar um e-mail já usado em B receberia
`409` — e acabaria de descobrir que aquela pessoa tem conta em outra organização cliente. Com a
unicidade por organização, o cadastro simplesmente funciona.

A consequência é que o login precisa saber a organização: o formulário pede identificador da
organização (slug), e-mail e senha. Organização inexistente responde exatamente como senha errada.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Schema por tenant | Migration multiplicada pelo número de clientes, pool de conexão por schema, e o esquecimento de trocar o `search_path` vira vazamento — o mesmo risco, só que mais difícil de testar |
| Banco por tenant | Custo fixo por cliente, incompatível com uma demonstração pública barata |
| Row-Level Security do PostgreSQL | Defesa forte, mas exige propagar o tenant para a sessão do banco em cada conexão do pool; fica como camada adicional candidata na Fase 20, não como substituto do filtro |
| Filtrar manualmente em cada consulta | Um `Where` esquecido é um vazamento; filtro global torna o esquecimento seguro |
| E-mail único global com login só por e-mail | Formulário mais simples, ao preço de um canal de inferência entre tenants |
| `403` para recurso de outro tenant | Confirma a existência do recurso |

## Consequências

- Toda entidade nova de tenant precisa de `OrganizacaoId` **e** de entrada no `OnModelCreating`
  com filtro. O Gate de tenant do ROADMAP exige teste cross-tenant para cada uma.
- Consulta que ignora filtro é exceção visível no código (`IgnoreQueryFilters`) e deve ser
  justificada no ponto de uso.
- Uma pessoa com acesso a duas organizações tem duas contas. Aceitável na v1; convite e conta
  multi-organização exigiriam ADR próprio.
- Índices de consulta frequente começam por `organizacao_id`.

## Como isto é verificado

`IsolamentoEntreTenantsTestes`, contra PostgreSQL real:

- leitura, troca de perfil e desativação de conta de B por administrador de A → `404` idêntico
  ao de identificador inexistente, e o estado de B intacto — inclusive a sessão da conta de B;
- listagem de A contém exatamente as contas de A;
- `organizacaoId` no corpo → `400`, nada criado;
- e-mail usado em B cadastra normalmente em A;
- conta de B não autentica usando o slug de A;
- **filtro global direto no contexto**: com tenant A enxerga só A; sem tenant, zero usuários e
  zero sessões.

`AutenticacaoTestes.TodasAsFalhasDeLoginSaoIndistinguiveis` cobre o lado da enumeração no login.
