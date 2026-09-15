import { describe, expect, it } from 'vitest';

import html from '../index.html?raw';
import manifesto from '../public/manifest.webmanifest?raw';
import estilos from './estilos.css?raw';

/**
 * Responsividade verificável sem navegador: o jsdom não calcula layout, então o que se prova aqui são as
 * regras que tornam a tela usável no celular. A verificação visual em aparelho fica registrada no ROADMAP.
 */
describe('responsividade e instalação', () => {
  it('declara viewport de aparelho, cor de tema e manifesto', () => {
    expect(html).toContain('width=device-width, initial-scale=1');
    expect(html).toContain('rel="manifest"');
    expect(html).toContain('name="theme-color"');
  });

  it('ações da entrega ocupam a largura e têm alvo de toque grande', () => {
    const acao = /\.acao\s*\{([^}]*)\}/.exec(estilos)?.[1] ?? '';

    expect(acao).toMatch(/width:\s*100%/);
    expect(acao).toMatch(/min-height:\s*3\.5rem/);
    expect(/\.motivos label\s*\{[^}]*min-height:\s*3\.5rem/.test(estilos)).toBe(true);
  });

  it('não fixa largura em pixel nem esconde conteúdo que não cabe', () => {
    expect(estilos).not.toMatch(/(?:^|[;\s{])width:\s*\d+px/m);
    expect(estilos).not.toMatch(/overflow(?:-x)?:\s*hidden/);
    expect(estilos).toMatch(/font:\s*16px/);
  });

  it('manifesto abre em tela cheia a partir da rota do dia', () => {
    const dados = JSON.parse(manifesto) as { display: string; start_url: string; lang: string; icons: unknown[] };

    expect(dados).toMatchObject({ display: 'standalone', start_url: '/', lang: 'pt-BR' });
    expect(dados.icons.length).toBeGreaterThan(0);
  });
});
