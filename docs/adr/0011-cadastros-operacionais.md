# ADR 0011 — Cadastros operacionais: tipos de valor próprios, PostGIS atrás de conversão, versão por linha e inativação no lugar de exclusão

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 2

## Contexto

A Fase 2 cria os cadastros que a operação usa: motorista, veículo, hub, cliente e destinatário.
Quatro perguntas de desenho aparecem em todos eles e precisam da mesma resposta nos cinco:

1. Como representar coordenada sem o domínio depender de biblioteca geoespacial (CLAUDE.md, seção 7)?
2. Até onde validar endereço, telefone, placa e CNPJ?
3. Como impedir que duas pessoas editando o mesmo cadastro se sobrescrevam em silêncio (seção 28)?
4. O que fazer com cadastro que sai de uso, se a partir da Fase 3 entregas passam a apontar para ele?

## Decisão

### Coordenada: tipo próprio no domínio, `geography(Point,4326)` no banco

O domínio usa `CoordenadaGeografica` (latitude, longitude, WGS 84). A Infrastructure converte para
o `Point` do NetTopologySuite e grava em `geography(Point,4326)`, com índice GiST. O pacote
`Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` fica só na Infrastructure — e um teste de
arquitetura reprova NetTopologySuite no domínio.

A conversão tem uma armadilha registrada no código: `Point` recebe **(longitude, latitude)**. Um
teste de integração lê `ST_X` e `ST_Y` direto do banco para provar a ordem.

`(0, 0)` é recusado: é o valor que aparece quando latitude e longitude ausentes viram zero numa
integração, e é um ponto no oceano. Meia coordenada também é recusada.

### Tipos de valor com normalização

| Tipo | Regra |
|---|---|
| `Endereco` | Brasil: CEP de 8 dígitos (não zerado), UF entre as 27, logradouro, número, bairro e cidade obrigatórios |
| `Telefone` | E.164; número sem código de país com 10 ou 11 dígitos é tratado como brasileiro, com DDD e celular validados |
| `PlacaDeVeiculo` | padrão antigo `ABC1234` ou Mercosul `ABC1D23`, sem hífen, maiúsculas |
| `Cnpj` | numérico **e alfanumérico** (formato da Receita desde julho de 2026), com dígitos verificadores |
| `TextoNormalizado` | nome aparado, sem caractere de controle; forma de busca sem acento e sem caixa |

A normalização alimenta a unicidade: `abc-1234` e `ABC1234` são a mesma placa; "Hub São José" e
"hub sao jose" são o mesmo hub.

Endereço é **só brasileiro** na v1. Endereço de outro país exigirá outro tipo, não um afrouxamento
deste — validação que aceita qualquer coisa não valida nada.

### Unicidade por organização

| Cadastro | Único dentro da organização |
|---|---|
| Veículo | placa |
| Hub | nome normalizado |
| Cliente | CNPJ, quando informado (índice parcial) |
| Motorista | conta de acesso associada (índice parcial; uma conta, um motorista) |

A checagem prévia dá a mensagem; o índice do banco decide a corrida. As violações são traduzidas
em `409` pelo nome da restrição — nunca `500`. Sendo por organização, conflito nunca revela
cadastro de outro tenant (mesmo raciocínio do ADR 0010).

### Concorrência otimista com `xmin`

Cada cadastro expõe `versao`, mapeada para a coluna de sistema `xmin` do PostgreSQL — muda a cada
gravação da linha, sem coluna extra. Alteração de dados exige a `versao` lida:

- versão diferente da atual → `409 conflito_de_versao` antes de aplicar qualquer coisa;
- mesma versão lida por duas requisições → a gravação usa a versão como condição, e a segunda
  recebe `409` ao gravar.

Ativar, inativar, associar e desassociar conta **não** exigem versão: são comandos de estado
idempotentes, e o resultado é exatamente o que quem chamou pediu — não há dado a perder.

### Inativação no lugar de exclusão

Não existe `DELETE` nos cadastros. Inativar retira de uso; as regras "motorista inativo não recebe
nova atribuição" e "veículo inativo não inicia nova rota" ficam no domínio e passam a ser aplicadas
na Fase 4. Apagar um cadastro que entregas referenciam reescreveria o passado delas.

Remoção de dado pessoal de destinatário por retenção (LGPD) é assunto da Fase 20 e será
anonimização, não exclusão da linha.

### Motorista ≠ conta

`Motorista` é o recurso operacional; a conta (`Usuario` com perfil Motorista) é quem autentica na
PWA. A associação é explícita, só com conta ativa de perfil Motorista da mesma organização. Trocar
a conta exige desassociar antes: troca direta transferiria, numa chamada, a identidade de quem
executa as entregas daquele motorista.

### Dado pessoal fora da auditoria e do log

A trilha registra **quais campos** mudaram, nunca os valores. A trilha é somente-inserção: um
telefone gravado ali não poderia mais ser apagado. Pelo mesmo motivo, log de cadastro traz só
identificadores.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| `Point` do NetTopologySuite no domínio | Acopla o domínio a biblioteca geoespacial; o CLAUDE.md só admite com justificativa, e a conversão resolve sem custo |
| Latitude e longitude em duas colunas `double` | Descarta o índice espacial e obriga a montar o ponto em toda consulta da Fase 7 |
| Coluna `versao` incrementada pela aplicação | Duplica o que `xmin` já faz e pode ser esquecida numa gravação |
| Last-write-wins nas alterações | Proibido pela seção 28 |
| Exclusão física com checagem de referência | Funciona hoje, quebra na Fase 3; e a checagem vira corrida |
| Validação permissiva de endereço internacional | Aceitar tudo tornaria CEP e UF sem significado para o geofence |

## Consequências

- Toda consulta geoespacial da Fase 7 pode usar a coluna `geography` e o índice GiST já existentes.
- Cliente da API precisa guardar a `versao` lida para alterar. Recarregar e reaplicar é o fluxo em
  conflito — o console da Fase 18 terá de tratar isso.
- CNPJ alfanumérico funciona desde o primeiro cadastro, sem migração futura.
- Operação fora do Brasil exige ADR novo para endereço e telefone.

## Como isto é verificado

- `DependenciasEntreCamadasTestes` — NetTopologySuite proibido no domínio.
- Testes de unidade de `Endereco`, `Telefone`, `PlacaDeVeiculo`, `Cnpj` (incluindo o exemplo
  alfanumérico da Receita, `12.ABC.345/01DE-35`), `CoordenadaGeografica` e das regras de inativo.
- `CadastrosOperacionaisTestes` — ciclo completo com versão e auditoria nos cinco cadastros,
  cross-tenant nos cinco, unicidade, 422 por regra, 400 por formato, seis alterações simultâneas
  com a mesma versão e um único vencedor, busca sem acento, auditoria sem dado pessoal.
- `PreparacaoDaOperacaoTestes` — critério de aceite do supervisor, regras de conta do motorista,
  associações simultâneas da mesma conta, e `GeometryType`, `ST_SRID`, `ST_X`, `ST_Y`,
  `ST_Distance` e índice GiST lidos direto do PostGIS.
