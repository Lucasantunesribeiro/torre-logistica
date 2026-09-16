# ADR 0023 — Prova de entrega: comprovante somente-inserção e storage por URL assinada

- **Status:** aceita
- **Data:** 2026-09-16
- **Fase:** 14 — Proof of Delivery

## Contexto

A conclusão da entrega precisava de evidência: quem recebeu, quando, onde, foto e assinatura. O
[ADR 0007](./0007-storage-de-comprovantes-fora-do-banco.md) já decidiu, na Fase 0, que binário não entra no
PostgreSQL e que o fluxo é autorização → URL assinada → upload direto → metadados. Faltava implementar, e
decidir **qual** storage usar agora, sem contrariar duas restrições do projeto: nenhum recurso pago sem
autorização explícita, e a escolha de hospedagem é da Fase 25.

## Decisão

### Comprovante é entidade própria, somente-inserção e única por entrega

- Tabela `comprovantes` (um por entrega, garantido por índice único) e `arquivos_do_comprovante`, ambas com
  trigger que recusa `UPDATE`, `DELETE` e `TRUNCATE`. Prova que se reescreve não é prova.
- Campos: quem recebeu (obrigatório), observação, localização, instante do registro, autor, rota e motorista.
- Metadados do arquivo: tipo (foto ou assinatura), chave no storage, tipo de conteúdo, tamanho, **SHA-256** e
  instante da gravação — todos vindos do que o storage realmente gravou, nunca do que o cliente declarou.

### Storage local agora, S3 na Fase 25

- `IObjectStorage` (Application) expõe três operações: autorizar envio, autorizar leitura e obter metadados.
- O adaptador desta fase é **disco local** com URL assinada por HMAC-SHA256 servida pela própria API
  (`/api/arquivos/{chave}`). Roda em desenvolvimento, em teste e numa instância única, sem serviço externo e
  sem custo — o que respeita a regra de não criar recurso pago e a decisão do ADR 0007 de que a escolha de S3
  ou compatível é da Fase 25. Trocar o adaptador não toca domínio, aplicação nem API.
- A assinatura cobre operação, chave, tipo de conteúdo, tamanho máximo e expiração. Trocar qualquer um desses
  campos invalida a URL: é isso que impede subir um arquivo maior, de outro tipo, ou em outra chave, com uma
  autorização emitida para outra coisa.
- A gravação confere o tipo declarado contra o autorizado, corta no tamanho autorizado e calcula o hash.
  Chave com `..` ou caractere fora do conjunto permitido é recusada — travessia de diretório é a falha
  clássica de storage em disco.

### Os endpoints de arquivo são anônimos, e isso é proposital

A credencial é a própria assinatura da URL, emitida só depois de autenticação, autorização e verificação de
tenant — exatamente como numa URL pré-assinada de S3, onde o bucket não conhece a sessão de quem baixa. Sem
assinatura válida e dentro do prazo, a resposta é 403, sem revelar se o objeto existe.

### Conclusão com evidência

- `POST /api/motorista/entregas/{id}/comprovante` registra a prova **e** conclui a entrega no mesmo commit:
  status, evento da timeline e comprovante, ou nada.
- Repetir é seguro: entrega já concluída não gera evento novo, e o comprovante é único por entrega — a
  segunda chamada devolve o mesmo estado sem duplicar prova.
- `Torre:Comprovantes:ExigirNaConclusao` liga a política de prova obrigatória: com ela, concluir sem
  comprovante responde `422 comprovante_obrigatorio`, e o comprovante precisa de ao menos um arquivo.
- Leitura pelo console: `GET /api/entregas/{id}/comprovante` devolve metadados e URLs assinadas curtas.
  Registrar é do motorista — quem prova a entrega é quem entregou.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Criar bucket S3 agora | Recurso pago sem autorização, e decisão de hospedagem é da Fase 25 |
| Upload através da API | O servidor seguraria a conexão e precisaria aceitar corpo grande; o limite de 1 MB do Kestrel existe justamente porque arquivo não passa pela API |
| Guardar o binário no PostgreSQL | Contraria o ADR 0007: infla backup, polui cache e torna restauração lenta |
| Servir arquivo em rota autenticada por sessão | Funciona no console, mas não no fluxo de upload direto; e a URL assinada é o que permite trocar por S3 sem mudar cliente |
| Deixar a leitura pública com nome imprevisível | Segurança por obscuridade: a URL vaza por histórico, captura de tela ou proxy |
| Comprovante editável | Prova que se corrige depois não sustenta contestação de entrega |

## Consequências

- A entrega concluída é provável sem tornar arquivo algum público, que é o critério de aceite da fase.
- Há dois sistemas a manter consistentes: metadados no banco e objetos no storage. O metadado só é gravado
  depois de o objeto existir, então objeto órfão é possível e é lixo recolhível; metadado órfão, não.
- O adaptador local não serve para mais de uma instância nem para disco efêmero — é limitação conhecida e
  documentada, resolvida na Fase 25.
- O hash permite detectar arquivo trocado no storage sem passar pelo banco.

## Limitações conhecidas

- Sem antivírus e sem reprocessamento de imagem (remoção de EXIF, compressão): entram com a fase de
  segurança e privacidade.
- Retenção e ciclo de vida do objeto continuam para a Fase 20, como o ADR 0007 registrou.
- A captura de assinatura na tela ainda não existe no aplicativo; o tipo `Assinatura` já é aceito pela API.
