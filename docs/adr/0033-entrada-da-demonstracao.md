# ADR 0033 — Entrada da demonstração: o servidor faz o login pelo visitante, com privilégio mínimo

**Status:** aceito — Fase 24
**Decisores:** time técnico
**Relacionados:** [0006](./0006-simulador-externo.md), [0009](./0009-autenticacao-e-sessao.md), [0010](./0010-multi-tenancy-e-isolamento.md), [0032](./0032-roteiro-da-demonstracao.md)

## Contexto

O projeto precisa ser explorável por alguém que chega sem credencial — um recrutador, um avaliador — e o
ROADMAP proíbe onboarding comercial: é um botão, não um cadastro.

Isso é um pedido incomum: abrir uma porta autenticada para o público. Publicar usuário e senha no README
resolveria e seria a pior forma possível — credencial em repositório, indexada, válida para sempre.

Há ainda o segundo pedido da fase: pausar, retomar e reiniciar a simulação.

## Decisão

### O visitante pede sessão; o servidor faz o login por ele

`POST /api/demonstracao/sessao` autentica com credenciais que só o servidor conhece e devolve **uma sessão
comum** — mesmo token curto, mesmo cookie de renovação rotativo, mesmas regras de expiração.

Nada de caminho paralelo de autenticação: se a demonstração tivesse a própria noção de sessão, ela seria
uma segunda implementação de segurança, com metade dos testes e nenhum dos olhos em cima.

### Desligada por padrão, e ausente quando desligada

`Torre:Demonstracao:Habilitada` começa `false`. Desligada, `POST .../sessao` responde **404**, não 403:
"existe mas você não pode" já é informação sobre o sistema.

`GET /api/demonstracao` diz apenas se há demonstração, e quando não há **não conta qual organização
seria usada**. É o que o console precisa para decidir se mostra o botão — e quem decide é a API, não uma
variável de build: o pacote publicado é o mesmo em todo lugar, e um botão ligado por configuração de
compilação apareceria também onde a porta não existe.

### Conta administrativa é recusada

Se a conta configurada for `Administrador`, o servidor **recusa abrir a sessão** e registra o motivo.

Uma conta administrativa aberta ao público daria a qualquer visitante o poder de criar credencial de
integração e revogar webhook. Recusar transforma um erro de configuração em indisponibilidade — o modo de
falhar que não custa caro.

O perfil sugerido é `Operador`: vê a operação inteira, opera entregas, e não alcança a administração da
organização. Privilégio mínimo aqui não é recomendação escrita em documento: é o que a sessão recusa
quando alguém tenta.

### A porta exige origem conhecida, como o login humano

O mesmo filtro de origem confiável do login. Não porque o visitante seja suspeito, mas porque abrir uma
exceção criaria um caminho de autenticação sem a defesa — e caminhos assim sobrevivem ao motivo que os
criou.

### Pausar, retomar e reiniciar: **não implementado**

O ROADMAP diz "pode haver". Não há.

O simulador é um processo externo por decisão ([ADR 0006](./0006-simulador-externo.md)). Para a API
pausá-lo, ela precisaria de um canal de controle sobre ele — uma fila de comandos que o simulador
consultasse —, e isso inverteria a dependência que a ADR 0006 estabeleceu: o simulador é um cliente da
API, não um subordinado dela.

O que existe no lugar: **encenar de novo**. Cada execução do simulador monta o próprio palco, e a mesma
semente conta a mesma história. A consequência — acúmulo de encenações no ambiente — está registrada como
pendência, e a saída provável é desativar a organização antiga, não apagar linha, porque as tabelas
centrais são somente-inserção por desenho.

## Consequências

Quem hospeda a demonstração precisa manter uma senha no ambiente. Ela nunca aparece no repositório, no
pacote publicado nem na resposta da API — mas existe, e rotacioná-la é operação normal.

A sessão de demonstração dura o mesmo que a de um operador: doze horas. Um visitante que voltar depois
disso clica no botão de novo, o que é aceitável para o propósito.

Não há limite de visitantes simultâneos além do limite de requisições do login. Todos entram na **mesma
conta**, então as ações de um aparecem para os outros — o que, numa demonstração de operação em tempo
real, é mais recurso do que problema.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Publicar usuário e senha no README | credencial em repositório, indexada e válida para sempre |
| Criar uma conta nova por visitante | lixo acumulado e uma fábrica de contas exposta ao público |
| Sessão própria da demonstração, sem passar pelo login | segunda implementação de segurança, com metade dos testes |
| Botão ligado por variável de build | apareceria onde a porta não existe; a API é quem sabe |
| Responder 403 quando desligada | confirma que o recurso existe |
| Conta administrativa para "mostrar tudo" | daria ao público o poder de criar credencial e revogar webhook |
| API controlando o simulador (pausar/retomar) | inverteria a dependência da ADR 0006 |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| O visitante entra sem credencial | `VisitanteEntraSemCredencialERecebeSessaoDeOperador` |
| E recebe uma sessão comum | o mesmo teste: cookie de renovação presente, token funcional |
| Privilégio mínimo é real | o mesmo teste: a sessão abre entregas e recebe 403 em integrações e criação de conta |
| Desligada, a porta não existe | `SemDemonstracaoNaoHaBotaoNemPorta`: 404 e convite nulo |
| Conta administrativa não abre | `ContaAdministrativaNaoAbreDemonstracao`: 404, sem cookie, com o motivo no log |
| A porta exige origem conhecida | `SessaoDeDemonstracaoExigeOrigemConhecida` |
| O botão só aparece quando há demonstração | testes do console: sem oferta, sem botão |
| Falha vira mensagem, não tela quebrada | teste do console: alerta visível e login ainda utilizável |
