# ADR 0007 — Comprovantes em storage de objeto, fora do PostgreSQL

- **Data:** 2026-09-09
- **Status:** aceita
- **Fase:** 0 (direção); implementação na Fase 14

## Contexto

Prova de entrega inclui foto e assinatura. São arquivos de centenas de quilobytes a
alguns megabytes, escritos uma vez e lidos raramente, vindos de celular em rede móvel
instável.

Guardá-los no PostgreSQL como `bytea` significa: cada backup do banco carrega todas as
fotos; o cache de páginas do banco passa a ser ocupado por dado que ninguém consulta;
o upload atravessa o servidor de aplicação, que fica segurando conexão durante a
subida inteira; e restaurar o banco para investigar um incidente passa a levar o tempo
de copiar todas as imagens.

## Decisão

Arquivo binário **não entra no PostgreSQL**. Vai para storage de objeto, atrás da
abstração `IObjectStorage`. O banco guarda apenas metadados: identificador, tipo,
tamanho, hash, autor, entrega associada e instante.

Fluxo de upload preferencial:

```text
cliente pede autorização de upload
→ backend autoriza e devolve URL assinada de curta duração
→ cliente envia o arquivo direto ao storage
→ backend registra os metadados
→ metadados são associados à conclusão
```

O bucket é privado. Nunca público, em hipótese alguma. Leitura acontece por URL
assinada curta, emitida somente depois de checar autorização e tenant.

Em produção a direção provável é S3 ou compatível. A escolha final é da Fase 25, e a
abstração existe justamente para que ela não obrigue a reescrever o domínio.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| `bytea` no PostgreSQL | Infla backup, polui o cache do banco e faz o upload passar pelo servidor de aplicação |
| Large Object do PostgreSQL | Mesmos problemas, com API proprietária por cima |
| Disco local do servidor | Morre no primeiro deploy com mais de uma instância ou com sistema de arquivos efêmero |
| Upload através da API, que repassa ao storage | O servidor segura a conexão durante a subida e precisa aceitar corpo grande; URL assinada evita as duas coisas |
| Bucket público com nome imprevisível | Segurança por obscuridade: a URL vaza por histórico, captura de tela, log de proxy ou compartilhamento |

## Consequências

- O limite de corpo da API pode ser baixo — está em 1 MB — porque arquivo não passa
  por ela.
- Há dois sistemas a manter consistentes: metadados no banco e objeto no storage.
  Metadado sem objeto é possível e precisa ser detectável; por isso o metadado é
  registrado **depois** da confirmação de upload.
- A expiração da URL assinada é curta de propósito. URL longa é, na prática, um link
  público com prazo.
- Política de retenção e ciclo de vida do objeto entram na Fase 20.

## Adendo — Fase 25: o adaptador de nuvem existe, e qual sobe é declarado

A abstração previa a troca do disco local por objeto em nuvem. Por muito tempo **só existia o adaptador
local**, e nada obrigava a dizer isso: subir em produção com comprovante em disco de contêiner era o
comportamento padrão e silencioso — e disco de contêiner some no reinício.

Agora existem dois adaptadores do mesmo contrato, e `Torre:Armazenamento:Provedor` é explícito:

| | `local` | `blob` |
|---|---|---|
| Onde o binário fica | disco do processo | Azure Blob Storage, contêiner privado |
| Quem serve a URL assinada | a própria API | o Storage, direto |
| Como a URL é assinada | HMAC com chave da aplicação | SAS com chave de **delegação de usuário**, obtida com a identidade gerenciada |
| Sobrevive a reinício | **não** | **sim** |
| Para que serve | desenvolvimento e teste | produção |

A validação roda na subida e recusa o que não fecha: `blob` sem conta ou sem contêiner, nome de contêiner
inválido, autenticação desconhecida, e — acima de tudo — **chave de conta fora de Development/Testing**.
Chave de conta é segredo permanente com acesso total à conta inteira; ela só existe porque o emulador não
implementa delegação de usuário.

Nada de fallback: pedir `blob` com configuração incompleta derruba o processo, em vez de gravar prova de
entrega num lugar que todo mundo acreditaria ser o Blob.

### Uma diferença que a nuvem impõe

No adaptador local, o envio passa pela API, que confere o tipo e corta no tamanho máximo enquanto grava.
Uma SAS do Azure Blob **não consegue prometer nenhum dos dois** — não há cláusula de tipo nem de tamanho
na assinatura. Conferido contra o emulador: subir `text/plain` com uma SAS emitida para `image/jpeg` é
aceito com `201`.

As duas regras continuam valendo, um passo depois: `ObterAsync` devolve o tipo e o tamanho reais, e
`ArquivoDoComprovante.Criar` recusa o registro fora da política. O objeto rejeitado fica órfão — a
inconsistência que esta própria ADR já previa.

### E o resumo do conteúdo

Com o envio indo direto ao Storage, a API nunca vê os bytes e não pode calcular o SHA-256 no caminho. E o
resumo não pode vir do cliente: quem manda o arquivo é justamente quem não pode atestar o que mandou. O
adaptador lê o objeto uma vez, no registro, e grava o resultado como metadado do próprio objeto — as
leituras seguintes não baixam nada.

## Como isto é verificado

Nesta fase, pela direção registrada e pelo limite de corpo configurado no Kestrel.

Na Fase 14, por teste de upload, validação de tipo e tamanho, expiração de URL
assinada, isolamento entre tenants e tentativa de acesso a arquivo de outra organização.
