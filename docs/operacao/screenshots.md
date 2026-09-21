# Plano de capturas da demonstração

As sete capturas que o ROADMAP exige, com **o que precisa estar na tela** em cada uma e como chegar lá.
Existe porque captura de tela improvisada mostra a tela vazia — e tela vazia é exatamente o que não se
quer mostrar de um sistema operacional.

## Como preparar o palco

```bash
# 1. a API no ar, com demonstração e semeadura ligadas
export Torre__Demonstracao__Habilitada=true
export Torre__Demonstracao__Senha='...'
dotnet run --project src/TorreLogistica.Api

# 2. a operação acontecendo, em ritmo de apresentação
export Torre__Simulador__Senha='...'
export Torre__Simulador__MultiplicadorDeTempo=20
dotnet run --project src/TorreLogistica.Simulator
```

Para as capturas 4 e 2, o ambiente precisa dos limiares curtos — senão o alerta e o risco levam a jornada
inteira para aparecer:

```text
Torre:Previsao:IntervaloDeReavaliacao   = 00:00:30
Torre:Alertas:TempoSemPosicaoParaOffline = 00:02:00
```

## As sete capturas

| # | Tela | O que precisa estar visível | Quando capturar |
|---|---|---|---|
| 1 | **Mapa da Operação** | pontos de motorista, destinos, lista lateral sincronizada, ao menos uma entrega em risco com cor de atenção | depois que o simulador iniciou a rota e as primeiras posições chegaram |
| 2 | **Entrega com ETA e SLA** | chegada prevista, situação (Atenção ou Risco), e a **explicação** do porquê — é ela que mostra que o número não é palpite | história B, depois que a reavaliação mudou a situação |
| 3 | **Motorista e rota** | rota do dia com paradas, última posição conhecida, horário da última notícia | qualquer momento com a rota em andamento |
| 4 | **Alerta operacional** | alerta aberto com evidência: quantos minutos sem posição, qual entrega, qual motorista | história C, depois de o limiar de offline vencer |
| 5 | **PWA do motorista** | próxima parada, ação grande, indicador de conexão — em viewport de celular (390 × 844) | com a rota iniciada, antes da conclusão |
| 6 | **Rastreamento público** | código do pedido, marcos, região aproximada do veículo — **sem** nome de motorista, rota ou endereço completo | com a entrega a caminho, pelo link emitido no console |
| 7 | **Prova de entrega** | quem recebeu, quando, onde, e a miniatura do comprovante | história F, depois da conclusão |

## Regras das capturas

- **Sem dado de pessoa real.** Tudo é fictício e gerado pelo simulador; conferir antes de publicar.
- **Sem token na barra de endereço.** A captura 6 usa um link com token — recortar a barra ou usar um
  token já revogado depois.
- **Viewport declarado.** Console e rastreamento em 1440 × 900; PWA em 390 × 844. Capturas de larguras
  diferentes numa mesma sequência fazem o produto parecer inconsistente quando não é.
- **Estado cheio, nunca vazio.** Nenhuma captura com lista vazia ou "carregando": o que se demonstra é
  operação acontecendo.
- **Tema claro.** É o único implementado; mostrar dois temas sugeriria um recurso que não existe.

## Por que não estão neste commit

As capturas exigem os três frontends e a API no ar simultaneamente, com o simulador encenando em tempo
real. Isso é trabalho de máquina com tudo publicado, e o ambiente público é decisão da Fase 25.

Publicar capturas tiradas de um ambiente local meio montado seria pior que não ter: elas envelhecem, não
batem com o que o visitante vê ao clicar em *Explorar demonstração*, e a primeira coisa que alguém nota é
a divergência.
