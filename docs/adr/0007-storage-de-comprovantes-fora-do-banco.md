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

## Adendo — Fase 25: qual adaptador sobe é declarado

A abstração previa a troca do disco local por objeto em nuvem. Até aqui, porém, **só existe o adaptador
local**, e nada obrigava a dizer isso: subir em produção com comprovante em disco de contêiner era o
comportamento padrão e silencioso — e disco de contêiner some no reinício.

Agora `Torre:Armazenamento:Provedor` é explícito (`local` ou `blob`) e a validação roda na subida:

- `blob` **recusa subir** enquanto não houver adaptador. Sem fallback: cair no disco local em silêncio
  gravaria prova de entrega num lugar que todo mundo acreditaria ser o Blob.
- `local` fora de desenvolvimento e teste **recusa subir** sem
  `PermitirLocalForaDeDesenvolvimento` ligado, que é a aceitação consciente do custo.
- o diretório é criado e sondado com uma escrita real na subida — não na primeira entrega com foto.

Implementar o adaptador de Blob continua pendente, e a Storage Account descrita em `infra/` é o destino
dele.

## Como isto é verificado

Nesta fase, pela direção registrada e pelo limite de corpo configurado no Kestrel.

Na Fase 14, por teste de upload, validação de tipo e tamanho, expiração de URL
assinada, isolamento entre tenants e tentativa de acesso a arquivo de outra organização.
