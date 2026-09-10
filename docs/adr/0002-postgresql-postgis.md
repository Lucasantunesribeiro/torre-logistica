# ADR 0002 — PostgreSQL com PostGIS como banco principal

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0

## Contexto

O produto é geoespacial no núcleo, não na borda: distância até o destino, raio de
geofence, detecção de entrada e saída de área, busca por proximidade, rota
percorrida. Junto disso há um modelo relacional com invariantes fortes — uma entrega
não pode estar em duas rotas ativas, um código humano é único por organização, uma
posição repetida não pode duplicar histórico.

## Decisão

PostgreSQL com a extensão PostGIS é a fonte de verdade do domínio operacional.

A extensão é declarada no modelo (`HasPostgresExtension("postgis")`) e entra no banco
pela primeira migration. Não é pré-requisito informal de ambiente: é parte do schema.

Cálculo geodésico fica no banco, com `ST_DWithin`, `ST_Distance` e tipo `geography`.
Não será reimplementado em C#.

Nomes físicos em `snake_case`, via `EFCore.NamingConventions`. Instantes em
`timestamptz`, sempre UTC.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| PostgreSQL sem PostGIS, geo em C# | Haversine em C# resolve distância e para aí: índice espacial, geofence como área e consulta por proximidade não têm substituto razoável em memória |
| MongoDB com índice geoespacial | O modelo tem invariante relacional e precisa de transação e constraint; abrir mão disso para ganhar geo que o PostGIS já faz melhor é troca ruim |
| SQL Server com tipos espaciais | Funciona, mas custa mais em nuvem e tem menos oferta gerenciada barata; PostGIS é o padrão de fato no assunto |
| Banco relacional + serviço geo separado | Duas fontes de verdade para a mesma posição, com sincronização para manter |

## Consequências

- Distância, raio e geofence são uma consulta, com índice GiST quando necessário.
- O teste geoespacial precisa de banco real: PostGIS mockado não prova geografia.
  Daí a decisão de usar Testcontainers na suíte de integração.
- Em nuvem, a instância gerenciada precisa ter PostGIS disponível, e criar extensão
  exige papel administrativo (`rds_superuser` na AWS). É requisito da Fase 25.
- `snake_case` vale também para a tabela de histórico de migrations, cuja coluna é
  `migration_id` — detalhe que aparece em consulta manual.

## Como isto é verificado

Na suíte de integração, contra PostgreSQL 17 com PostGIS 3.5 real via Testcontainers:

- `postgis_version()` responde depois da migration;
- `ST_Distance` sobre `geography` devolve distância em metros dentro da faixa esperada;
- a coluna da tabela de migrations é `migration_id`, provando a convenção de nomes ativa.
