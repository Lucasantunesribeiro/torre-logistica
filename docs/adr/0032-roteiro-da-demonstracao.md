# ADR 0032 — Roteiro da demonstração: semente para a narrativa, carimbo para a identidade, e o servidor como protagonista

**Status:** aceito — Fase 23
**Decisores:** time técnico
**Relacionados:** [0006](./0006-simulador-externo.md), [0009](./0009-autenticacao-e-sessao.md), [0016](./0016-geofence-de-destino.md), [0018](./0018-previsao-de-chegada-e-sla.md), [0019](./0019-motor-de-alertas-operacionais.md)

## Contexto

O ROADMAP pede uma demonstração **viva e reproduzível**, com seis histórias obrigatórias, cenário e semente
fixos, tempo acelerado e reset seguro — e a regra dura da [ADR 0006](./0006-simulador-externo.md): nada de
`UPDATE` direto em tabela.

Três coisas precisavam ser decididas: o que exatamente se repete quando se repete o cenário; quem encena
cada história; e o que significa "reset" num sistema cujas tabelas centrais são somente-inserção.

## Decisão

### A semente decide a narrativa; o carimbo de execução decide a identidade

A semente fixa destinos, ruas, bairros, nomes, ordem das histórias, janelas e distâncias. Duas execuções
com a mesma semente contam a mesma história.

O que **não** se repete são os campos que o sistema exige únicos — e-mail do motorista, placa, CNPJ. Eles
carregam também um carimbo da execução. Repetir a identidade faria a segunda encenação esbarrar na
unicidade que o próprio sistema garante, e a demonstração provaria, sem querer, que não dá para demonstrar
duas vezes.

### Três histórias o simulador não encena — ele só cria a situação

Risco de atraso, motorista offline e entrada em geofence **nascem no servidor**: na reavaliação periódica
da previsão, no motor de alertas e na ingestão de posição. O simulador aproxima ou silencia o motorista, e
espera.

Se ele escrevesse o alerta, a demonstração provaria que o simulador sabe escrever alerta. É a mesma razão
pela qual ele não toca no banco, levada até o fim: **o que a plateia precisa ver é o sistema decidindo**.

### As histórias A, D e F param fora do raio do geofence

Dentro dos 300 m, o sistema detecta a aproximação sozinho e a entrega vai para `ProximaDoDestino` — e essas
histórias virariam a E, em que o ponto é justamente esse.

Parando fora do raio, quem diz "cheguei" é o motorista, e a diferença entre "o app registrou" e "o sistema
percebeu" fica visível. Isso foi descoberto pela medição do próprio teste, não pensado antes: a primeira
versão do roteiro aproximava demais e as histórias saíam iguais.

### O multiplicador comprime a espera **e** a promessa

A janela prometida de cada entrega é criada na mesma escala do tempo comprimido. Comprimir só a espera
faria toda entrega nascer atrasada, e a demonstração contaria uma história falsa logo no primeiro quadro.

O que o multiplicador **não** comprime são os temporizadores do servidor — reavaliação de previsão e limiar
de motorista offline. Eles são configuração do ambiente, e a demonstração precisa configurá-los curtos.
Está documentado no guia do simulador em vez de escondido numa constante.

### O simulador declara a própria origem

O login do console exige `Origin` conhecido, defesa de CSRF que existe desde a Fase 3. O simulador não é
navegador e não tem vítima a proteger, mas não passa por cima da regra: ele declara quem é, e **quem opera
decide** se aquela origem entra na lista de permitidas. Vazio significa não declarar, e o login é recusado.

A alternativa — abrir exceção no servidor para o simulador — criaria um caminho de autenticação sem a
defesa, que é exatamente o tipo de atalho que vira incidente em produção.

### Não existe reset destrutivo

Cada execução monta o próprio palco. "Reiniciar" é encenar de novo.

Apagar o palco anterior exigiria remover linhas de tabelas **somente-inserção** por desenho — timeline,
ocorrências, comprovantes, auditoria —, e desligar esses gatilhos para uma demonstração seria trocar uma
garantia de verdade por uma conveniência.

## Consequências

O ambiente de demonstração acumula encenações. Limpar isso é assunto do modo demonstração (Fase 24), que é
onde "reiniciar cenário" tem dono, e a alternativa provável ali é desativar a organização inteira em vez de
apagar linha.

As histórias B e C não aparecem num teste automatizado rápido: elas dependem de temporizadores reais do
servidor. O teste afirma o que é verdade — que foram encenadas e saíram para rota — e o resto está
documentado. Afirmar mais seria escrever um teste que passa sem provar.

O simulador ganhou uma referência no projeto de testes de integração. A seta continua apontando de fora
para dentro: o simulador é objeto de teste, não dependência da aplicação.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Semear o cenário direto no banco | contraria a ADR 0006 e provaria só que o atalho funciona |
| Simulador escrever alerta e risco | a demonstração provaria que o simulador sabe escrever |
| Semente também nos identificadores únicos | a segunda execução esbarraria na unicidade do sistema |
| Abrir exceção de `Origin` para o simulador | criaria caminho de autenticação sem a defesa |
| Endpoint de reset que apaga o cenário | exigiria desligar os gatilhos de somente-inserção |
| Comprimir só a espera, não a janela | toda entrega nasceria atrasada |
| Credencial de integração em vez de login | a API de integração cria entrega, não encena rota nem posição |

## Como a decisão é verificada

| Afirmação | Prova |
|---|---|
| Resetar e reproduzir leva ao mesmo storytelling | `MesmaSementeContaAMesmaHistoria`: duas encenações, narrativa comparada inteira |
| As seis histórias acontecem | `AsSeisHistoriasAcontecemEChegamAoDesfechoEsperado` |
| O geofence decide sozinho na história E | o mesmo teste: `ProximidadeDetectada` sem `ChegadaRegistrada` |
| A e E são histórias diferentes | o mesmo teste: a normal **não** tem `ProximidadeDetectada` |
| A semente manda de verdade | `SementeDiferenteMudaAOperacao`: destinos diferentes, ordem das histórias igual |
| O simulador não alcança o banco | teste de arquitetura da ADR 0006, que reprova referência a Domain, Application ou Infrastructure |
