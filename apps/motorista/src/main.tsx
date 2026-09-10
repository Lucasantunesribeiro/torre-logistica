import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router';

import { App } from './App';
import { criarClienteDeConsultas } from './infra/consultas';
import './estilos.css';

const raiz = document.getElementById('raiz');

if (!raiz) {
  throw new Error('Elemento #raiz não encontrado no index.html.');
}

createRoot(raiz).render(
  <StrictMode>
    <QueryClientProvider client={criarClienteDeConsultas()}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
