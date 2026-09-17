# ADR 0025 — API de integração: credencial de máquina, versão no caminho e idempotência em duas camadas

**Status:** aceito — Fase 16
**Decisores:** time técnico
**Relacionados:** [0009](./0009-autenticacao-e-sessao.md), [0010](./0010-multi-tenancy-e-isolamento.md), [0012](./0012-entrega-como-agregado-central.md), [0021](./0021-operacao-offline.md), [0024](./0024-rastreamento-publico.md)

## Contexto

Um ERP precisa criar entregas sem abrir o console. Isso traz três problemas que a autenticação humana não
resolve: a credencial não pertence a ninguém, o cliente vai repetir requisições (timeout, retentativa
automática, reprocessamento de fila) e o contrato passa a ser público para terceiros, que não atualizam
junto com o nosso frontend.

## Decisão

### Credencial de máquina, em esquema próprio

A integração autentica por chave de API em `Authorization: Bearer`, num esquema de autenticação separado
dos dois existentes. Não há sessão, renovação, perfil nem canal: a credencial vale até ser revogada.

O principal produzido carrega a organização — que é o que o filtro de tenant precisa — e o identificador
da integração, e **nenhuma reivindicação de usuário**. Assim, o que a integração faz nasce com autor vazio
na timeline ("foi o sistema") enquanto a auditoria guarda qual credencial agiu. Foi por isso que
`IContextoDoUsuario` ganhou `AutorUsuarioId` opcional: inventar um usuário de serviço para preencher o
campo criaria uma conta fantasma que aparece como pessoa em toda a trilha.

Só o **administrador** emite e revoga credencial: é decisão de segurança, da mesma natureza que criar ou
desativar conta.

### A chave tem parte pública e parte secreta

Formato `tlog_<identificador>_<segredo>`, com 12 e 32 bytes sorteados. O identificador acha a linha; o
segredo é conferido contra o SHA-256 guardado, em tempo constante. Sem a parte pública, conferir uma chave
exigiria varrer a tabela calculando hash — e essa varredura seria, ela mesma, o ataque.

O segredo é guardado como **SHA-256, não com o hasher de senha**. Derivação lenta existe para senha humana,
que é curta e adivinhável; contra 256 bits sorteados não há dicionário, e o custo lento puniria o
integrador legítimo, que apresenta a chave a cada requisição.

O prefixo fixo é deliberado: varredura de segredos em repositório, log e histórico consegue casar `tlog_`
e apontar o vazamento.

### Versão no caminho

`/api/integracoes/v1/…`. O console pode mudar junto com o frontend na mesma entrega; o ERP de um cliente
não. A versão no caminho torna a quebra de contrato uma decisão explícita, não um efeito colateral.

### Idempotência em duas camadas, com propósitos diferentes

| Guarda | Contra o quê | Como |
|---|---|---|
| `Idempotency-Key` + hash do corpo | reenvio do mesmo pedido: resposta perdida, timeout, retentativa | única por `organização + integração + chave`; mesma chave com corpo diferente é **409**, porque é defeito do cliente e responder o resultado antigo o esconderia |
| Identificador de origem | o mesmo pedido chegando com chaves diferentes, típico de fila reprocessada | única por `organização + integração + identificador`; reenvio devolve a entrega que já existe, com **200** em vez de 201 |

O hash é calculado sobre a **re-serialização canônica do contrato**, não sobre os bytes crus: reenvio com
espaçamento ou ordem de campos diferente é o mesmo pedido, e tratá-lo como outro criaria duplicata.

Os dois registros entram **no mesmo commit** da entrega. Como `CriarAsync` abre a própria transação — e
aninhar outra é recusado pelo provedor, que precisa reexecutar a unidade inteira em falha transitória —, a
criação ganhou um gancho executado dentro dessa transação. Entrega criada sem registro de idempotência
seria duplicada no reenvio seguinte.

### CSV com prévia e sem dependência nova

O leitor é escrito à mão: o formato aceito é definido por nós (cabeçalho fixo, sem campos multilinha), e o
que precisa estar certo — vírgula dentro de aspas, aspas escapadas, BOM de planilha do Windows — cabe em
poucas linhas cobertas por teste. Uma biblioteca custaria revisão de segurança e manutenção sem resolver
nada que já não esteja resolvido (`CLAUDE.md`, seção 71).

O fluxo tem duas etapas: a **prévia** confere o arquivo inteiro sem gravar nada; a **confirmação** recusa o
lote antes de começar se houver um só erro de formato, inclusive identificador repetido dentro do próprio
arquivo. Erro de regra que só aparece na gravação — cliente inexistente, janela no passado — é relatado
linha a linha, e as demais seguem.

Cada linha entra com chave derivada do **hash do arquivo** e do número da linha. Reenviar o mesmo arquivo é
seguro por construção, inclusive depois de uma queda no meio da importação: cada linha reencontra a própria
chave e o identificador de origem.

### Limite por credencial

`limite-integracao` particiona por credencial, não por endereço: dois ERPs atrás do mesmo IP de datacenter
não disputam a mesma janela, e um integrador afobado é contido sem atingir os outros.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| JWT para integração | exigiria endpoint de emissão e renovação para um cliente que não tem sessão; a chave estática revogável resolve com menos peça |
| Usuário de serviço para preencher o autor | cria conta fantasma que aparece como pessoa em toda a trilha de auditoria |
| Guardar o segredo com PBKDF2 | custo lento por requisição, sem ganho contra 256 bits sorteados |
| Versão por cabeçalho ou query | invisível no log e fácil de esquecer; o caminho deixa a versão explícita em qualquer ferramenta |
| Só `Idempotency-Key`, sem identificador de origem | não cobre a fila reprocessada, que reenvia o mesmo pedido com chave nova |
| Importação tudo-ou-nada num commit | exigiria refatorar a criação de entrega inteira; a prévia obrigatória mais a idempotência por linha dão a mesma garantia prática |
| Biblioteca de CSV | dependência nova para um formato que nós mesmos definimos |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| Credencial separada da identidade humana | token de console e de motorista não valem na API v1; chave de integração não abre console nem PWA |
| Só o hash é persistido | teste confere o hash contra o SHA-256 do segredo emitido e procura a chave na linha inteira |
| Replay não duplica | mesma chave reenviada devolve a mesma entrega; contagem no banco continua 1 |
| Payload conflitante | mesma chave com corpo diferente responde 409 |
| Fila reprocessada | mesmo identificador de origem com chave nova devolve 200 e não cria outra entrega |
| Credencial revogada | 401 idêntico ao de chave inexistente |
| Tenant | credencial de uma organização não enxerga nem cria nada em outra |
| Rate limiting | janela por credencial, com 429 |
| Importação | prévia acusa cabeçalho, coluna, tipo e identificador repetido; lote com erro não grava nada; reenvio do arquivo não duplica |
