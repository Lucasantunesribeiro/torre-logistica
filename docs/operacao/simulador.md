# Simulador da operação

O simulador encena uma operação logística contra a API real. Ele é a diferença entre um sistema que
*parece* funcionar numa tela estática e um que se comporta como operação de verdade enquanto alguém
assiste.

## O que ele não faz

Não toca no banco. O projeto nem referencia a persistência, e um teste de arquitetura guarda isso
([ADR 0006](../adr/0006-simulador-externo.md)). Tudo acontece por HTTP autenticado, passando pela mesma
validação, máquina de estados e concorrência que a operação real atravessa.

É por isso que a demonstração vale: um atalho que escrevesse `UPDATE entregas SET status = 'Entregue'`
provaria apenas que o atalho funciona.

## Como rodar

Pré-requisitos: a API no ar, com a semeadura de desenvolvimento habilitada (ela cria a organização
`transportadora-aurora` e as contas que o simulador usa).

```bash
# a senha nunca vem do repositório
export Torre__Simulador__Senha='a-senha-da-semeadura'

dotnet run --project src/TorreLogistica.Simulator
```

| Chave | Padrão | Para quê |
|---|---|---|
| `Torre:Simulador:UrlBaseDaApi` | `http://localhost:5080` | onde a API está |
| `Torre:Simulador:Organizacao` | `transportadora-aurora` | onde a demonstração acontece |
| `Torre:Simulador:EmailDoAdministrador` | conta semeada | quem monta o palco |
| `Torre:Simulador:Senha` | **vazio** | sem ela o simulador recusa começar |
| `Torre:Simulador:OrigemDeclarada` | `http://localhost:5173` | ver *Por que declarar origem* |
| `Torre:Simulador:Semente` | `2026` | decide a narrativa |
| `Torre:Simulador:MultiplicadorDeTempo` | `60` | quantas vezes o tempo corre mais rápido |

## As seis histórias

Na ordem em que são encenadas. A ordem não é arbitrária: a operação normal vem primeiro para estabelecer o
que é o certo, e só então aparecem o risco, o silêncio do motorista e a porta fechada.

| # | História | O que a plateia vê | Quem decide |
|---|---|---|---|
| A | Operação normal | sai, se aproxima, o motorista registra chegada e entrega | o motorista |
| B | Risco de atraso | o motorista quase não anda, a folga até o fim da janela encolhe e a torre avisa | **o servidor**, na reavaliação periódica |
| C | Motorista offline | uma posição e silêncio; a última localização envelhece | **o servidor**, no motor de alertas |
| D | Tentativa frustrada | chega, ninguém atende, e a falha vira estado com motivo | o motorista |
| E | Entrada no geofence | ninguém chama "cheguei": a distância muda o estado sozinha | **o servidor**, na ingestão |
| F | Prova de entrega | conclusão com quem recebeu, onde e quando | o motorista |

As três marcadas em negrito são o ponto alto, e é por isso que o simulador **não as encena**: ele só cria a
situação e espera. Se o simulador escrevesse o alerta, a demonstração provaria que o simulador sabe
escrever alerta.

Nas histórias A, D e F o roteiro para **fora** do raio de 300 m do geofence, de propósito: dentro dele o
sistema detectaria a aproximação sozinho, e essas histórias virariam a E.

## Determinismo

A mesma semente conta a mesma história: os mesmos destinos, na mesma ordem, com as mesmas janelas e
distâncias, terminando nos mesmos estados.

O que **não** se repete são os identificadores que o sistema exige únicos — e-mail do motorista, placa,
CNPJ. Eles carregam também um carimbo da execução. Repetir a identidade faria a segunda encenação esbarrar
na unicidade que o próprio sistema garante, e a demonstração provaria, sem querer, que não dá para
demonstrar duas vezes.

> **Verificação:** `MesmaSementeContaAMesmaHistoria` encena duas vezes contra a API e compara a narrativa
> inteira — história, título, estado final e sequência de eventos de cada entrega.

## Tempo acelerado

`MultiplicadorDeTempo` comprime a espera do roteiro. A **janela prometida de cada entrega é criada na mesma
escala**: comprimir a espera sem comprimir a promessa faria toda entrega nascer atrasada, e a demonstração
contaria uma história falsa.

Com o padrão de 60×, uma janela de três horas do roteiro vira três minutos de relógio, e o que a plateia vê
em minutos é o que a operação levaria uma tarde para viver.

As histórias B e C dependem de temporizadores do servidor — a reavaliação de previsão e o limiar de
motorista offline — que **não** são comprimidos pelo multiplicador. Para vê-las numa apresentação curta, o
ambiente de demonstração precisa configurar esses limiares mais curtos:

```text
Torre:Previsao:IntervaloDeReavaliacao
Torre:Alertas:TempoSemPosicaoParaOffline
```

## Por que declarar origem

O login do console só é aceito a partir das aplicações da Torre, conferido pelo cabeçalho `Origin` — é a
defesa contra um site terceiro fazer o navegador de alguém autenticar sem querer.

O simulador não é navegador e não tem vítima a proteger, mas também não passa por cima da regra: ele
declara quem é, e quem opera decide se aquela origem entra na lista de permitidas. Sem declarar, o login é
recusado — que é o padrão correto.

## Reset

Não existe reset destrutivo, e isso é decisão, não esquecimento.

Cada execução monta o próprio palco: cadastros novos, rota nova, entregas novas. "Reiniciar" é encenar de
novo. Apagar o palco anterior exigiria remover linhas de tabelas que são **somente-inserção** por desenho —
timeline, ocorrências, comprovantes, auditoria —, e desligar esses gatilhos para uma demonstração seria
trocar uma garantia de verdade por uma conveniência.

A consequência é que o ambiente de demonstração acumula encenações. Limpar isso é assunto do modo
demonstração (Fase 24), que é onde "reiniciar cenário" tem dono.
