/**
 * UUIDv7 gerado no aparelho (ADR 0004): 48 bits de milissegundos, versão, variante e aleatório.
 *
 * O identificador de uma posição nasce antes do envio. É ele que torna o reenvio seguro: a mesma posição
 * mandada duas vezes é reconhecida pelo servidor como duplicada, e não gravada de novo.
 */
export function uuidv7(
  agoraEmMs: number = Date.now(),
  aleatorio: (tamanho: number) => Uint8Array = (tamanho) => crypto.getRandomValues(new Uint8Array(tamanho)),
): string {
  const bytes = aleatorio(16);

  for (let indice = 0; indice < 6; indice++) {
    bytes[indice] = Math.floor(agoraEmMs / 2 ** (8 * (5 - indice))) % 256;
  }

  bytes[6] = ((bytes[6] ?? 0) & 0x0f) | 0x70;
  bytes[8] = ((bytes[8] ?? 0) & 0x3f) | 0x80;

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
