# Torre Logística — plano para um portfólio de nível sênior, meta 95+/100

**Responsável pela execução futura:** Codex, em outro chat.
**Data:** 08/10/2026.
**Base:** auditoria de 07/10/2026; consulta de dependências repetida em 08/10/2026.
**Nota atribuída:** 78/100.
**Meta:** alcançar pelo menos 95/100 em uma nova avaliação baseada em evidências.
**Estado:** planejamento; nenhuma correção deste plano foi implementada na criação do relatório.

## 1. Orientação para executar em outro chat

Leia [AGENTS.md](AGENTS.md), que aponta para [CLAUDE.md](CLAUDE.md) e [ROADMAP.md](ROADMAP.md). Leia os documentos de governança completos e as ADRs pertinentes antes de implementar.

O pedido atual autorizou somente os relatórios. Quando Lucas autorizar executar este plano, implemente os itens obrigatórios até os critérios de conclusão. Explique cada grupo de mudanças em linguagem simples antes de editar código. Continue autonomamente nas escolhas locais, reversíveis e já autorizadas.

Preserve a stack, isolamento, eventos, offline, privacidade, custos e decisões existentes. Preparar localmente não equivale a autorizar push, deploy, DNS, mudança de dados publicados, recurso pago ou operação destrutiva. Conclua o trabalho local e revisável antes de qualquer aprovação externa necessária.

**Primeiro:** comparar com o commit auditado, reproduzir a lacuna de destino de webhook em teste local e corrigi-la. Não iniciar por novos gráficos ou módulos.

## 2. Estado auditado e limites

