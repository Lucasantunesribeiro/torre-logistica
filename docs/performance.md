# Performance e resiliência — números medidos

> Documento da Fase 22. Todos os números aqui foram **medidos**, não estimados, e vêm com o ambiente em
> que saíram. Nenhum deles é promessa comercial: a meta do ROADMAP é carga de engenharia.

Para reproduzir:

```bash
export TORRE_CARGA=1
export TORRE_CARGA_RELATORIO=/caminho/relatorio.txt
dotnet run --project tests/TorreLogistica.IntegrationTests -- \
  -class TorreLogistica.IntegrationTests.CargaDeIngestaoTestes
```

O benchmark fica **desligado por padrão**. Benchmark dentro de suíte de regressão mede errado — compete por
CPU com o que roda antes e depois — e vira teste instável que alguém acaba desabilitando por outro motivo.

## Ambiente da medição

| Item | Valor |
|---|---|
| Processador | AMD Ryzen 7 5700X3D, 8 núcleos físicos / 16 lógicos |
| Memória da máquina | 32 GB |
| Sistema | Windows 10 Pro |
| Banco | PostgreSQL 17 + PostGIS 3.5, em contêiner Docker sobre WSL2 |
| Memória disponível ao Docker | 7,8 GB |
| Aplicação | .NET 10, servidor de teste no mesmo processo da suíte |

Duas ressalvas honestas sobre este ambiente: aplicação e banco dividem a mesma máquina, o que **elimina a
latência de rede** que existiria em produção; e o WSL2 impõe um custo de E/S que uma instância dedicada não
teria. Os números servem para comparar decisões entre si e achar gargalos — não para prometer latência em
produção, que só a Fase 25 pode medir.

---

## 1. Ingestão de GPS sob a carga alvo

**Meta do ROADMAP:** 500 motoristas a uma posição a cada 15 segundos ≈ **33 posições por segundo**.

A mesma taxa foi produzida por **60 motoristas enviando com mais frequência**, e isso é deliberadamente
conservador: concentrar a taxa em menos chaves aumenta a disputa na tabela de posição atual, que é
justamente onde o `UPSERT` condicional poderia engasgar. Se aguenta assim, aguenta espalhado.

| Medida | Resultado |
|---|---|
| Vazão sustentada | **32,7 posições/s** (meta 33/s) |
| Latência p50 | **31,5 ms** |
| Latência p95 | 48,2 ms |
| Latência p99 | **245,3 ms** |
| Latência máxima | 258,4 ms |
| Posições recusadas pela política | 0 |
| Preparo (60 motoristas em rota, pela API) | 16,4 s |

Cada requisição atravessa o caminho real: autenticação, política de qualidade da posição, gravação no
histórico, `UPSERT` condicional da posição atual, avaliação de geofence e publicação em tempo real.

### O que o p99 revela

O p99 é **8 vezes** o p50. Isso não é volume de dados — é **concorrência**: 33 requisições partindo juntas a
cada segundo, disputando pool de conexões e coletor de lixo. A prova está na seção 3: com a tabela 2.880
vezes maior, mas requisições em série, a latência **cai** para 9,7 ms.

É o gargalo conhecido número um, e está registrado na seção 6.

## 2. Leituras do console sob a mesma carga

| Consulta | p50 | p95 |
|---|---|---|
| Lista do mapa (entregas em rota) | 4,0 ms | 4,4 ms |
| Posição atual de um motorista | 2,9 ms | 3,6 ms |

A separação entre histórico e projeção de posição atual (ADR 0015) se paga aqui: a tela que o operador
mantém aberta lê uma linha por motorista, não o caminho percorrido.

---

## 3. Volume de um dia inteiro de operação

Medir plano de consulta com mil linhas não diz nada: o planejador escolhe varredura sequencial porque a
tabela cabe em poucas páginas, e o resultado engana nos dois sentidos — parece rápido e parece sem índice.

Aqui o histórico foi semeado até **2.851.200 posições**, que é o que 500 motoristas produzem em 24 horas na
meta. A semeadura é feita em SQL porque o que se mede é a decisão do banco; o caminho da aplicação já foi
medido na seção 1.

| Medida | Resultado |
|---|---|
| Linhas | 2.851.200 |
| Tempo de semeadura | 111,6 s |
| Tabela `posicoes` | **1.023 MB** no total |
| Só de índice | **528 MB** — 52% do total |

### Planos de consulta (com estatísticas atualizadas, medidos a quente)

| Consulta | Plano | Tempo |
|---|---|---|
| Posição atual por motorista | `Seq Scan` em `posicoes_atuais` | 0,01 ms |
| Histórico de um motorista por período | usa `ix_posicoes_motorista_id_capturada_em` | **0,19 ms** |
| Limpeza por retenção (lote de 5.000) | usa `ix_posicoes_recebida_em` | 2,77 ms |
| Outbox pendente | `Seq Scan` | 0,03 ms |

Leitura dos resultados:

- **O índice da Fase 7 se paga.** Buscar 500 posições de um motorista em 2,85 milhões leva 0,19 ms. Sem
  índice, seria varredura de tabela de 1 GB.
- **O índice da Fase 20 se paga.** A limpeza por retenção encontra o lote mais antigo por índice, e o
  `DELETE` real de 5.000 linhas levou **12 ms**.
