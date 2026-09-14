# ADR 0016 — Geofence de destino: transição avaliada na ingestão, distância no PostGIS, histerese e estado por entrega

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 7

## Contexto

A Fase 7 põe o PostGIS no domínio operacional: distância, raio, geofence do destino, detecção de
`fora → dentro`, registro da entrada e proteção contra evento repetido enquanto o motorista
permanece dentro. A entrada pode levar a entrega de `EmRota` a `ProximaDoDestino`, e GPS antigo não
pode produzir transição retroativa.

As peças já existiam: a entrega guarda a coordenada do destino em `geography(Point,4326)` com índice
GiST (Fase 3); a posição atual só avança por captura e sequência (Fase 6); a máquina de estados tem
a transição `EmRota → ProximaDoDestino` pelo comando manual de chegada (Fase 5).

## Decisão

### Onde a geofence é avaliada

Na ingestão, dentro da mesma transação do lote, **para cada posição que avançou a posição atual**.
Posição fora de ordem, duplicada ou imprecisa não avança a posição atual e por isso nunca chega à
geofence. Uma segunda barreira fica no estado: ele recusa qualquer captura mais antiga que a última
avaliada. GPS antigo não decide transição por dois caminhos independentes.

Avaliar só a última posição do lote perderia entrada e saída ocorridas durante uma perda de sinal;
por isso cada posição que avança é avaliada.

### Distância e raio no PostGIS

```sql
ST_Distance(entregas.localizacao, ponto)             -- metros, geodésico (geography)
ST_DWithin(entregas.localizacao, ponto, 300)         -- dentro do raio
ST_DWithin(entregas.localizacao, ponto, 350)         -- dentro da margem de saída
```

`geography` mede sobre o elipsoide WGS 84, em metros — sem conversão de graus nem cálculo
geodésico em C# (CLAUDE.md, seção 16). Os candidatos são as entregas **em execução do motorista
responsável** (Em rota ou Próxima do destino) com coordenada de destino, sempre filtrados pela
organização na condição da consulta.

### Histerese

| Distância | Estando fora | Estando dentro |
|---|---|---|
| ≤ 300 m | entrada | continua dentro |
| 300 m a 350 m | continua fora | continua dentro |
| > 350 m | continua fora | saída |

Sem a margem, um GPS oscilando na borda — 298 m, 303 m, 299 m — geraria entrada, saída e entrada.
Raio e margem são constantes da política (`PoliticaDeGeofence`), registradas em cada estado e em
cada evento, para que decisões antigas continuem explicáveis se os números mudarem.

### Estado por entrega e eventos próprios

`estados_de_geofence` (uma linha por entrega) guarda dentro/fora, distância, última captura e
sequência avaliadas e o número de entradas. `eventos_de_geofence` registra cada entrada e saída com
distância, raio e o evento de localização que decidiu — sem coordenada — e é somente-inserção por
trigger.

Entradas e saídas ficam em tabela própria, não na timeline da entrega: uma entrega com sinal ruim
pode entrar e sair várias vezes, e a timeline deve contar o que mudou na entrega.

### Transição da entrega

A **primeira** entrada de uma entrega em rota executa o comando novo `RegistrarProximidade` da
máquina de estados (`EmRota → ProximaDoDestino`), com evento `ProximidadeDetectada` na timeline,
sem autor humano. Reentradas registram evento de geofence, mas não geram nova transição.

Chegada manual e proximidade levam ao mesmo status. Quem vier primeiro transiciona; o outro é
repetição sem efeito (idempotência da Fase 5).

### Concorrência com o comando do motorista

Se o motorista registra chegada enquanto o lote de posições está sendo gravado, a versão da linha
da entrega recusa a gravação do lote. A ingestão refaz a transação inteira, até três vezes, com o
estado novo: nenhuma posição duplica (a transação anterior foi desfeita) e nenhuma transição se
repete (a entrega já está próxima do destino). O comando do motorista, se perder a corrida, recebe
`409 conflito_de_versao` e ao repetir encontra a transição já feita.

### Consulta

`GET /api/entregas/{id}/geofence` (Administrador, Supervisor, Operador) devolve se a geofence está
disponível (a entrega tem coordenada), raio, dentro/fora, distância e captura da última avaliação,
número de entradas e as transições em ordem.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Distância em C# (haversine) | CLAUDE.md, seção 16: cálculo geodésico é do PostGIS; e o teste precisa provar geografia real |
| Avaliar em worker assíncrono | Adicionaria fila e atraso sem necessidade na escala de referência; a ordem por motorista já é serializada na ingestão |
| Avaliar só a última posição do lote | Perde entrada e saída durante perda de sinal |
| Raio único sem histerese | Oscilação na borda gera entradas e saídas em sequência |
| Entradas e saídas na timeline da entrega | Poluiria a timeline com ruído de sinal |
| Geofence como `ST_Buffer` pré-calculado | Para círculo em `geography`, `ST_DWithin` é o mesmo teste e usa o índice sem guardar polígono |
| Raio configurável por organização ou cliente | Decisão de produto que ainda não existe; o raio já é gravado em cada estado e evento |

## Consequências

- ETA e SLA (Fase 9) podem usar a distância da última avaliação sem recalcular.
- Alertas (Fase 10) têm evidência pronta: distância, raio e o evento de localização da transição.
- O custo por posição é uma consulta PostGIS pelas entregas em execução do motorista (dezenas, não
  milhares); medir na Fase 22.
- Geofence de hub (saída e retorno ao depósito) segue o mesmo desenho quando for pedida.

## Como isto é verificado

- `EstadoDeGeofenceTestes` — uma entrada ao ficar dentro, borda da margem (350 m continua dentro,
  351 m sai), reentrada, posição antiga sem efeito, sequência desempatando mesma captura.
- `ProximidadeDaEntregaTestes` e a tabela da máquina de estados — `RegistrarProximidade` só de Em rota,
  idempotente, e chegada manual depois sem efeito.
- `GeofenceTestes` (PostgreSQL + PostGIS real, pontos gerados por `ST_Project` a distâncias exatas) —
  301 m fora e 299 m dentro, trinta posições dentro e duplicata com uma única entrada, saída com
  margem e reentrada, posição fora de ordem sem transição retroativa (envios separados e no mesmo
  lote), destinos no mesmo ponto em duas organizações, entrega concluída e sem coordenada, chegada
  manual simultânea à entrada com uma única transição, e eventos de geofence somente-inserção.
