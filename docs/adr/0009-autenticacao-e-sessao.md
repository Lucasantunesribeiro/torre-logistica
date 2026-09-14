# ADR 0009 — Autenticação: token de acesso curto, renovação rotativa em cookie e sessão conferida a cada requisição

- **Data:** 2026-09-14
- **Status:** aceita
- **Fase:** 1

## Contexto

O CLAUDE.md (seção 37) exige para usuários administrativos: token de acesso curto, refresh
token rotativo, detecção de reuso e revogação de família. A seção 14 exige que o motorista seja
uma **autoridade separada**, cuja sessão não dê acesso ao console.

Topologia a atender: três aplicações web em origens próprias (console, PWA, rastreamento)
chamando a API em outra origem, todas sob o mesmo domínio registrável — `localhost` em
desenvolvimento; subdomínios de um mesmo domínio em produção (a confirmar na Fase 25).

A PWA do motorista roda em rede móvel instável: resposta perdida no meio de uma renovação é
cenário normal, não exceção.

## Decisão

### Dois tokens, dois lugares

| | Token de acesso | Token de renovação |
|---|---|---|
| Formato | JWT, HMAC-SHA256 | 32 bytes aleatórios, Base64Url, opaco |
| Validade | 15 min (configurável) | até o fim da sessão |
| Onde fica no cliente | memória da aplicação | cookie `HttpOnly; Secure; SameSite=Strict; Path=<prefixo do canal>` |
| Onde fica no servidor | em lugar nenhum | só o **hash SHA-256** |
| Trafega em | cabeçalho `Authorization` | cookie, só nas rotas de autenticação do canal |

Um XSS no console rouba no máximo o token de acesso, que morre em minutos. O token de renovação
não é legível por JavaScript e não está no banco em forma utilizável.

### A sessão é conferida em toda requisição

Assinatura e validade corretas não bastam. O token carrega `sid`, e cada requisição autenticada
confere no banco que a sessão continua aberta, dentro do prazo, com conta e organização ativas.

É o que faz **logout, desativação de conta, troca de perfil e detecção de reuso valerem na hora**,
em vez de só quando o token de acesso expirar. O custo é uma consulta por chave primária por
requisição. Se deixar de ser desprezível, o passo seguinte é cache curto invalidado na revogação —
nunca abrir mão da conferência.

### Rotação com detecção de reuso e janela de tolerância

Cada renovação consome o token apresentado e emite um sucessor na mesma sessão ("família").
A decisão é uma função pura, `PoliticaDeRenovacao.Avaliar`:

| Token apresentado | Decisão |
|---|---|
| disponível | rotacionar |
| já trocado, há ≤ 20 s, com o sucessor ainda intacto | rotacionar de novo, descartando o sucessor não usado |
| já trocado fora dessa condição | **reuso: revogar a sessão inteira** |
| descartado sem uso | **reuso: revogar a sessão inteira** |
| sessão encerrada, expirada ou de outro canal; token vencido | recusar sem revogar |

A janela de tolerância existe porque retry é esperado. Ela não elimina a detecção, só a adia um
passo: se um invasor usar a janela, o sucessor do usuário legítimo é descartado, e a próxima
renovação dele é que derruba a família. Zerar a janela por configuração transforma qualquer
reapresentação em reuso.

A linha da sessão fica travada (`SELECT … FOR UPDATE`) durante toda a decisão. Sem isso, duas
renovações simultâneas do mesmo token enxergariam ambas o token "disponível".

### Um esquema de autenticação por canal

`Operacao` e `Motorista` são esquemas JWT separados, cada um exigindo sua própria audiência
(`aud`). Um token da PWA apresentado ao console **falha na autenticação** — 401, não 403: para o
console aquilo não é credencial. Cada canal tem login, cookie e `Path` próprios; o navegador nem
envia o cookie do console para as rotas do motorista. Uma conta de motorista não pode ser
promovida a perfil do console, nem o contrário.

A política de *fallback* exige sessão do console: endpoint novo esquecido sem anotação fica
fechado, não aberto.

### Validação do JWT

