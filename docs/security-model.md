# Modelo de segurança e privacidade — Torre Logística

> Documento de referência da Fase 20. Descreve o que o sistema protege, de quem, com quê, e **onde cada
> afirmação é verificada**. Afirmação sem prova aqui é aspiração, não controle.

Escopo: a API, as três aplicações web e o banco. Infraestrutura de hospedagem (TLS, WAF, rede) é da
Fase 25 e está marcada como pendência onde afeta uma decisão.

---

## 1. O que está em jogo

A Torre Logística guarda três coisas que valem ataque:

| Ativo | Por que importa |
|---|---|
| **Localização de pessoas** | o rastro de um motorista é o deslocamento de uma pessoa real, minuto a minuto |
| **Comprovantes de entrega** | foto e nome de quem recebeu, no endereço de quem recebeu |
| **Operação de terceiros** | cada organização vê a própria operação e nada além dela |

Os adversários considerados: um usuário autenticado de **outra** organização; alguém que descobriu um link
público; um integrador com credencial de máquina; um assinante de webhook malicioso; e quem consegue ler
logs ou backups.

Fora do modelo: comprometimento do servidor, do banco ou da máquina do desenvolvedor.

---

## 2. Autoridades

Três autoridades separadas, que **não se convertem uma na outra**:

| Autoridade | Como se prova | O que abre |
|---|---|---|
| Usuário do console | sessão humana, canal `Operacao` | console, conforme o perfil |
| Motorista | sessão humana, canal `Motorista` | apenas a própria rota e as próprias entregas |
| Integração | chave de máquina, `Authorization: Bearer` | apenas `/api/integracoes/v1/*` |

A sessão do motorista não abre o console, e a credencial de integração não abre nenhum dos dois. O token
de integração não carrega `sub`: não existe "usuário" por trás dele, e fingir que existe abriria caminho
para reaproveitar a credencial num endpoint humano.

**Perfis do console:** `Administrador`, `Supervisor`, `Operador`. A tabela completa de quem acessa o quê
está em [`matriz-de-autorizacao.md`](./seguranca/matriz-de-autorizacao.md).

> **Verificação:** `AutorizacaoTestes` percorre a matriz perfil × rota contra o roteamento **real** e
> reprova quando uma rota nova sobe sem política declarada. Esconder botão no frontend não conta como
> controle — o teste chama a API direto.

---

## 3. Isolamento entre organizações

O tenant **nunca** vem do payload. Ele deriva da autoridade autenticada: sessão, identidade do motorista
ou credencial de integração. Um `organizacaoId` no corpo é ignorado como autoridade.

No banco, filtro global por discriminador em toda entidade de negócio, que **falha fechado**: sem tenant
resolvido, a consulta não devolve linha nenhuma em vez de devolver tudo.

Recurso de outra organização responde **404**, igual a inexistente. Um 403 diria "existe, mas não é seu" —
que é justamente o que o atacante queria saber.

> **Verificação:** `IsolamentoEntreTenantsTestes` (9 provas), incluindo o filtro global falhando fechado e
> a recusa do tenant informado no corpo. Cada fase acrescenta o próprio caso: indicadores, rastreamento
> público, webhooks e integrações têm o seu.

---

## 4. Sessão humana

| Parâmetro | Valor padrão |
|---|---|
| Token de acesso | 15 minutos, em memória do navegador |
| Sessão no console | 12 horas (prazo absoluto) |
| Sessão no aplicativo do motorista | 7 dias (prazo absoluto) |
| Renovação | cookie `HttpOnly`, rotativo a cada uso |
| Janela de tolerância da rotação | 20 segundos |
| Bloqueio por falha de senha | 5 falhas consecutivas → 15 minutos |

A renovação é rotativa com **detecção de reuso**: um token de renovação apresentado duas vezes fora da
janela de tolerância derruba a **família inteira** de tokens daquela sessão. Um token roubado só serve uma
vez, e o uso denuncia o roubo — o legítimo é expulso junto, de propósito, porque não dá para saber qual dos
dois é o ladrão.

A janela de tolerância existe porque reenvio legítimo acontece: a resposta se perde, o cliente repete.
Sem ela, toda conexão instável viraria logout.

