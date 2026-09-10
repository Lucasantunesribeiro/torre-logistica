import { describe, expect, it } from 'vitest';

import { lerAmbiente } from './ambiente';

describe('lerAmbiente', () => {
  it.each(['http://localhost:5080', 'https://api.torre.test', 'https://api.torre.test:8443/base'])(
    'aceita o endereço absoluto %s',
    (valor) => {
      expect(lerAmbiente({ VITE_URL_DA_API: valor }).urlDaApi).toBe(valor.replace(/\/+$/, ''));
    },
  );

  it('remove a barra final para não gerar caminho com barra dupla', () => {
    expect(lerAmbiente({ VITE_URL_DA_API: 'https://api.torre.test/' }).urlDaApi).toBe(
      'https://api.torre.test',
    );
  });

  /**
   * `localhost:5080` é uma URL tecnicamente válida — o analisador lê `localhost:`
   * como esquema. Sem a checagem de protocolo, o valor passaria e a aplicação
   * tentaria falar com um endereço que não existe.
   */
  it.each([undefined, '', 'localhost:5080', '/api', 'api.torre.test', 'ftp://api.torre.test'])(
    'recusa o valor %p em vez de construir um endereço quebrado',
    (valor) => {
      expect(() => lerAmbiente({ VITE_URL_DA_API: valor })).toThrow(
        /Configuração de ambiente inválida/,
      );
    },
  );
});
