import '@testing-library/jest-dom/vitest';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach } from 'vitest';

// IndexedDB em memória, novo a cada teste: um teste nunca herda a fila ou as cópias de outro. Dentro do
// mesmo teste ele persiste, e é isso que permite simular fechar e reabrir o aplicativo.
beforeEach(() => {
  globalThis.indexedDB = new IDBFactory();
});
