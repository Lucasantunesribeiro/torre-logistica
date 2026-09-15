/*
 * Armazenamento do aparelho em IndexedDB, sem biblioteca: quatro lojas e meia dúzia de operações.
 *
 * - operacoes: a fila de ações do motorista (ClientOperationId), que precisa sobreviver a fechar o navegador;
 * - leituras: a última cópia da rota e das entregas, para abrir a tela sem internet;
 * - posicoes: posições ainda não enviadas;
 * - identidade: quem entrou neste aparelho (nome e organização; nunca token) e a saída ainda não confirmada.
 *
 * Toda gravação só termina quando a transação do IndexedDB confirma: a ação que o motorista vê como
 * "guardada" está de fato no disco.
 */

const NOME_DO_BANCO = 'torre-motorista';
const VERSAO_DO_BANCO = 1;

export type Loja = 'operacoes' | 'leituras' | 'posicoes' | 'identidade';

/** O navegador não oferece IndexedDB (modo privado restrito, política do aparelho). */
export class ArmazenamentoIndisponivel extends Error {
  constructor(causa?: unknown) {
    super('O armazenamento do aparelho não está disponível.', { cause: causa });
    this.name = 'ArmazenamentoIndisponivel';
  }
}

let abertura: Promise<IDBDatabase> | null = null;
let fabricaDaAbertura: IDBFactory | null = null;

function abrir(): Promise<IDBDatabase> {
  // A conexão aberta pertence a uma fábrica de IndexedDB; se o ambiente trocou de fábrica (os testes trocam a
  // cada caso), a conexão antiga não serve mais.
  if (typeof indexedDB !== 'undefined' && fabricaDaAbertura !== indexedDB) {
    abertura = null;
    fabricaDaAbertura = indexedDB;
  }

  abertura ??= new Promise<IDBDatabase>((resolver, rejeitar) => {
    if (typeof indexedDB === 'undefined') {
      rejeitar(new ArmazenamentoIndisponivel());
      return;
    }

    const pedido = indexedDB.open(NOME_DO_BANCO, VERSAO_DO_BANCO);

    pedido.onupgradeneeded = () => {
      const banco = pedido.result;
      banco.createObjectStore('operacoes', { keyPath: 'id' }).createIndex('usuarioId', 'usuarioId');
      banco.createObjectStore('leituras', { keyPath: 'chave' }).createIndex('usuarioId', 'usuarioId');
      banco.createObjectStore('posicoes', { keyPath: 'eventoDeLocalizacaoId' }).createIndex('usuarioId', 'usuarioId');
      banco.createObjectStore('identidade', { keyPath: 'chave' });
    };

    pedido.onsuccess = () => {
      const banco = pedido.result;
      // Outra aba com versão nova do aplicativo pede para atualizar o banco: esta libera e reabre depois.
      banco.onversionchange = () => {
        banco.close();
        abertura = null;
      };
      resolver(banco);
    };

    pedido.onerror = () => {
      abertura = null;
      rejeitar(new ArmazenamentoIndisponivel(pedido.error));
    };
  });

  return abertura;
}

async function transacao<T>(loja: Loja, modo: IDBTransactionMode, trabalho: (armazem: IDBObjectStore) => () => T): Promise<T> {
  const banco = await abrir();

  return new Promise<T>((resolver, rejeitar) => {
    const tx = banco.transaction(loja, modo);
    const resultado = trabalho(tx.objectStore(loja));
    tx.oncomplete = () => {
      resolver(resultado());
    };
    tx.onerror = () => {
      rejeitar(new ArmazenamentoIndisponivel(tx.error));
    };
    tx.onabort = () => {
      rejeitar(new ArmazenamentoIndisponivel(tx.error));
    };
  });
}

export function obter<T>(loja: Loja, chave: string): Promise<T | undefined> {
  return transacao(loja, 'readonly', (armazem) => {
    const pedido = armazem.get(chave);
    return () => pedido.result as T | undefined;
  });
}

export function gravarVarios<T>(loja: Loja, valores: readonly T[]): Promise<void> {
  return transacao(loja, 'readwrite', (armazem) => {
    for (const valor of valores) {
      armazem.put(valor);
    }
    return () => undefined;
  });
}

export function gravar<T>(loja: Loja, valor: T): Promise<void> {
  return gravarVarios(loja, [valor]);
}

export function apagarVarios(loja: Loja, chaves: readonly string[]): Promise<void> {
  return transacao(loja, 'readwrite', (armazem) => {
    for (const chave of chaves) {
      armazem.delete(chave);
    }
    return () => undefined;
  });
}

export function apagar(loja: Loja, chave: string): Promise<void> {
  return apagarVarios(loja, [chave]);
}

export function listarDoUsuario<T>(loja: Exclude<Loja, 'identidade'>, usuarioId: string): Promise<T[]> {
  return transacao(loja, 'readonly', (armazem) => {
    const pedido = armazem.index('usuarioId').getAll(usuarioId);
    return () => pedido.result as T[];
  });
}

export function limpar(loja: Loja): Promise<void> {
  return transacao(loja, 'readwrite', (armazem) => {
    armazem.clear();
    return () => undefined;
  });
}
