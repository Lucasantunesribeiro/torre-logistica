# ADR 0008 — Application usa o núcleo do EF Core, sem repositório genérico

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 1
- **Altera:** a tabela de dependências do [ADR 0001](./0001-monolito-modular.md), que listava para a
  Application só "Domain + abstrações de DI e log".

## Contexto

A Fase 1 trouxe os primeiros casos de uso com acesso a dados: login, renovação de sessão,
gestão de contas. Eles precisam de LINQ com projeção, `Join`, `AnyAsync`, `ExecuteUpdateAsync`
atômico e transação com trava de linha.

O caminho clássico seria a Application declarar `IRepositorioDeUsuarios` e a Infrastructure
implementá-lo. Na prática, cada método desse repositório seria uma linha repassando a chamada
para o EF Core — e a cada consulta nova nasceria um método novo no repositório. O CLAUDE.md
(seção 61) proíbe exatamente isso: repositório genérico universal e wrapper sem valor sobre EF Core.

## Decisão

A Application referencia **somente o pacote núcleo** `Microsoft.EntityFrameworkCore` e declara
`IContextoDePersistencia`, que expõe os `DbSet` diretamente e mais quatro operações que dependem
do banco concreto:

| Operação | Por que não fica na Application |
|---|---|
| `ExecutarEmTransacaoAsync` | precisa da estratégia de nova tentativa do provedor |
| `BloquearSessaoAsync` / `BloquearOrganizacaoAsync` | `SELECT … FOR UPDATE` é SQL do PostgreSQL |
| `EhViolacaoDeUnicidade` | inspeciona `PostgresException`, tipo do Npgsql |

O `TorreLogisticaDbContext` da Infrastructure implementa a interface.

A Application **não** referencia `Microsoft.EntityFrameworkCore.Relational`, Npgsql, ASP.NET Core
nem pacotes de token. SQL específico mora na Infrastructure.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Repositório por agregado | Repassaria chamadas sem isolar nada; esconderia `ExecuteUpdate` e projeção atrás de métodos que cresceriam a cada consulta |
| Casos de uso dentro da Infrastructure | Regra de aplicação misturada com detalhe de banco; a camada perderia o sentido |
| Application sem EF, com CQRS e handlers em Infrastructure | Duplicaria a camada inteira para isolar uma dependência que não vai mudar — o banco é decisão de arquitetura (ADR 0002) |

## Consequências

- Casos de uso leem como consultas de verdade, com projeção direta para o DTO — sem carregar
  entidade inteira para montar um resumo.
- Teste de caso de uso passa a exigir banco. É o que o projeto já decidiu fazer (Testcontainers):
  concorrência, restrição e trava não se provam com dublê.
- O filtro de tenant do contexto vale para toda consulta da Application automaticamente
  ([ADR 0010](./0010-multi-tenancy-e-isolamento.md)).
- Trocar de ORM exigiria reescrever a Application. Aceito: o banco e o ORM são decisões estáveis
  do projeto, e proteger-se contra essa troca custaria mais do que a troca.

## Como isto é verificado

`RegrasDoDominioEDaApplicationTestes.ApplicationUsaSoONucleoDoEfCore` reprova a Application se
ela referenciar Npgsql, EF Core Relational, ASP.NET Core ou pacotes de token.
`DependenciasEntreCamadasTestes` continua reprovando Application → Infrastructure.
