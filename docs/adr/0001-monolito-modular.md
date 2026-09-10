# ADR 0001 — Monólito modular com workers separados

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0

## Contexto

A Torre Logística tem módulos bem distintos — entregas, rotas, rastreamento,
geoespacial, alertas, ocorrências, comprovantes, integrações, auditoria — e precisa
parecer software operacional real, não exercício acadêmico. Existe a tentação de
nascer em microsserviços por esse motivo.

Só que os módulos compartilham um núcleo transacional estreito e altamente acoplado
por invariante: concluir uma entrega precisa, no mesmo commit, mudar o estado,
registrar evento de timeline, gravar metadados do comprovante e enfileirar o evento
de saída. Em serviços separados isso viraria coordenação distribuída — saga,
compensação, consistência eventual — para um problema que uma transação resolve.

## Decisão

Um monólito modular no núcleo, com fronteiras explícitas entre módulos, mais
processos separados para carga assíncrona:

```
TorreLogistica.Domain          regras e invariantes, sem infraestrutura
TorreLogistica.Application     casos de uso e orquestração
TorreLogistica.Infrastructure  PostgreSQL/PostGIS, storage, mensageria, relógio
TorreLogistica.Api             composition root HTTP
TorreLogistica.Workers         composition root assíncrono
TorreLogistica.Simulator       cliente externo, sem acesso ao núcleo
```

A direção de dependência é sempre para dentro: `Api`/`Workers` → `Infrastructure` →
`Application` → `Domain`. O domínio não referencia projeto nenhum.

Migrar para serviços separados exige necessidade comprovada e autorização explícita.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Microsserviços por módulo | O núcleo transacional de entrega ficaria distribuído; pagaríamos saga e consistência eventual sem ter problema de escala ou de time que justifique |
| Camada única (projeto só) | Nada impediria um controller de conter regra de negócio; a fronteira viraria convenção verbal, que não sobrevive a pressa |
| Monólito com pastas, sem projetos | Sem fronteira de compilação, a dependência proibida entra por autocomplete e ninguém percebe |

## Consequências

- Uma transação cobre o que precisa ser atômico; nada de dual-write no caminho crítico.
- Um deploy para o núcleo, outro para workers. Operação mais simples e mais barata.
- A fronteira entre módulos depende de disciplina, porque estão no mesmo processo.
  Por isso ela é verificada por teste, não confiada ao bom senso.
- Escala é vertical primeiro. Se um módulo exigir escala própria, a extração é
  possível justamente porque as fronteiras foram mantidas desde o início.

## Como isto é verificado

`TorreLogistica.ArchitectureTests` lê os arquivos `.csproj` e os metadados dos
assemblies e reprova:

- qualquer referência de projeto no `Domain`;
- pacote de infraestrutura (EF Core, ASP.NET Core, Npgsql, Serilog, AWS SDK) no `Domain`;
- `Application` referenciando `Infrastructure` ou `Api`;
- `Infrastructure` referenciando ASP.NET Core.

A leitura do `.csproj` é deliberada: uma referência declarada e ainda não usada não
aparece nos metadados do assembly compilado e passaria por uma checagem feita apenas
em tempo de execução.