Emissor, audiência, assinatura, validade e algoritmo são todos exigidos. Só `HS256` é aceito,
o que fecha as trocas de algoritmo (`alg: none`, ou algoritmo assimétrico usando a chave simétrica
como pública). A validade é conferida contra o mesmo `TimeProvider` que emitiu o token, com
tolerância de 30 s.

HMAC com chave única basta porque emissor e validador são o mesmo processo (ADR 0001). A chave
vem de configuração e é obrigatória fora de Development/Testing; nesses dois ambientes, sem chave,
gera-se uma efêmera na subida.

### Proteções do login

| Ameaça | Proteção |
|---|---|
| Enumeração de contas pela resposta | mesmo 401 (status, código, título, detalhe) para organização inexistente, e-mail inexistente, senha errada, conta bloqueada, conta inativa e canal errado |
| Enumeração pelo tempo | hash de senha calculado em todos os caminhos, inclusive contra um hash fictício |
| Força bruta numa conta | 5 falhas seguidas → bloqueio de 15 min, contador incrementado num único `UPDATE` atômico |
| Força bruta em volume | limite de 10 logins/min por endereço de origem, aplicado **antes** de qualquer hash |
| Senha gigante como negação de serviço | teto de 128 caracteres |
| CSRF / login CSRF | cookie `SameSite=Strict` **e** exigência de `Origin` presente e na lista do ambiente em login, renovação e logout |
| Hash corrompido no banco | conferência recusada sem lançar exceção; o 500 denunciaria que a conta existe |

Senhas usam PBKDF2-HMAC-SHA512 com 210.000 iterações, pela implementação do ASP.NET Core
Identity; hash com parâmetros antigos é recalculado no login seguinte.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Os dois tokens em `localStorage` | Um XSS leva a sessão inteira, indefinidamente |
| Só cookie de sessão, sem JWT | Funciona, mas obrigaria o PWA a carregar cookie em toda chamada — e toda chamada viraria alvo de CSRF |
| JWT sem conferência de sessão | Logout e revogação só teriam efeito após a expiração do token |
| Rotação sem janela de tolerância | Toda resposta perdida em rede móvel derrubaria o motorista |
| Um esquema único com claim de canal | Separação dependeria só de política de autorização; com esquemas distintos, a audiência errada nem autentica |
| Unicidade global de e-mail | Ver [ADR 0010](./0010-multi-tenancy-e-isolamento.md): o conflito revelaria contas de outra organização |
| Bloqueio permanente após N falhas | Qualquer um bloquearia qualquer conta para sempre |

## Consequências

- Logout e revogação são imediatos; o preço é uma consulta por requisição autenticada.
- Duas abas do mesmo navegador renovando ao mesmo tempo com o mesmo token disputam a cadeia.
  O cliente precisa serializar a renovação (uma só em curso por vez) — o console faz isso.
- O limite por endereço vê o endereço do proxy quando a API estiver atrás de um. Tratar os
  cabeçalhos encaminhados, com lista de proxies confiáveis, é requisito da Fase 25.
- O bloqueio temporário permite a um atacante atrasar o login de uma vítima por 15 minutos
  conhecendo organização e e-mail. Aceito na v1, documentado; a alternativa (não bloquear) é pior.
- Cookie `SameSite=Strict` pressupõe console e API no mesmo domínio registrável. Se a Fase 25
  escolher domínios diferentes, esta decisão precisa ser revista — não afrouxada em silêncio.

## Como isto é verificado

Testes de unidade cobrem cada ramo de `PoliticaDeRenovacao`. Testes de integração, contra
PostgreSQL real:

- `AutenticacaoTestes` — atributos do cookie, token fora do corpo, falhas indistinguíveis,
  token adulterado, assinado com outra chave e sem assinatura, exigência de `Origin`;
- `RenovacaoTestes` — rotação, retry na janela, sucessor descartado denunciando reuso,
  10 renovações simultâneas deixando exatamente um token disponível, logout imediato;
- `ReusoDeTokenTestes` — reuso revogando a família e gravando auditoria;
- `TempoDeSessaoTestes` — com relógio controlado: expiração do token, prazo absoluto da sessão,
  bloqueio que libera sozinho;
- `LimiteDeRequisicoesTestes` — 429 antes de conferir a senha;
- `LogsSemSegredoTestes` — nenhum token, senha ou e-mail no log, com prova de que o log foi
  capturado.