- **Os dois `Seq Scan` são corretos, não descuido.** `posicoes_atuais` tem uma linha por motorista e o
  outbox é fila que esvazia: nesses tamanhos, varrer é mais barato que percorrer índice, e o planejador
  está certo. Os índices existem e passam a ser escolhidos quando o volume justifica.

### Ingestão com a tabela cheia

| Medida | Tabela vazia, 33 req/s simultâneas | Tabela com 2,85 M linhas, requisições em série |
|---|---|---|
| p50 | 31,5 ms | **9,7 ms** |
| p95 | 48,2 ms | 20,4 ms |

**Conclusão:** o volume de dados não degradou a ingestão. O que custa é a concorrência.

---

## 4. Particionamento temporal: decisão de **não** implementar

O ROADMAP autoriza particionar `posicoes` **apenas se a medição justificar**. Ela não justifica:

| Argumento a favor | O que a medição mostra |
|---|---|
| Consulta lenta no histórico | 0,19 ms para 500 linhas em 2,85 milhões |
| Limpeza lenta | 12 ms por lote de 5.000 |
| Tabela grande demais | 1 GB por dia é grande, mas a retenção de 30 dias põe teto em ~30 GB |

O que a medição **de fato** mostra como custo é outro: **528 MB de índice por dia**, metade do peso da
tabela. Particionar não resolveria isso — os índices continuariam existindo, só que por partição.

Fica registrado o gatilho para rever a decisão: se a limpeza por retenção passar a não acompanhar o volume
de entrada — o que aparece como o aviso `rodada encerrada no teto de lotes com dado vencido restante` —,
particionar passa a valer, porque aí o expurgo vira `DROP PARTITION` em vez de `DELETE` em lote.

---

## 5. Resiliência

| Modo de falha | Comportamento medido | Onde está provado |
|---|---|---|
| Banco cai no meio da operação | a primeira operação depois da queda falha; da segunda em diante o processo se recupera **sozinho**, sem reinício | `ConexaoDerrubadaNaoDerrubaAOperacao` |
| Processo cai no meio do despacho | nada se perde e nada duplica: a mensagem continua pendente ou virou entrega, nunca as duas | `QuedaNoMeioDoDespachoNaoPerdeNemDuplicaEvento` |
| Provedor de rotas não responde | a previsão cai na contingência em linha reta e diz por quê; a ingestão não trava esperando | `ProvedorQueNaoRespondeNaoSeguraAIngestaoECaiNaContingencia` |
| Assinante de webhook fora do ar | seis tentativas com espera crescente e desistência **visível** na API | `AssinanteQuebradoEhRetentadoEDepoisDesisteDeFormaVisivel` |
| Mensagem repetida | efeito único, por identificador do cliente gravado na mesma transação do efeito | `SincronizacaoTestes` |
| Tempo real cai | o console avisa a queda e continua correto pela API; o aviso é conveniência, não fonte de verdade | ADR 0017 e testes do console |

### O que a queda do banco revelou

O PostgreSQL devolve `57P01` — *terminating connection due to administrator command* — quando a conexão é
encerrada por reinício ou failover. **O provedor não classifica esse código como falha transitória**, então
a estratégia de nova tentativa do EF Core, que está ativa (3 tentativas), não entra em ação para ele.

Ampliar a classificação para retentar `57P01` foi considerado e **recusado**: repetir escrita por conta
própria custa mais do que um erro isolado num reinício planejado, que é evento raro e anunciado. O
comportamento fica documentado aqui e travado por teste — que é a diferença entre limitação conhecida e
surpresa.

---

## 6. Gargalos conhecidos

Em ordem de quanto pesam:

1. **Latência sob concorrência (p99 de 245 ms).** Oito vezes o p50, com a tabela vazia. O suspeito é
   disputa de pool de conexões e coletor de lixo, não o banco — a seção 3 mostra o mesmo trabalho em 9,7 ms
   quando as requisições não competem. Investigar exige as métricas de runtime da Fase 21 ligadas contra um
   coletor, o que depende da Fase 25.
2. **Índice ocupa metade da tabela.** 528 MB de índice para 495 MB de dados, por dia de operação. São cinco
   índices em `posicoes`, cada um com um caso de uso real. Reduzi-los é possível, mas cada remoção paga com
   uma consulta mais lenta — decisão que precisa de dado de uso real, não de opinião.
3. **Primeira leitura depois de queda do banco falha.** Seção 5. Limitação aceita conscientemente.
4. **A medição não tem rede.** Aplicação e banco na mesma máquina. Em produção, cada ida ao banco paga
   latência de rede, e a ingestão faz várias por requisição. O número real só sai na Fase 25.

## 7. O que **não** foi medido

- **SignalR sob carga.** A contagem de conexões existe como métrica (Fase 21), mas não há medição de
  latência de aviso com muitos consoles conectados. Exige múltiplos clientes reais, e o ganho de informação
  não justificou o custo nesta fase.
- **Storage sob falha.** O armazenamento hoje é disco local; a falha que importa é a do provedor de objeto,
  que só existe a partir da Fase 25.
- **Vários dias de volume acumulado.** Foi medido um dia. A retenção põe teto em 30, e o comportamento
  entre 1 e 30 dias é linear no tamanho, não no tempo de consulta — que usa índice.
- **Concorrência de escrita no mesmo agregado sob carga.** Os conflitos de concorrência têm testes próprios
  (Fase 6), mas não sob carga sustentada.
