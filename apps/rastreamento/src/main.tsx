import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';

import { App } from './App';
// Importado só pelo efeito: a validação roda na importação e derruba a aplicação
// agora, com mensagem clara, se a build foi publicada com VITE_URL_DA_API inválida.
import './infra/ambiente';
import './estilos.css';

const raiz = document.getElementById('raiz');

if (!raiz) {
  throw new Error('Elemento #raiz não encontrado no index.html.');
}

// Sem QueryClientProvider: a página faz uma consulta só, sem cache entre telas, sem
// invalidação e sem nada para sincronizar — uma dependência a mais no pacote que o
// destinatário baixa não se paga por isso.
createRoot(raiz).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
