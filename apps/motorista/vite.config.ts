// `defineConfig` vem de vitest/config, e não de vite: é a versão que conhece a
// seção `test`. Importar a do vite deixaria a configuração de teste sem tipo.
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// A porta é fixa por aplicação porque a política de CORS da API lista origens
// exatas por ambiente (ver appsettings.Development.json da API). Porta que muda
// sozinha quebraria a liberação sem deixar pista.
export default defineConfig({
  plugins: [react()],

  // As três aplicações leem o mesmo `.env` da raiz do repositório: uma cópia por
  // aplicação divergiria na primeira vez que alguém mudasse só uma delas.
  envDir: '../..',

  server: {
    port: 5174,
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',

    // O teste não depende do `.env` da máquina. Sem isto, quem clonasse o
    // repositório veria a suíte falhar por configuração ausente, não por defeito.
    env: {
      VITE_URL_DA_API: 'http://api.teste.local',
    },

    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    restoreMocks: true,
  },
});