| Item | Evidência |
|---|---|
| Pasta | F:/Programacao_novos_projetos_2026/torre_logistica |
| Commit local auditado e ainda presente em 08/10 | 6f885d62cee60b6a9fea48488e34c750c04131ed |
| Git inicial | Sem alterações locais antes deste relatório |
| Frontend executado em 07/10 | 131 testes aprovados: 76 motorista, 38 operação, 17 rastreamento |
| Contratos locais | 5 ignorados por falta do ambiente específico; não contá-los como aprovados localmente |
| Outros checks locais | Lint, tipos e builds aprovados |
| CI do mesmo commit | [Execução 36615702523](https://github.com/Lucasantunesribeiro/torre-logistica/actions/runs/36615702523), aprovada em setembro, incluindo job de contrato real |
| Backend nessa CI | 729 unitários, 28 de arquitetura; integração com 571 casos totais, incluindo 2 ignorados |
| npm audit repetido em 08/10 | 2 altas: brace-expansion e source-map-js; zero achados com --omit=dev |
| Demo observada | https://operacao.torre.lucasafvr.com.br |
| Backend local | Não reexecutado por ausência de .NET 10 disponível e Docker parado |

A consulta ao catálogo NuGet em 07/10 não encontrou correspondência nas dependências dos arquivos locais de restauração existentes. Repetir com restore atual antes da conclusão.

O problema de endereços internos foi identificado no código e pela interpretação local de endereços IP. Não foi explorado na infraestrutura publicada. A navegação pública foi uma verificação de leitura do console; não comprovou todos os caminhos do motorista, uploads e rastreamento em aparelhos reais.

Datas e indicadores da demo referem-se a 07/10. Conferir novamente antes de agir. A versão publicada não foi comprovada como idêntica ao commit local.

## 3. Diferenciais a preservar

- Console operacional, PWA do motorista e rastreamento público com necessidades distintas.
- Atualização em tempo real com SignalR.
- Localização, geofence, previsão de chegada e alertas com evidência.
- Fila offline no aparelho, identificação estável da operação e proteção contra duplicidade.
- Dados e fila separados por usuário; service worker voltado à aplicação, sem cache indevido da API.
- Eventos históricos, Outbox, auditoria e testes com PostgreSQL/PostGIS.
- Privacidade do rastreamento público e dos comprovantes.

A nota deve subir pelo funcionamento confiável e apresentação desses diferenciais. Não acrescentar mensageria, microserviços, nova cloud ou mais tecnologias para aumentar a aparência de complexidade.

## 4. Lista priorizada e ordem

| ID | Prioridade | Trabalho | Conclusão |
|---|---|---|---|
| TL-01 | P0 | Fechar as lacunas de acesso interno pelos webhooks | [ ] |
| TL-02 | P1 | Tornar o cenário demonstrativo recente ou explicitamente reproduzível | [ ] |
| TL-03 | P1 | Entregar mapa contextual útil na demonstração | [ ] |
| TL-04 | P1 | Resolver alertas de dependências e alinhar ambiente | [ ] |
| TL-05 | P2 | Evitar carregar MapLibre na entrada da aplicação | [ ] |
| TL-06 | P1 | Provar offline, tempo real e privacidade nos fluxos principais | [ ] |
| TL-07 | P1 | Corrigir documentação e delimitar resultados de segurança | [ ] |
| TL-08 | P2 | Comprovar desempenho e limites da demonstração | [ ] |
| TL-09 | P2 | Organizar roteiro, capturas e evidências de conclusão | [ ] |

P0 representa a prioridade de execução; a lacuna de webhook foi classificada como gravidade alta na auditoria.

Ordem: preparar ambiente → TL-01 → TL-04 → TL-02 → TL-03 → TL-05 → TL-06 → TL-08 → TL-07 → TL-09 → reavaliar.

## 5. TL-01 — Webhooks não podem alcançar serviços internos

### Evidência

[ClienteDeWebhookHttp.cs](src/TorreLogistica.Infrastructure/Webhooks/ClienteDeWebhookHttp.cs), aproximadamente linhas 25–80, resolve o host e testa se os IPs são públicos. No método EhPublico, um endereço que não seja IPv4 recebe aprovação depois de algumas verificações IPv6.

Não existe normalização de IPv4 mapeado em IPv6. A interpretação local mostrou que estes endereços são IPv6 mapeados e não são classificados como loopback, link-local, site-local ou multicast:

~~~text
http://[::ffff:10.0.0.5]/
http://[::ffff:169.254.169.254]/
~~~

Eles representam destinos internos que deveriam ser recusados.

Além disso, a consulta DNS de validação acontece antes de HttpClient abrir a conexão com o domínio. [ProcessamentoDeWebhooks.cs](src/TorreLogistica.Infrastructure/Webhooks/ProcessamentoDeWebhooks.cs), aproximadamente linha 119, desativa redirecionamentos e limita conexão, mas não fixa o destino aprovado nem aplica uma validação equivalente ao endereço efetivamente conectado.

**Condição de acesso:** o cadastro de webhook exige a política GestaoDeUsuarios em [EndpointsDeWebhooks.cs](src/TorreLogistica.Api/Webhooks/EndpointsDeWebhooks.cs). O risco envolve uma pessoa com esse privilégio; não foi identificado cadastro anônimo.

**Impacto:** usar o processo de entrega de webhooks para enviar requisições a destinos da rede interna. A inspeção estática não prova quais serviços seriam alcançáveis no ambiente publicado.

### Arquivos de partida

- [ClienteDeWebhookHttp.cs](src/TorreLogistica.Infrastructure/Webhooks/ClienteDeWebhookHttp.cs).
- [ProcessamentoDeWebhooks.cs](src/TorreLogistica.Infrastructure/Webhooks/ProcessamentoDeWebhooks.cs).
- [EntregaDeWebhooks.cs](src/TorreLogistica.Application/Webhooks/EntregaDeWebhooks.cs).
- [Webhook.cs](src/TorreLogistica.Domain/Webhooks/Webhook.cs).
- [AssinaturaHmacDeWebhookTestes.cs](tests/TorreLogistica.UnitTests/Aplicacao/AssinaturaHmacDeWebhookTestes.cs).
- [WebhooksTestes.cs](tests/TorreLogistica.IntegrationTests/WebhooksTestes.cs).
- [AssinanteDeWebhookDeTeste.cs](tests/TorreLogistica.IntegrationTests/Infra/AssinanteDeWebhookDeTeste.cs).

### Correção proposta

1. Normalizar endereços IPv4 mapeados em IPv6 antes de aplicar a política.
2. Definir uma política explícita para endereços locais, privados, não especificados, multicast e demais faixas proibidas no contexto.
3. Validar o destino usado na abertura da conexão, ou conectar exclusivamente ao endereço aprovado.
4. Preservar o nome do host para HTTP Host, SNI e verificação do certificado TLS.
5. Aplicar a mesma política nas novas conexões, tentativas posteriores e alternativas de endereço.
6. Manter redirecionamentos desativados e os limites de tempo.
7. Inspecionar o uso de proxy do processo para que ele não contorne a garantia.
8. Garantir que a opção de destino local continue restrita ao cenário autorizado de desenvolvimento/teste.

A [referência OWASP sobre SSRF](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) recomenda vincular a conexão ao endereço validado; uma segunda resolução DNS sem controle permite contornar a verificação.

Não trocar para uma lista fixa de clientes que quebre o contrato do produto. Não desligar TLS, não bloquear IPv6 público indiscriminadamente e não enviar chamadas de teste a serviços internos reais.

### Testes obrigatórios

Usar resolução e conexão controladas por teste, com servidores locais isolados. Um teste de DNS variável precisa provar qual destino foi efetivamente usado, não apenas que o validador foi chamado.

| Caso | Resultado esperado |
|---|---|
| IPv4 privados, loopback, link-local e faixas proibidas | Recusados |
| ::ffff:10.0.0.5 e ::ffff:169.254.169.254 | Recusados pela mesma política IPv4 |
| IPv6 loopback, local, único local, multicast e não especificado | Recusados conforme a política |
| Endereço público IPv4 e IPv6 permitido | Entrega continua funcionando |
| DNS com combinação pública e interna | Comportamento conservador explícito; nenhum destino interno recebe conexão |
| DNS inicialmente público e depois interno | Conexão permanece no IP aprovado ou é recusada |
| Redirecionamento para endereço interno | Não é seguido |
| Nova tentativa ou endereço alternativo | Mesma validação |
| Destino inexistente, lento ou cancelado | Falha controlada e prazo respeitado |
| HTTPS permitido | Certificado continua validado para o host correto |
| Modo local de teste | Só funciona quando explicitamente habilitado para o ambiente autorizado |

**Aceite TL-01:**

- [ ] Os dois endereços mapeados são reproduzidos e bloqueados em teste permanente.
- [ ] A conexão não consegue usar um destino interno após a validação de DNS.
- [ ] Testes comprovam o endereço usado pelo cliente, inclusive em retentativas.
- [ ] Assinaturas HMAC, política administrativa, retries e registro de resultados continuam corretos.
- [ ] Segredos e payloads sensíveis não são impressos em erro ou evidência.
- [ ] O ambiente publicado não recebe ataques de teste.
- [ ] Decisão e limites da proteção ficam documentados.

## 6. TL-02 — Demonstração que mantém seu valor

**Problema observado:** em 07/10 os registros apresentados eram de 28/09, com atraso acima de 215 horas e posição sem atualização por mais de 12 mil minutos. “Operação conectada” era verdadeiro para a conexão SignalR, mas não demonstrava dados sendo renovados.

**Causa operacional identificada:** o simulador em [infra-demo/docker-compose.demo.yml](infra-demo/docker-compose.demo.yml), aproximadamente linha 278, está no perfil roteiro, roda sob demanda e termina. [Program.cs](src/TorreLogistica.Simulator/Program.cs) executa uma encenação limitada; não é uma fonte contínua de atividade.

**Decisões existentes:** [ADR 0032](docs/adr/0032-roteiro-da-demonstracao.md) e [docs/operacao/simulador.md](docs/operacao/simulador.md). A simulação deve usar a API real e preservar eventos. “Reset” não é apagar tabelas históricas.

### Caminho recomendado

Concluir um roteiro curto, determinístico e reexecutável em um ambiente exclusivo de demo, com identificação da execução e datas coerentes. Separar visualmente três informações: conexão ativa, idade do último dado e situação da encenação.

Escolher entre encenação sob demanda controlada e manutenção periódica limitada. Não iniciar automaticamente uma simulação infinita. Registrar a escolha, limites e operação de renovação.

### Aceite

- [ ] Visitante entende se a demo está ao vivo, em execução ou mostrando um cenário histórico.
- [ ] Quando uma encenação está ativa, pelo menos uma posição e um estado mudam pelo caminho real.
- [ ] Existe forma documentada e segura de preparar uma nova demonstração.
- [ ] Roteiro repetido não falha por unicidade de entidades nem mistura execuções de maneira incompreensível.
- [ ] Alertas de atraso e offline são deliberados, em vez de todos os itens parecerem abandonados.
- [ ] Não existe UPDATE direto para fabricar geofence, atraso ou conclusão.
- [ ] Não são apagados eventos, auditoria ou dados externos para “limpar a demo”.
- [ ] Renovação tem limite de duração, frequência, volume e retenção; consumo cabe na infraestrutura.
- [ ] Se a publicação precisar de renovação, preparar tudo localmente e registrar a dependência da autorização externa.

Mudar apenas datas exibidas ou esconder atrasos não atende esta tarefa.

## 7. TL-03 — Mapa útil com ruas e contexto

**Problema observado:** a demo mostra “Sem provedor de mapa configurado: os pontos aparecem sobre fundo neutro”. O comportamento é previsto e não representa uma quebra do componente, mas reduz o principal diferencial visual.

**Arquivos:** [MapaDaOperacao.tsx](apps/operacao/src/paginas/MapaDaOperacao.tsx), [ambiente.ts](apps/operacao/src/infra/ambiente.ts), [infra-demo/.env.demo.example](infra-demo/.env.demo.example), [infra-demo/README.md](infra-demo/README.md) e configurações da borda.

Variáveis já existentes: VITE_URL_DO_ESTILO_DO_MAPA no frontend e TORRE_URL_DO_ESTILO_DO_MAPA na infraestrutura de demo.

### Execução

1. Procurar primeiro uma configuração aprovada e compatível com a infraestrutura existente.
2. Confirmar licença, atribuição, limites, privacidade e custo do estilo e dos tiles.
3. Não presumir que tiles públicos podem ser usados sem limite ou que um plano gratuito nunca gera cobrança.
4. Preparar a configuração local e testar carregamento e erro de provedor.
5. Preservar a alternativa sem mapa para indisponibilidade.
6. Se não houver solução sem risco de custo dentro das decisões atuais, entregar as opções concretas para aprovação e manter a tarefa pendente.

### Aceite

- [ ] Demonstração final usa mapa contextual legível com atribuição correta.
- [ ] Motorista e entregas se relacionam com os destinos e a situação operacional.
- [ ] Falha do provedor não impede listas, painel ou navegação.
- [ ] Posições públicas continuam aproximadas; o mapa não amplia dados que a API permite expor.
- [ ] CORS, CSP e certificados continuam restritos corretamente.
- [ ] Não são versionadas credenciais privadas de provedor.
- [ ] Não há contratação, conta paga ou mudança de DNS sem autorização aplicável.

Um screenshot bonito sozinho não comprova que o mapa publicado funciona.

## 8. TL-04 — Dependências e ambiente reproduzível

**Problema confirmado:** duas altas em brace-expansion e source-map-js, ambas na cadeia de desenvolvimento. Zero achados com --omit=dev. O Node local era 22.17.0, abaixo do mínimo >=22.22.0 declarado no workspace; a CI principal usa Node 24.

**Arquivos:** [package.json](package.json), [package-lock.json](package-lock.json), manifests de apps e [.github/workflows/ci.yml](.github/workflows/ci.yml).

### Aceite

- [ ] Usar Node compatível; preferir o mesmo major da CI principal para reproduzir seus checks.
- [ ] npm ci reproduz os três apps, sem erro de engine ou dependência inválida.
- [ ] Atualizar pacotes pais de maneira compatível; sem npm audit fix --force indiscriminado.
- [ ] npm audit --audit-level=high passa e o filtro sem dev continua sem alta/crítica.
- [ ] Lint, tipos, testes e builds dos três workspaces passam.
- [ ] Auditoria NuGet atual e varredura de segredos aprovadas.
- [ ] O job de contrato também usa uma versão que atende ao requisito do workspace.

Não reduzir o requisito do Node nem remover gates para acomodar o ambiente antigo.

## 9. TL-05 — Separar o mapa do carregamento inicial

**Problema confirmado:** o build do console gerou JavaScript principal de aproximadamente 1.496,81 KB, cerca de 416,34 KB comprimido. [App.tsx](apps/operacao/src/App.tsx) importa MapaDaOperacao antecipadamente; esse componente importa MapLibre.

### Execução e aceite

- [ ] Carregar a página do mapa sob demanda, incluindo a biblioteca pesada.
- [ ] Verificar quais páginas extras justificam a mesma divisão.
- [ ] Entrada/login e painel não transferem o código pesado do mapa antes de ser necessário.
- [ ] URL direta de /mapa e atualização da página funcionam.
- [ ] Há carregamento e recuperação de falha de chunk.
- [ ] Comparação antes/depois mostra redução do total de JavaScript inicial.
- [ ] Tamanho menor não foi obtido elevando o limite do aviso ou ocultando saída.
- [ ] Sessão, SignalR e estado dos filtros não sofrem regressão.

Medir com a mesma configuração de produção e considerar todos os chunks iniciais, não apenas um arquivo.

## 10. TL-06 — Provar os diferenciais difíceis

**Tipo:** verificação dirigida das garantias existentes, não lista de defeitos confirmados.

### Offline do motorista

Arquivos de partida: [fila.ts](apps/motorista/src/offline/fila.ts), [sincronizador.ts](apps/motorista/src/offline/sincronizador.ts), [guardadosNoAparelho.ts](apps/motorista/src/offline/guardadosNoAparelho.ts), [sw.test.ts](apps/motorista/src/sw.test.ts).

- [ ] Sem rede, ação prevista é enfileirada com seu identificador estável.
- [ ] Retomar rede aplica a operação sem duplicar evento ou efeito.
- [ ] Resposta perdida e retentativa não duplicam conclusão.
- [ ] Reiniciar navegador não perde a operação pendente.
- [ ] Trocar usuário ou sair não revela a fila de outro motorista.
- [ ] Conflito com cancelamento feito no servidor é mostrado com explicação; não é sobrescrito.
- [ ] Service worker não guarda respostas autenticadas da API indevidamente.
- [ ] GPS e câmera têm estados compreensíveis para permissão negada e indisponibilidade.

### Tempo real e console

- [ ] Posição enviada pelo caminho real aparece no console sem atualização manual.
- [ ] Reconexão recupera o estado e não duplica mensagens.
- [ ] Conexão de uma organização não recebe eventos de outra.
- [ ] Expiração/encerramento da sessão afeta o canal conforme o contrato.
- [ ] Indicador de conexão e idade dos dados são informações distintas.

### Rastreamento e comprovantes

Arquivos de partida: [RastreamentoPublicoTestes.cs](tests/TorreLogistica.IntegrationTests/RastreamentoPublicoTestes.cs) e os testes existentes de entrega/upload.

- [ ] Link público válido mostra somente a entrega permitida e posição aproximada.
- [ ] Link expirado, revogado ou inválido apresenta resposta segura e útil.
- [ ] Resposta pública não revela motorista, rota completa, endereço indevido ou comprovantes privados.
- [ ] URLs assinadas expiram e permanecem limitadas ao objeto autorizado.
- [ ] Os cinco testes de contrato executam contra a API real; skip local não conta como aprovação.

Reusar os testes existentes; adicionar permanentes quando surgir uma lacuna comprovada ou quando o comportamento mudar. Registrar também uma verificação em navegador de pelo menos um percurso envolvendo as três interfaces. Em produção, apenas as operações especificamente autorizadas.

## 11. TL-07 — Documentação factual e segurança com escopo

**Problemas confirmados:** README ainda informa infraestrutura da demo “validada localmente, nada provisionado” apesar da publicação. Contagens de testes estão antigas. A promessa de uma operação que acontece sozinha não descreve o simulador sob demanda observado.

**Arquivos:** [README.md](README.md), [ROADMAP.md](ROADMAP.md), [CLAUDE.md](CLAUDE.md), [infra-demo/README.md](infra-demo/README.md), [docs/operacao/simulador.md](docs/operacao/simulador.md), [docs/pentest-v1.md](docs/pentest-v1.md).

### Aceite

- [ ] Documentação distingue demo pública, produção proposta e infraestrutura local.
- [ ] Descreve quando o simulador roda, como repetir e quais limites existem.
- [ ] Documenta o modo do mapa e eventual dependência de provedor.
- [ ] Contagens e resultados têm data e execução correspondente.
- [ ] TL-01 ganha evidência de testes e descrição da política de destino.
- [ ] Pentest histórico não é anunciado como prova completa contra SSRF; o teste anterior de acesso a webhook não cobria as lacunas deste relatório.
- [ ] ADRs alteradas são atualizadas somente quando a decisão muda.
- [ ] README mostra primeiro problema, demo, fluxo e diferenciais; detalhes operacionais ficam vinculados.

## 12. TL-08 — Limites e desempenho comprovados

A VM demonstrativa tem recursos restritos e o mapa/simulador podem alterar consumo. Usar os limites e medições existentes em infra-demo; não adicionar uma arquitetura maior como primeira resposta.

### Aceite

- [ ] Registrar ambiente, volume, número de conexões e duração do teste.
- [ ] Medir memória/CPU da API, workers e simulador durante uma encenação limitada.
- [ ] Medir tempo entre envio de posição e atualização observada no console.
- [ ] Medir reconexão e entrega das ações offline no volume de demonstração.
- [ ] Conferir crescimento do disco e execução da retenção existente.
- [ ] Comparar antes/depois das alterações pertinentes, em condições equivalentes.
- [ ] Sem crescimento indefinido por renovar cenários e sem mudanças incompatíveis com eventos históricos.
- [ ] Se uma meta numérica for adotada, justificar pelo cenário e pela infraestrutura; não inventar um SLA universal a partir de um teste curto.

Não executar carga na infraestrutura publicada sem autorização específica. Logs e métricas de evidência devem omitir coordenadas privadas, tokens e comprovantes.

## 13. TL-09 — Roteiro, interface e evidências

### Roteiro de três a cinco minutos

1. Console: identificar uma entrega e o motivo do alerta.
2. Mapa: relacionar posição, destino e previsão.
3. Motorista: demonstrar uma operação prevista sem rede e sua sincronização em ambiente controlado.
4. Console: observar o efeito e a trilha.
5. Link público: mostrar a visão reduzida do destinatário.

### Aceite

- [ ] Capturas existentes em docs/assets/screenshots correspondem ao resultado final.
- [ ] Console, PWA e rastreamento verificados em 390, 768 e 1280 pixels conforme o uso.
- [ ] Principais ações e diálogos funcionam por teclado, com foco visível.
- [ ] Erro de rede, permissão, upload e mapa apresenta saída compreensível.
- [ ] Sem erro inesperado no console.
- [ ] Criar docs/evidencias-portfolio-95.md com commit, mudanças por ID, testes, contratos, auditorias, medição, capturas e limitações.
- [ ] Distinguir comprovação local e comprovação publicada após eventual deploy autorizado.

## 14. Ambiente e comandos

[global.json](global.json) exige SDK 10.0.400. Os testes usam Microsoft.Testing.Platform/xunit.v3; o caminho oficial do repositório é [scripts/testar.ps1](scripts/testar.ps1). Não considerar “zero testes” como aprovação.

Executar e inspecionar cada código de saída.

**Na raiz, durante a implementação autorizada:**

~~~powershell
git status --porcelain
git rev-parse HEAD
node --version
dotnet --list-sdks
docker info
npm ci
npm ls
npm run lint
npm run typecheck
npm run test
npm run build
npm audit --audit-level=high
npm audit --omit=dev
dotnet restore TorreLogistica.slnx
dotnet build TorreLogistica.slnx --configuration Release --no-restore -warnaserror
powershell -File scripts/testar.ps1
dotnet list TorreLogistica.slnx package --vulnerable --include-transitive --format json --output-version 1
~~~

O script resolve o SDK 10 e o runtime; usar TORRE_DOTNET se a instalação estiver fora do PATH. Conferir quantidade de testes e motivos de skip. A auditoria NuGet precisa de inspeção do JSON, mesmo com saída zero.

**Contrato real:** seguir o job contrato de [.github/workflows/ci.yml](.github/workflows/ci.yml). Ele prepara stack de demo local e usa TORRE_API_CONTRATO, TORRE_CONTRATO_ORIGEM, TORRE_CONTRATO_ORG, TORRE_CONTRATO_EMAIL e TORRE_CONTRATO_SENHA antes de executar:

~~~powershell
npm run test --workspace @torre/operacao -- contrato
~~~

Não copiar segredos do job para o documento. Reproduzir em ambiente isolado; o cleanup de CI destrói volumes desse ambiente e não deve ser usado contra dados publicados ou volumes pessoais.

Executar varredura de segredos com redação e registrar o resultado. Manter gates, assertions e integração real.

## 15. Reavaliação para 95+/100

A nota anterior foi julgamento técnico. Esta rubrica organiza a nova avaliação sem garantir nota automaticamente:

| Dimensão | Peso |
|---|---:|
| Correção, eventos e integridade | 25 |
| Segurança e privacidade | 20 |
| Testes e reprodução | 20 |
| Arquitetura e manutenção | 15 |
| Demo, experiência móvel e desempenho | 10 |
| Documentação e evidências | 10 |
| Total | 100 |

Uma distribuição como 24 + 19 + 19 + 14 + 10 + 9 chega a 95; a avaliação precisa justificar os pontos.

- [ ] TL-01 a TL-09 concluídos ou substituídos por solução equivalente comprovada.
- [ ] Destino interno e troca de resolução DNS não contornam a proteção de webhooks.
- [ ] Nenhum defeito importante confirmado de segurança, integridade ou privacidade permanece aberto.
- [ ] Instalação limpa e suítes de backend/frontend aprovadas.
- [ ] Contratos reais executados; skips não escondem as garantias principais.
- [ ] Dependências altas/críticas resolvidas sem desativar gates.
- [ ] Demonstração recente ou claramente reproduzível, mapa útil e indicador de idade dos dados.
- [ ] Offline, reconexão e privacidade demonstrados com evidência.
- [ ] Consumo permanece compatível com o ambiente e não exige custo oculto.
- [ ] Documentação corresponde ao resultado.
- [ ] Demo publicada verificada após eventual implantação autorizada.
- [ ] Nova nota fundamentada e pendências explícitas.

## 16. Melhorias opcionais

Não são exigidas para atingir a meta isoladamente: mapa com efeitos sofisticados, novos provedores, aplicativo nativo, rastreamento em segundo plano não previsto, mensageria adicional, microserviços, IA ou novos dashboards sem pergunta operacional.

O funcionamento offline e o mapa contextual existentes merecem prioridade sobre novas funcionalidades.

## 17. Mensagem sugerida para o outro chat

> Trabalhe na Torre Logística. Leia AUDITORIA_PORTFOLIO_95.md e a governança. Autorizo implementar os itens obrigatórios do plano, começando pelos webhooks. Preserve eventos, offline, privacidade, arquitetura e limites de custo. Explique cada grupo de mudanças de forma simples antes de editar. Execute testes com PostgreSQL/PostGIS real e contratos da API, prepare uma demonstração fiel e registre evidências por ID. Não faça push, deploy, alteração destrutiva em dados publicados, DNS ou contratação sem a autorização aplicável. Ao terminar, reavalie a nota com base nas provas; não declare 95+/100 com tarefas obrigatórias pendentes.

