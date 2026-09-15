import { describe, expect, it } from 'vitest';

import { uuidv7 } from './uuidv7';

describe('UUIDv7 do aparelho', () => {
  it('tem versão 7, variante RFC e o instante nos primeiros 48 bits', () => {
    const id = uuidv7(0x0191_1234_5678, (tamanho) => new Uint8Array(tamanho).fill(0xff));

    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(id.startsWith('01911234-5678-7')).toBe(true);
  });

  it('ordena pelo instante de criação', () => {
    const antes = uuidv7(1_726_400_000_000);
    const depois = uuidv7(1_726_400_000_001);

    expect([depois, antes].sort()).toEqual([antes, depois]);
    expect(uuidv7()).not.toBe(uuidv7());
  });
});
