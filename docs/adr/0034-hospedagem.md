# ADR 0034 — Hospedagem: contêiner sempre vivo, porque o sistema trabalha quando ninguém olha

**Status:** aceito — Fase 25 (escolhido e escrito; **nada provisionado**)
**Decisores:** Lucas (decisão de produto e custo) e time técnico
**Relacionados:** [0001](./0001-monolito-modular.md), [0002](./0002-postgresql-postgis.md), [0017](./0017-tempo-real-da-operacao.md), [0026](./0026-webhooks-e-backbone-assincrono.md), [0029](./0029-retencao-de-localizacao.md), [0030](./0030-observabilidade.md)

## Contexto

O ROADMAP abre esta fase com uma proibição: *não copiar automaticamente a arquitetura Lambda da Central
Antifraude*. E pede um gate — comparar antes de provisionar — com "scale-to-zero **ou** baixo idle cost"
entre os critérios.

O projeto chega aqui com números próprios, medidos na Fase 22: 33 posições por segundo sustentadas, p50 de
10 ms com a tabela cheia, ~1 GB de banco por dia, ~30 GB em regime pela retenção de 30 dias.

## Decisão

### Azure Container Apps, com **duas** aplicações e réplica mínima de 1 em cada

O trabalho periódico — despacho do outbox a cada 5 segundos, reavaliação de previsão a cada minuto, motor
de alertas, limpeza por retenção — **não depende de requisição nenhuma**. A entrega que entraria em risco
às três da manhã precisa ser marcada mesmo com o console fechado, e alerta que chega quando o problema já
passou não é alerta: é histórico.

Isso elimina scale-to-zero. E, na revisão desta fase, mostrou que também eliminava o desenho de um
processo só: até aqui a API hospedava esses laços, o que amarrava as duas coisas — a borda não podia
escalar sem duplicar o trabalho de fundo, e o trabalho de fundo não podia parar sem derrubar a borda.

| | API | Workers |
|---|---|---|
| Ingress | HTTPS público | **nenhum** |
| Por que fica viva | conexão persistente do console (ADR 0017) | os laços só existem enquanto o processo vive |
| `minReplicas` | 1 | 1 |
| `maxReplicas` | 1 — ver abaixo | 1 — dois despachantes disputariam o mesmo outbox sem ganho |
| Recursos | 0,5 vCPU / 1 GiB | 0,25 vCPU / 0,5 GiB |

A separação é estrutural, não configuracional: o registro das dependências continua nos dois processos,
porque a API **lê** o que os workers produzem, mas o registro dos laços (`AdicionarProcessamentoDe…`)
existe só no host de workers. Uma opção de configuração poderia ser ligada por engano; a ausência de uma
chamada, não.

### Por que não as outras

| Descartada | Motivo |
|---|---|
| **AWS Lambda** | conexão persistente não cabe no modelo de invocação; é exatamente o que o ROADMAP alerta |
| **AWS App Runner** | suporte a WebSocket **não confirmado na documentação oficial** — para requisito obrigatório, "não documentado" basta |
| **Aurora Serverless v2** | a capacidade mínima cobrada continuamente custa mais que uma instância pequena dedicada |
| **ECS Fargate + ALB** | o balanceador sozinho custa mais que todo o compute deste projeto |
| **Kubernetes gerenciado** | plano de controle pago e operação que um monólito modular não pede |
| **AWS Lightsail + RDS** | tecnicamente viável, com preço fixo a favor; perdeu na diversificação de nuvem do portfólio |
| **EC2 + RDS** | mais barato na fatura, mas manter sistema operacional e TLS é custo que não aparece nela |

### Teto de uma réplica na API, e o caminho para sair dele

`maxReplicas` é **1**, e isso é limitação declarada, não descuido: o SignalR ainda não tem backplane
(ADR 0017). Com duas réplicas, consoles conectados a instâncias diferentes receberiam avisos diferentes —
e o operador que não vê o alerta é pior que o operador sem tempo real, porque ele confia no que vê.

O caminho para escalar, quando o tráfego justificar e **só então**:

