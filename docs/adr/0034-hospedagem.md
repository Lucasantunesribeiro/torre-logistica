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

### Azure Container Apps, com réplica mínima de **1**

E a parte que importa é o **1**, não o serviço.

O processo que atende HTTP é o mesmo que hospeda o trabalho periódico: despacho do outbox a cada 5
segundos, reavaliação de previsão a cada minuto, motor de alertas, limpeza por retenção a cada 6 horas. Um
serviço que dorme quando ninguém acessa **para de avaliar SLA, de despachar webhook e de apagar rastro
vencido**.

A entrega que entraria em risco às três da manhã só seria marcada quando alguém abrisse o console. E
alerta que chega quando o problema já passou não é alerta — é histórico.

Somado a isso: o console mantém conexão persistente (ADR 0017). Dormir derruba todo console conectado.

Portanto, das duas saídas que o ROADMAP oferecia, vale a segunda: **baixo custo ocioso, não escala a zero**.

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

### Teto de uma réplica, por enquanto

`maxReplicas` é **1**, e isso é limitação declarada, não descuido: o SignalR ainda não tem backplane
(ADR 0017). Com duas réplicas, consoles conectados a instâncias diferentes receberiam avisos diferentes —
e o operador que não vê o alerta é pior que o operador sem tempo real, porque ele confia no que vê.

Subir daqui exige backplane primeiro. A ordem é essa, e não a inversa.

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

O template **não foi validado por ferramenta**: a Azure CLI não está instalada na máquina de
desenvolvimento, e afirmar que um Bicep está correto sem compilá-lo seria inventar. O primeiro passo de
quem aplicar é `az bicep build`, e está escrito no `infra/README.md`.

Os números de desempenho da Fase 22 foram medidos com aplicação e banco na mesma máquina, sem rede no
meio. Em produção, cada ida ao banco paga latência de rede, e a ingestão faz várias por requisição. A
medição terá de ser refeita.

## Alternativas consideradas

Estão na tabela acima e, com as quantidades que dirigem o custo, em
[`docs/cost-model.md`](../cost-model.md).

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| A imagem sobe e responde | contêiner construído e exercitado: `/health/live` → **200** com o banco inacessível |
| A sonda de prontidão não mente | `/health/ready` → **503** no mesmo cenário |
| O processo não roda como root | UID 1654 dentro do contêiner |
| Falha de banco não vira ciclo de reinício | os serviços de fundo erram, registram e o processo continua vivo |
| A imagem é enxuta | 196 MB, sem ICU, porque o projeto compila com `InvariantGlobalization` |
| Nada foi provisionado | não há recurso criado; o template descreve, e aplicar exige autorização |
