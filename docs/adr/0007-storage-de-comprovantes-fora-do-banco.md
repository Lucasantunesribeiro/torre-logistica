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

## Como isto é verificado

Nesta fase, pela direção registrada e pelo limite de corpo configurado no Kestrel.

Na Fase 14, por teste de upload, validação de tipo e tamanho, expiração de URL
assinada, isolamento entre tenants e tentativa de acesso a arquivo de outra organização.