1. ligar um backplane (Azure SignalR Service em modo *Default*, ou Redis), que é o que faz o aviso
   publicado numa instância alcançar as conexões das outras;
2. subir `maxReplicas` da API.

Nada além disso precisa mudar — e é justamente por causa da separação desta fase: com os laços fora da
API, escalar a borda não multiplica o trabalho de fundo. Antes, subir uma réplica significaria dois
despachantes de outbox e duas reavaliações concorrentes.

A ordem é essa, e não a inversa. Subir a réplica antes do backplane produz um sistema que parece
funcionar e mente para metade dos operadores.

### Retenção: avaliada para Container Apps Job, mantida no processo de trabalho

A limpeza roda a cada 6 horas — cara de tarefa agendada, e `Microsoft.App/jobs` com `cron` seria a forma
canônica. Recusada por um motivo simples: **os workers já estão sempre vivos** por causa do outbox, que
roda a cada 5 segundos. Um Job traria uma terceira imagem, um terceiro recurso e um terceiro caminho de
configuração para executar trabalho que o processo existente faz numa rodada de 12 ms (medido em
`docs/performance.md`).

Gatilho para rever: se o outbox virar fila nativa com escala por evento, o processo de trabalho deixa de
precisar estar sempre vivo — e aí a retenção passa a valer como Job.

### O que a infraestrutura **não** faz

Três coisas ficaram fora do template de propósito:

- **Atribuição de papéis** à identidade gerenciada: é operação de diretório, e costuma exigir permissão
  que uma pipeline de aplicação não deveria ter.
- **Migrations**: schema é passo de deploy, não de infraestrutura. Misturados, um `what-if` de
  infraestrutura pareceria inofensivo quando não é.
- **Frontends**: são arquivos estáticos, não guardam segredo e não precisam de contêiner.

### Deploy só à mão

O workflow dispara apenas por `workflow_dispatch`, com uma confirmação digitada. Não há gatilho por push:
um deploy que acontece porque alguém mergeou algo é um deploy que ninguém decidiu fazer.

A autenticação é federada — o workflow troca o token do GitHub por um do Azure na hora, sem segredo de
longa duração guardado no repositório.

## Consequências

Existe um custo mensal fixo enquanto o ambiente estiver de pé: contêiner e banco não dormem. É o preço de
um sistema que continua operando quando ninguém está olhando — que é, afinal, o que ele se propõe a ser.

O template **foi validado**: `bicep build` e `bicep lint` passam sem erro nem aviso, e os 12 recursos que
ele geraria estão listados em `infra/README.md`. O que **não** rodou foi o `what-if`, e o motivo não é
comodidade: ele exige assinatura autenticada e grupo de recursos existente, e nenhum dos dois existe.

Os números de desempenho da Fase 22 foram medidos com aplicação e banco na mesma máquina, sem rede no
meio. Em produção, cada ida ao banco paga latência de rede, e a ingestão faz várias por requisição. A
medição terá de ser refeita.

## Alternativas consideradas

Estão na tabela acima e, com as quantidades que dirigem o custo, em
[`docs/cost-model.md`](../cost-model.md).

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| A API não hospeda mais os laços | contêiner da API sem banco: **0 ocorrências** de "Falha na rodada"; a imagem anterior enchia o log delas |
| Os workers assumiram os laços | contêiner dos workers contra o banco real: vivo, registrando as rodadas |
| Os workers não têm porta | `docker ps` mostra a coluna de portas **vazia**; a imagem usa `runtime`, não `aspnet` |
| A separação não mudou comportamento | as 5 suítes que dependem dos laços dão o mesmo resultado antes e depois: 33 provas, a mesma 1 falha pré-existente |
| A API sobe e responde | com banco real: migrations aplicadas e `/health/ready` **200** |
| A sonda não mente | sem banco: `/health/live` **200** e `/health/ready` **503** |
| Nenhum processo roda como root | UID **1654** nos dois contêineres |
| O template está correto | `bicep build` e `bicep lint` sem erro nem aviso |
| Nenhum segredo fixo | `senhaDoBanco` é `securestring` e não consta do arquivo de exemplo |
| Nada foi provisionado | não há recurso criado; aplicar exige autorização |
