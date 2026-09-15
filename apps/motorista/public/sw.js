/*
 * Service worker da PWA do motorista.
 *
 * Só guarda a casca do aplicativo — HTML, scripts, estilos, manifesto e ícone — para ele abrir sem
 * internet. Nunca intercepta a API: dados e ações passam pela fila e pelas cópias em IndexedDB, que sabem
 * de sessão, de idempotência e de conflito. Não usa Background Sync; o envio é do próprio aplicativo.
 *
 * - Navegação: rede primeiro (versão nova do aplicativo chega assim que há conexão); sem rede, a casca guardada.
 * - Recursos com hash no nome (/assets/): cache primeiro; o nome muda a cada versão.
 */

const VERSAO = 'torre-motorista-v1';
const CASCA = ['/', '/manifest.webmanifest', '/icone.svg'];

self.addEventListener('install', (evento) => {
  evento.waitUntil(
    (async () => {
      const cache = await caches.open(VERSAO);
      await cache.addAll(CASCA);

      // Os recursos da versão atual estão referenciados no HTML: guardar agora, e não só na segunda visita.
      const pagina = await cache.match('/');
      const html = pagina ? await pagina.text() : '';
      const recursos = [...html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)].map((achado) => achado[1]);
      await cache.addAll(recursos);

      await self.skipWaiting();
    })(),
  );
});

self.addEventListener('activate', (evento) => {
  evento.waitUntil(
    (async () => {
      for (const nome of await caches.keys()) {
        if (nome !== VERSAO) {
          await caches.delete(nome);
        }
      }
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('fetch', (evento) => {
  const pedido = evento.request;
  if (pedido.method !== 'GET') {
    return;
  }

  const url = new URL(pedido.url);
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/')) {
    return;
  }

  if (pedido.mode === 'navigate') {
    evento.respondWith(navegar(pedido));
    return;
  }

  if (url.pathname.startsWith('/assets/') || CASCA.includes(url.pathname)) {
    evento.respondWith(recurso(pedido));
  }
});

async function navegar(pedido) {
  try {
    const resposta = await fetch(pedido);
    if (resposta.ok) {
      const cache = await caches.open(VERSAO);
      // Toda rota do aplicativo é a mesma página: guardada como a casca.
      await cache.put('/', resposta.clone());
    }
    return resposta;
  } catch {
    return (await caches.match('/')) ?? Response.error();
  }
}

async function recurso(pedido) {
  const guardado = await caches.match(pedido);
  if (guardado) {
    return guardado;
  }

  const resposta = await fetch(pedido);
  if (resposta.ok) {
    const cache = await caches.open(VERSAO);
    await cache.put(pedido, resposta.clone());
  }
  return resposta;
}
