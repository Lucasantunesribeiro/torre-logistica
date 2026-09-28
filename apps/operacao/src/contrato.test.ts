import { beforeAll, describe, expect, it } from 'vitest';

import { esquemasDeContrato } from './infra/api';

// Este teste roda em Node (vitest), não no navegador, então lê variáveis de ambiente. O app não
// depende de @types/node — declarar só o que se usa evita puxar a dependência inteira para o front.
declare const process: { readonly env: Record<string, string | undefined> };

/**
 * Teste de contrato consumidor: valida os esquemas Zod do console contra a API REAL.
 *
 * Por que existe: o console quebrou em produção porque os esquemas do frontend divergiram do
 * backend (o enum de severidade e a forma da Rota), e os testes de unidade não pegaram — os mocks
 * repetiam o contrato errado do próprio frontend. Mock nunca detecta drift; só a API real detecta.
 *
 * Como roda: precisa de uma API viva. Defina as variáveis e aponte para a stack levantada —
 * localmente ou num job de CI que sobe o docker-compose de demonstração:
 *
 *   TORRE_API_CONTRATO=http://localhost \
 *   TORRE_CONTRATO_ORG=transportadora-aurora \
 *   TORRE_CONTRATO_EMAIL=paula.siqueira@aurora.test \
 *   TORRE_CONTRATO_SENHA=... \
 *   npm run test --workspace @torre/operacao -- contrato
 *
 * Sem `TORRE_API_CONTRATO`, o teste é PULADO (não falha o `npm test` do dia a dia). O que ele nunca
 * faz é passar em silêncio por falta de API: quando a variável está setada, uma API inacessível é
 * falha, não skip.
 */
const base = process.env.TORRE_API_CONTRATO?.replace(/\/+$/, '');
const org = process.env.TORRE_CONTRATO_ORG ?? 'transportadora-aurora';
const email = process.env.TORRE_CONTRATO_EMAIL ?? 'paula.siqueira@aurora.test';
const senha = process.env.TORRE_CONTRATO_SENHA ?? '';

// A origem precisa ser uma que o CORS/CSRF da API aceite. Local: console.localhost; produção: o
// domínio real do console. Parametrizada para o teste servir aos dois ambientes.
const origem = process.env.TORRE_CONTRATO_ORIGEM ?? 'http://console.localhost';

describe.skipIf(!base)('contrato do console contra a API real', () => {
  // Uma autenticação para todos os casos: um login por caso dispara o rate limiter da própria API.
  let token = '';

  beforeAll(async () => {
    const resposta = await fetch(`${base}/api/autenticacao/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Origin: origem },
      body: JSON.stringify({ organizacao: org, email, senha }),
    });
    if (!resposta.ok) {
      throw new Error(`Login de contrato falhou: HTTP ${resposta.status}. Confira TORRE_CONTRATO_*.`);
    }
    const corpo = (await resposta.json()) as { tokenDeAcesso?: string };
    if (typeof corpo.tokenDeAcesso !== 'string') {
      throw new Error('Login de contrato não devolveu tokenDeAcesso.');
    }
    token = corpo.tokenDeAcesso;
  });

  it.each(Object.entries(esquemasDeContrato))(
    'a resposta real de %s satisfaz o esquema Zod do console',
    async (caminho, esquema) => {
      const resposta = await fetch(`${base}${caminho}`, {
        headers: { Authorization: `Bearer ${token}`, Origin: origem },
      });
      expect(resposta.status, `GET ${caminho} deveria responder 200`).toBe(200);

      const analisado = esquema.safeParse(await resposta.json());
      if (!analisado.success) {
        // A mensagem lista o campo divergente — é o que o desenvolvedor precisa ver no CI.
        throw new Error(`Contrato divergente em ${caminho}:\n${JSON.stringify(analisado.error.issues, null, 2)}`);
      }
      expect(analisado.success).toBe(true);
    },
  );
});
