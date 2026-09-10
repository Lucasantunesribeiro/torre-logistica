import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';

import { App } from './App';
// Importado só pelo efeito: a validação roda na importação e derruba a aplicação
// agora, com mensagem clara, se a build foi publicada com VITE_URL_DA_API inválida.
// A página pública ainda não chama a API — isso acontece na Fase 15 — mas publicar
// com configuração quebrada e descobrir depois é pior do que falhar aqui.
import './infra/ambiente';
import './estilos.css';

const raiz = document.getElementById('raiz');

if (!raiz) {
  throw new Error('Elemento #raiz não encontrado no index.html.');
}

// Sem QueryClientProvider: esta aplicação não tem estado de servidor nesta fase, e
// provider sem consumidor é peça decorativa.
createRoot(raiz).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
