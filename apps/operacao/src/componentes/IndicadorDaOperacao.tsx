import { useQuery } from '@tanstack/react-query';

import { obterPulso } from '../infra/api';

/** Até aqui, o dado é recente o bastante para a tela se dizer ao vivo. */
const LIMITE_AO_VIVO_EM_MS = 2 * 60 * 1000;

/** Daqui para cima, a encenação está claramente encerrada e o dado é histórico. */
const LIMITE_RECENTE_EM_MS = 30 * 60 * 1000;

type Situacao = 'aoVivo' | 'recente' | 'encerrada' | 'semOperacao';

const aparencia: Record<Situacao, { classe: string; texto: (idade: string) => string }> = {
  aoVivo: { classe: 'pronta', texto: (idade) => `Operação ao vivo · dado de ${idade}` },
  recente: { classe: 'indisponivel', texto: (idade) => `Último dado ${idade}` },
  encerrada: { classe: 'historica', texto: (idade) => `Encenação encerrada · último dado ${idade}` },
  semOperacao: { classe: 'historica', texto: () => 'Nenhuma operação em curso' },
};

/**
 * Mostra a IDADE do dado mais recente, separada do estado da conexão.
 *
 * A luz da conexão responde "a API está me respondendo?"; esta responde "há operação acontecendo agora?".
 * São perguntas diferentes: numa demonstração sob demanda a encenação termina e os dados envelhecem com a
 * API no ar. Sem separar as duas, o verde da conexão faria uma encenação encerrada parecer ao vivo — que
 * foi exatamente o que a auditoria apontou.
 *
 * A idade é medida contra o relógio do SERVIDOR (`agora` vem na mesma resposta): comparar dois instantes do
 * mesmo relógio evita que o fuso ou o atraso do navegador do visitante invente envelhecimento.
 */
export function IndicadorDaOperacao() {
  const consulta = useQuery({
    queryKey: ['pulso'],
    queryFn: ({ signal }) => obterPulso(signal),
    // O dado envelhece sozinho; reconsultar de tempos em tempos mantém a idade honesta sem recarregar a tela.
    refetchInterval: 20_000,
  });

  // Enquanto não há resposta — ou se a consulta falha — quem fala é o indicador de conexão ao lado.
  if (!consulta.data) {
    return null;
  }

  const pulso = consulta.data;
  const situacao = classificar(pulso.agora, pulso.ultimaPosicaoRecebidaEm);
  const idade = pulso.ultimaPosicaoRecebidaEm === null
    ? ''
    : descreverIdade(Date.parse(pulso.agora) - Date.parse(pulso.ultimaPosicaoRecebidaEm));

  const { classe, texto } = aparencia[situacao];

  return (
    <p className={`conexao conexao--${classe}`} role="status" aria-live="polite">
      <span className="conexao__marca" aria-hidden="true" />
      {texto(idade)}
    </p>
  );
}

function classificar(agora: string, ultima: string | null): Situacao {
  if (ultima === null) {
    return 'semOperacao';
  }

  const idadeEmMs = Date.parse(agora) - Date.parse(ultima);

  if (idadeEmMs < LIMITE_AO_VIVO_EM_MS) {
    return 'aoVivo';
  }

  return idadeEmMs < LIMITE_RECENTE_EM_MS ? 'recente' : 'encerrada';
}

/** "há 12 s", "há 3 min", "há 2 h", "há 5 dias" — a partir de um intervalo em milissegundos. */
function descreverIdade(ms: number): string {
  const segundos = Math.max(0, Math.round(ms / 1000));

  if (segundos < 60) {
    return `há ${segundos} s`;
  }

  const minutos = Math.round(segundos / 60);
  if (minutos < 60) {
    return `há ${minutos} min`;
  }

  const horas = Math.round(minutos / 60);
  if (horas < 24) {
    return `há ${horas} h`;
  }

  const dias = Math.round(horas / 24);
  return `há ${dias} ${dias === 1 ? 'dia' : 'dias'}`;
}
