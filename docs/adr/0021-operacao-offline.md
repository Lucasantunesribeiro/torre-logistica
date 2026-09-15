# ADR 0021 — Operação offline, sincronização e idempotência das ações do motorista

- **Status:** aceita
- **Data:** 2026-09-15
- **Fase:** 12 — Offline, Sincronização e Idempotência

## Contexto

O motorista trabalha na rua, com conexão que cai, oscila e volta. Até a Fase 11, cada ação crítica da PWA
(iniciar rota, chegada, conclusão, tentativa sem sucesso, encerrar rota) era um `POST` direto: sem internet,
a ação não acontecia; com a resposta perdida, repetir o `POST` dependia de o comando ser naturalmente
idempotente pela máquina de estados — e a tentativa sem sucesso, por exemplo, não é.

O roadmap exige: fila local com `ClientOperationId` UUIDv7, tipo, payload, criação, status e tentativas;
processamento exatamente uma vez no servidor; resposta perdida sem efeito duplicado; conflito real
(cancelamento durante o offline) sem sobrescrever. Critério: nenhuma ação crítica pode depender de "tomara que
o POST não repita".

## Decisão

### Servidor: registro da operação na mesma transação do efeito

- `POST /api/motorista/sincronizacao` recebe um lote (1 a 100) de operações `{ operacaoDoClienteId, tipo,
  alvoId, motivo, criadaEm }` e responde **200** com um desfecho por operação, na ordem: `Aplicada`,
  `Conflito`, `Recusada` ou `TentarDeNovo`, com `repetida` quando o desfecho veio do registro.
- Tabela `operacoes_do_cliente` (somente-inserção por trigger), única por
  `(organizacao_id, motorista_id, operacao_do_cliente_id)`. O motorista vem da sessão; tenant e dono nunca vêm
  do corpo.
- Cada operação roda na própria transação: o registro `Aplicada` é inserido **junto** com o efeito, pelo mesmo
  comando do caminho online (`ExecucaoPeloMotorista`), com a mesma máquina de estados e as mesmas regras de
  propriedade. Ou os dois ficam, ou nenhum.
- Repetição encontra o registro e devolve o mesmo desfecho, sem executar. Mesmo identificador com outro
  conteúdo é `operacao_divergente`, e não herda o desfecho.
- Repetições simultâneas: a transação entra numa fila por motorista (`pg_advisory_xact_lock`) e confere o
  registro já na vez. Quem chega depois encontra o registro confirmado. O índice único continua como rede de
  segurança.
- **Conflito e recusa também são registrados e definitivos.** Repetir uma conclusão que conflitou com um
  cancelamento devolve o mesmo conflito, em vez de tentar de novo contra um estado que pode ter mudado outra
  vez. Operação malformada (identificador que não é UUIDv7, criada há mais de 7 dias ou no futuro além de
  2 minutos) é recusada sem registro: não há efeito a proteger.
- Conflito de versão (outra gravação na mesma entrega venceu) não aplica nem registra: o servidor reprocessa
  até 3 vezes e, persistindo, responde `TentarDeNovo`.
- Limite próprio por motorista (`SincronizacoesPorMinuto`, 30 por padrão) e métrica `sync.operations` por
  desfecho e repetição.

### PWA: fila em IndexedDB, projeção local e envio pelo próprio aplicativo

- Toda ação crítica é guardada no IndexedDB **antes** de qualquer rede, com UUIDv7 gerado no aparelho, e a PWA
  não usa mais os comandos diretos. Estados: `Pending`, `Uploading`, `Synced`, `Conflict`, `Failed`.
- A tela aplica as ações da fila sobre a leitura (projeção), com as mesmas transições da API — por isso a
  projeção é segura sobre uma leitura que já contém o efeito. Quem decide conflito é sempre o servidor.
- O envio não depende de Background Sync: acontece ao abrir o aplicativo, ao voltar a conexão, ao voltar a
  ficar visível, depois de cada ação e a cada 30 s. Um envio por vez, em ordem, em lotes de até 100.
- `Uploading` encontrado ao abrir é de um envio interrompido (aba fechada, aparelho reiniciado): vai de novo
  com o mesmo identificador.
- Falha de rede, 5xx, 401 e 429 deixam a ação `Pending`. Só o lote recusado inteiro por formato (400) vira
  `Failed`. Conflito e recusa ficam na tela, com frase para o motorista, até ele tocar "Entendi".
- Leituras (rotas do dia, rota, entrega) guardam cópia; sem rede ou com servidor fora, a tela mostra a cópia e
  diz de quando ela é. Resposta 401, 404 ou 409 não cai para a cópia, e 404/409 apagam a cópia.
- Posições não enviadas também vão para o IndexedDB e sobrevivem a fechar o navegador.
- Service worker mínimo guarda só a casca (HTML, scripts, estilos, manifesto, ícone). Nunca intercepta a API.

### Sessão sem conexão

- Abrindo sem rede, a renovação não tem como ser confirmada. Se o servidor confirmou uma sessão deste motorista
  no aparelho nos últimos 7 dias, a PWA abre com a identidade guardada (nome e organização; **nunca token**) e
  continua registrando na fila. A API autoriza cada operação quando a conexão volta; sessão recusada leva ao
  login e a fila espera o mesmo motorista.
- Sair apaga cópias, posições e identidade, e mantém as ações não enviadas (trabalho feito). Com ações
  pendentes, o aplicativo avisa antes. Saída sem rede fica anotada: na próxima abertura, a PWA encerra a
  sessão no servidor antes de qualquer renovação, para o aparelho não reentrar sozinho pelo cookie.

## Alternativas consideradas

- **Tabela de inbox genérica com resposta HTTP serializada.** Guardaria o corpo inteiro da resposta; o desfecho
  tipado é menor, estável entre versões e é o que a PWA precisa.
- **`Idempotency-Key` em cada comando existente.** Cinco rotas com a mesma lógica e um `POST` por ação ao
  reconectar; o lote ordenado é um só caminho e uma só política de limite.
- **Só idempotência pela máquina de estados.** Não cobre a tentativa sem sucesso (duas tentativas são dois
  fatos legítimos) nem distingue "repetição" de "segunda ação igual".
- **Background Sync como mecanismo principal.** Suporte irregular entre navegadores (CLAUDE.md, seção 29).
- **Biblioteca de IndexedDB (`idb`, Dexie).** O uso é pequeno e local; nenhuma dependência de produção nova.
- **Service worker com cache de API.** Duplicaria, sem sessão nem conflito, o que a fila e as cópias já fazem.

## Consequências

- A PWA funciona sem conexão para as ações da rota e mostra o que ficou pendente e o que não foi aplicado.
- Toda ação crítica tem um registro auditável de quando nasceu no aparelho e quando foi recebida.
- `fake-indexeddb` entra só como dependência de teste (Apache-2.0, sem dependências de runtime).
- A tabela `operacoes_do_cliente` cresce com o uso; retenção entra com as políticas de retenção do roadmap.

## Limitações conhecidas

- Com o navegador fechado, nada é enviado: o envio acontece na próxima abertura.
- A identidade guardada permite ver a cópia da rota sem rede por até 7 dias depois da última confirmação, mesmo
  que a conta tenha sido desativada nesse meio; nenhuma ação é aplicada sem a API autorizar.
- O service worker guarda a casca da versão atual; versão nova chega na primeira navegação com rede.
- Ações de motoristas diferentes no mesmo aparelho ficam separadas por conta; as de quem não voltar a entrar
  no aparelho não são enviadas.
