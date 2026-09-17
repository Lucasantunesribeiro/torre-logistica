# ADR 0024 — Rastreamento público: link com token forte, posição grossa e resposta neutra

**Status:** aceito — Fase 15
**Decisores:** time técnico
**Relacionados:** [0007](./0007-storage-de-comprovantes-fora-do-banco.md), [0009](./0009-autenticacao-e-sessao.md), [0010](./0010-multi-tenancy-e-isolamento.md), [0015](./0015-ingestao-de-localizacao.md), [0018](./0018-previsao-de-chegada-e-sla.md), [0023](./0023-prova-de-entrega.md)

## Contexto

O destinatário da entrega não tem conta, não pertence à organização e não pode ver o console. Ainda
assim precisa saber onde está a encomenda dele. Isso cria a única superfície do sistema que responde a
quem não foi autenticado por ninguém — e que, se malfeita, vira duas coisas ao mesmo tempo: um oráculo
para descobrir entregas alheias e um rastreador de pessoa, já que a posição mostrada é a de um motorista
trabalhando, não a de um pacote.

## Decisão

### O link é um token forte, guardado apenas como hash

O valor vai no link e nunca é persistido: a tabela `tokens_de_rastreamento` guarda o SHA-256 dele, pelo
mesmo mecanismo dos tokens de renovação de sessão (`SegredosDeRenovacao`, 32 bytes de entropia em
Base64Url). Um vazamento do banco não devolve nenhum link funcionando.

A consequência prática é deliberada: **link perdido não é recuperável, é reemitido**. Emitir de novo
revoga o anterior no mesmo commit, e um índice único parcial (`revogado_em IS NULL`) garante no banco que
só existe um link ativo por entrega — de modo que um link repassado adiante para de funcionar assim que o
operador emite outro.

A emissão é do console (`POST /api/entregas/{id}/link-de-rastreamento`, política `entregas:operacao`) e
fica na trilha de auditoria. O que se audita é o ato, nunca o valor: registrar o token na auditoria
desfaria o motivo de só guardarmos o hash.

### Um único erro para tudo que não abre

Token malformado, desconhecido, expirado ou revogado terminam todos no mesmo
`rastreamento_nao_encontrado`, com a mesma mensagem e o mesmo status. Nada é consultado antes do acerto do
hash — nem a entrega, nem a organização. Distinguir "expirado" de "nunca existiu" seria confirmar ao
atacante que aquele link já foi de alguém.

Antes de tocar no banco, o que não tem forma de token (43 caracteres Base64Url) é descartado, e a rota tem
limite de requisições **por endereço** (`limite-rastreamento-publico`): a superfície não tem conta para
particionar, e é justamente o endereço que precisa ser contido em tentativa em massa.

### A autoridade é o token, nunca a requisição

A consulta pública não tem sessão, então o filtro global de tenant não teria o que filtrar — e, por
desenho, ele falha fechado (ADR 0010). Aqui ele é ignorado de propósito, com `IgnoreQueryFilters()`, e
substituído por comparação explícita com a organização **do token** em cada consulta: entrega, timeline,
previsão, posição e comprovante. Nenhum identificador de organização vindo da requisição é aceito.

### A posição é deliberadamente grossa

Três defesas combinadas, em `PoliticaDeRastreamentoPublico`:

| Defesa | Valor | Por quê |
|---|---|---|
| Só a caminho | `EmRota` e `ProximaDoDestino` | fora desses estados, a posição do motorista não diz nada sobre a encomenda e só expõe a pessoa |
| Grade de arredondamento | 0,01° (~1,1 km no eixo norte-sul) | apaga a rua e preserva a região: dá para dizer "está chegando no bairro", não "está na esquina" |
| Idade máxima | 15 minutos | posição velha faz o destinatário decidir errado, e sustenta uma vigilância que já não informa nada |

A página declara a grossura (`precisaoAproximadaEmMetros`) com o valor do eixo norte-sul, o maior dos
dois — anunciar precisão melhor que a real seria a única versão perigosa deste arredondamento.

### A página mostra a encomenda, não a operação

Aparecem: código, status, janela prometida, chegada prevista, situação do SLA, bairro/cidade/UF do
destino, os marcos públicos da timeline e, depois da conclusão, a prova de entrega.

Não aparecem: motorista, veículo, rota, outras entregas, telefone, identificador interno, logradouro e
número, autor dos eventos e os dados em JSON de cada evento. Planejamento, atribuição, troca de motorista
e alteração de cadastro são decisões internas — contá-las revelaria como a empresa se organiza sem dizer
nada ao destinatário sobre a encomenda.

O comprovante sai sempre por URL assinada de curta duração (ADR 0023), nunca por link permanente, e pode
ser desligado por configuração (`Torre:RastreamentoPublico:ExporComprovante`) em operações que fotografam
o interior da casa ou coletam assinatura manuscrita.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Código curto do tipo `ENT-2026-004821` como chave pública | previsível e enumerável: o código humano existe para conversa operacional, não para autorizar acesso |
| Guardar o token em claro para permitir reenviar o mesmo link | transformaria a tabela em um cofre de credenciais válidas; reemitir custa um clique |
| Posição exata em tempo real | é vigilância de uma pessoa a pretexto de informar sobre um pacote; nenhum ganho para o destinatário justifica |
| Página pública sem limite de requisições | o endereço vira oráculo de força bruta sobre tokens |
| Distinguir "expirado" de "inexistente" para ser gentil | a gentileza confirma a existência do link a quem não deveria saber |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| Só o hash é persistido | `TokenDeRastreamentoTestes` e consulta ao banco em integração: nenhuma coluna guarda o valor emitido |
| Reemitir revoga o anterior | teste de integração: o link antigo para de abrir e o índice único parcial só aceita um ativo |
| Resposta neutra | token malformado, inexistente, expirado e revogado devolvem resposta byte a byte idêntica |
| Isolamento entre organizações | token de uma organização não alcança entrega de outra |
| Posição grossa e só a caminho | testes de unidade da política e de integração com posição real |
| Sem dado interno na resposta | teste que varre o corpo procurando motorista, rota, logradouro e identificadores |
| Limite por endereço | teste de integração estourando a janela |