Falha de login responde igual para conta inexistente, senha errada e conta desativada. O hash de senha é
calculado **mesmo quando a conta não existe** (`AutenticarUsuario`): sem isso, a resposta instantânea para
e-mail desconhecido contra os dezenas de milissegundos do hash real viraria um oráculo de enumeração de
contas. Essa parte é garantida por construção, não por teste — medir tempo em teste automatizado produz
prova instável, que passaria a falhar por carga da máquina.

> **Verificação:** `AutenticacaoTestes`, `RenovacaoTestes`, `TempoDeSessaoTestes` e
> `ProtecaoContraAbusoTestes`.

---

## 5. Borda HTTP

| Cabeçalho | Valor |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; sandbox` |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `geolocation=(), camera=(), microphone=(), payment=(), usb=()` |
| HSTS | ligado fora de desenvolvimento |

A CSP da API bloqueia tudo porque a API não serve página: se um dia uma resposta devolver HTML por engano,
o navegador não executa nada dele. A CSP das **aplicações web** depende de como forem servidas e é decisão
da Fase 25 — está registrada como pendência, não como item cumprido.

**CORS** por lista de origens explícita. Origem desconhecida não recebe liberação, nem no *preflight*.

**CSRF**: o cookie de renovação é `SameSite` restrito e o endpoint de renovação exige `Origin` conhecido.
A topologia final de deploy pode exigir `SameSite=None`; nesse caso a defesa por `Origin` é o que sustenta
a proteção, e por isso ela existe desde já em vez de depender só do atributo do cookie.

A resposta não anuncia servidor nem versão, e a documentação OpenAPI não é publicada fora de
desenvolvimento.

> **Verificação:** `SegurancaDeBordaTestes` (13 provas).

---

## 6. Limites de requisição

Limite por superfície, não um número genérico para tudo: telemetria legítima é intensa por natureza e
seria bloqueada por uma política pensada para login.

| Superfície | Padrão por minuto |
|---|---|
| Login | 10, por origem |
| Renovação | 30, por origem |
| Envio de posições | 60, por motorista autenticado |
| Sincronização offline | 30, por motorista autenticado |
| Rastreamento público | 30, por origem |
| API de integração | 120, por credencial |

O limite do rastreamento público é o que torna caro varrer tokens por tentativa — somado à entropia do
token, que é a defesa principal.

---

## 7. Localização: coleta, uso, retenção

### 7.1 Quando pode ser coletada

Só do motorista autenticado, só pelo aplicativo do motorista, **só em primeiro plano** e **só com rota em
andamento**. Não há coleta em segundo plano: a PWA não usa — e o navegador não oferece de forma
confiável — rastreamento com a tela bloqueada. Fora da jornada, o aplicativo não envia posição, e o
servidor não a associa a rota nenhuma.

### 7.2 Finalidade

Operar a entrega em curso: mostrar o veículo no mapa, avaliar geofence do destino, calcular previsão de
chegada e detectar motorista offline. Nenhuma outra finalidade está implementada, e o documento é o lugar
de dizer isso: dado coletado para operar não vira, depois, avaliação de desempenho individual sem uma
decisão nova e explícita.

### 7.3 Minimização

Uma posição carrega o mínimo para essas finalidades: coordenada, precisão, captura, recebimento, sequência
e identificador do evento. Não há bateria, rede, modelo de aparelho, sensor, nem identificador de
dispositivo.

### 7.4 Acesso

| Quem | O que vê |
|---|---|
| Console (`operacao:leitura`) | **posição atual** do motorista |
| Console (`operacao:gestao`) | **histórico**, por período limitado |
| Destinatário (link público) | região aproximada, nas condições da seção 8 |
| Integração | nada de localização |

Histórico é dado pessoal volumoso e fica atrás do perfil de gestão, com período máximo por consulta — não
existe "baixar tudo".

### 7.5 Retenção

| Dado | Prazo padrão | Configuração |
|---|---|---|
| Histórico bruto de posições | **30 dias** | `Torre:Retencao:PosicoesBrutas` |

A limpeza roda em segundo plano (`Torre:Retencao:Intervalo`, padrão 6 h), apaga em lotes
(`TamanhoDoLote`, padrão 5.000) com teto por rodada (`LotesPorRodada`, padrão 20) e publica a métrica
`retention.positions.deleted`.

Três decisões merecem registro:

- **O corte é pelo recebimento, não pela captura.** Captura é carimbo do aparelho, e aparelho é cliente:
  um relógio errado decidiria por nós quando o dado sai ou fica.
- **Prazo abaixo de 1 dia cai num piso.** Retenção zerada por erro de digitação apagaria a operação em
  curso; o piso transforma o engano em comportamento previsível.
- **Só o rastro vence.** A posição *atual* é projeção de estado — uma linha por motorista, não um caminho
  percorrido — e continua depois do expurgo, senão apagar o histórico cegaria a torre. Evento operacional
  derivado (entrada em geofence, chegada, conclusão) também fica: é o que explica a operação, e some junto
  com a entrega.

Particionamento temporal não está ativo e não é necessário no volume atual; nada no desenho o impede
depois — a limpeza por lote continua válida sobre tabela particionada.

> **Verificação:** `RetencaoTestes`, `RetencaoEmLotesTestes` e `RetencaoComPrazoInvalidoTestes`: o rastro
> vencido sai, o recente fica, a posição atual sobrevive ao expurgo, o lote é respeitado e o piso protege
> a operação do dia.

---

## 8. Rastreamento público

O link é a credencial: quem o tem, entra. Daí as quatro travas:

1. token com entropia alta, gerado por fonte criptográfica;
2. o banco guarda **apenas o SHA-256** do token — vazamento do banco não produz links válidos;
3. validade padrão de 30 dias, revogável, e reemissão revoga a anterior;
4. resposta **neutra** para token inexistente, expirado ou revogado: os três respondem igual, para a
   resposta não confirmar qual caso é.

O que a página pública mostra de localização:

- posição **arredondada numa grade de 0,01°** — cerca de 1,1 km de lado, o que preserva a região e apaga
  a rua;
- **só** quando a entrega está a caminho ou próxima do destino;
- **nunca** quando a captura envelheceu mais que 15 minutos, para não sugerir que o veículo parado
  continua onde estava.

A página não mostra nome do motorista, rota, outras entregas nem o endereço completo. O endpoint público
é anônimo e **não aceita tenant** de lugar nenhum.

> **Verificação:** `RastreamentoPublicoTestes`, incluindo a resposta neutra e a ausência de campo
> administrativo no corpo público.

---

## 9. Comprovantes

Arquivo não passa pela API nem mora no banco. O fluxo é: o cliente pede autorização, o backend assina uma
URL de curta duração, o cliente sobe direto no storage e o backend registra os metadados.

Controles:

- bucket/diretório **privado**, sem leitura pública;
- URL assinada de curta duração, para subir e para baixar;
- tipo aceito por lista fechada (`image/jpeg`, `image/png`, `image/webp`), com extensão derivada do tipo —
  nunca do nome enviado;
- teto de 5 MB por arquivo;
- chave do arquivo derivada pelo servidor, o que fecha travessia de caminho;
- vínculo obrigatório com a entrega e a organização.

A checagem de assinatura da URL responde **403 uniforme** para assinatura ausente, inválida ou expirada.
(Esse detalhe nasceu de um defeito real encontrado na Fase 14: a ausência de assinatura respondia 400 de
binding, e o código de status ensinava o contrato ao atacante.)

> **Verificação:** `ComprovantesTestes`, incluindo travessia de caminho em forma escapada e acesso
> cruzado entre organizações.

---

## 10. Integrações e webhooks

**Credencial de máquina** em duas partes (`prefixo.identificador.segredo`): o identificador encontra a
linha, o segredo é conferido por hash em tempo constante. A chave é mostrada **uma vez**, na criação, e
revogável. Idempotência em duas camadas (`Idempotency-Key` e referência externa da entrega) para que
retry do integrador não vire entrega duplicada.

**Webhooks de saída** são assinados com HMAC-SHA256, com segredo por assinatura, carimbo de tempo no
cabeçalho e tolerância de 5 minutos contra repetição. O segredo fica cifrado em repouso (AES-GCM).

**SSRF** é tratado no destino: antes de entregar, o endereço resolvido é conferido contra loopback, faixas
privadas, link-local, CGNAT, multicast e locais IPv6, e **redirecionamento não é seguido** — um assinante
que responde 302 para um endereço interno nos usaria como ponte para a rede de dentro.

> **Verificação:** `IntegracoesTestes` e `WebhooksTestes`, incluindo assinatura conferida por um
> assinante HTTP real subido pelo teste.

---

## 11. Segredos

Nenhum segredo é versionado. `.env.example` documenta apenas nomes de variável.

- Hook local bloqueia `git commit` ao encontrar padrão de segredo; falso positivo se libera por arquivo
  em `.varredura-permitido`.
- A CI varre o **histórico completo**, não só o diff, e reprova em qualquer achado.
- Chave de criptografia de webhook ausente derruba a subida fora de desenvolvimento: cada instância com
  chave própria não decifraria o que a outra gravou, e o erro apareceria como "segredo inválido" meses
  depois.
- Credencial exposta se resolve por **rotação**. Apagar do arquivo não remove do histórico do git.

Detalhes em [`gestao-de-segredos.md`](./seguranca/gestao-de-segredos.md).

---

## 12. Logs

O log registra **o que aconteceu**, não **com que credencial** nem **onde a pessoa estava**.

Nunca aparecem em log: token de acesso, cookie de renovação, senha, chave de integração, segredo de
webhook, token de rastreamento público, URL assinada e coordenada bruta. Erro não devolve *stack trace*
em produção; o cliente recebe um identificador de correlação, e o detalhe fica do lado de dentro.

> **Verificação:** `RedacaoDeLogTestes` exercita os cinco fluxos que emitem segredo e varre o texto de
> tudo o que foi registrado; `ProtecaoContraAbusoTestes` cobre o fluxo de autenticação; `TempoRealTestes`
> cobre o token do hub.

---

## 13. Auditoria

Trilha administrativa **somente-inserção**, protegida por gatilho no banco que recusa `UPDATE`, `DELETE`
e `TRUNCATE` com erro de privilégio — não por disciplina do código de aplicação. Registra criação e alteração de
cadastro, reatribuição e cancelamento de entrega, mudança de perfil, criação e revogação de credencial.

Telemetria de GPS **não** é duplicada na auditoria: ela tem histórico próprio, com prazo próprio, e
copiá-la para uma tabela sem prazo anularia a retenção da seção 7.5.

---

## 14. O que ainda não está coberto

Registrado aqui porque documento de segurança que só lista acertos é peça de marketing.

| Lacuna | Situação |
|---|---|
| CSP das aplicações web | depende de como forem servidas; decisão da Fase 25 |
| TLS, WAF e cabeçalhos de borda da hospedagem | Fase 25 |
| Retenção de comprovantes | o arquivo fica enquanto a entrega existir; prazo próprio exige decisão de produto |
| Retenção de requisições de integração | apagar a marca de idempotência reabre a porta para duplicata; a janela de garantia é decisão de produto |
| Rotação automática da chave de criptografia de webhook | hoje é troca manual com reescrita dos segredos |
| Pentest | Fase 24 |
| Verificação de conteúdo do arquivo enviado | o tipo é conferido na autorização; o conteúdo real não é inspecionado depois do upload direto |
| CI nunca executada | exige `git push`, ainda não autorizado |

---

## 15. Princípios que decidem os casos novos

Quando aparecer um caso que este documento não cobre, as regras que decidiram os anteriores:

1. **Falhar fechado.** Sem tenant resolvido, sem linha. Sem assinatura válida, sem arquivo.
2. **A resposta não ensina o contrato.** Inexistente, sem acesso e expirado respondem igual.
3. **O backend é a autoridade.** Frontend esconde por conveniência, não por segurança.
4. **Segredo se guarda por hash quando só precisa ser conferido**, e cifrado quando precisa ser usado.
5. **Dado pessoal tem prazo.** Se não tem, é porque alguém esqueceu — não porque é útil para sempre.
6. **Toda afirmação de segurança tem um teste com nome.** Sem isso, ela se desfaz na primeira refatoração
   e ninguém percebe.
