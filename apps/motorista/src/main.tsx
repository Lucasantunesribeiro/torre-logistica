import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router';

import { App } from './App';
import { criarClienteDeConsultas } from './infra/consultas';
import { ProvedorDeSessao } from './sessao/ProvedorDeSessao';
import './estilos.css';

const raiz = document.getElementById('raiz');

if (!raiz) {
  throw new Error('Elemento #raiz não encontrado no index.html.');
}

// Só no build: em desenvolvimento o cache da casca esconderia cada alteração do código.
if (import.meta.env.PROD && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Sem service worker o aplicativo funciona igual com internet; só não abre sem ela.
    });
  });
}

createRoot(raiz).render(
  <StrictMode>
    <QueryClientProvider client={criarClienteDeConsultas()}>
      <BrowserRouter>
        <ProvedorDeSessao>
          <App />
        </ProvedorDeSessao>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
