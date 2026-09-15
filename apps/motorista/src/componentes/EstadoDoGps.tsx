import type { EstadoDoRastreador } from '../gps/rastreador';
import { formatarHora } from '../infra/formatos';

const DICA_DE_SEGUNDO_PLANO =
  'Mantenha o aplicativo aberto durante a rota: com a tela bloqueada, o navegador pode parar de enviar a localização.';

/** O que o motorista precisa saber sobre a localização, sem prometer o que o navegador não garante. */
export function EstadoDoGps({ estado }: { readonly estado: EstadoDoRastreador }) {
  switch (estado.gps) {
    case 'inativo':
      return null;

    case 'aguardando-permissao':
      return (
        <p className="gps" role="status">
          Aguardando a permissão de localização…
          <span className="gps__dica">Toque em “Permitir” quando o navegador perguntar.</span>
        </p>
      );

    case 'negado':
      return (
        <p className="gps gps--negado" role="alert">
          Localização bloqueada: a operação não vê onde você está.
          <span className="gps__dica">
            Libere a localização deste site nas configurações do navegador. As entregas continuam funcionando.
          </span>
        </p>
      );

    case 'indisponivel':
      return (
        <p className="gps gps--indisponivel" role="alert">
          Este aparelho ou navegador não informa localização.
          <span className="gps__dica">As entregas continuam funcionando; avise a operação.</span>
        </p>
      );

    case 'falhando':
      return (
        <p className="gps gps--falhando" role="status">
          Sem sinal de localização agora. Tentando de novo…
          <span className="gps__dica">{DICA_DE_SEGUNDO_PLANO}</span>
        </p>
      );

    case 'ativo':
      return (
        <p className="gps" role="status">
          Localização ativa
          {estado.ultimoEnvioEm !== null ? ` · enviada às ${formatarHora(new Date(estado.ultimoEnvioEm).toISOString())}` : ''}
          {estado.falhaNoEnvio && estado.pendentes > 0 ? ` · ${estado.pendentes} aguardando conexão` : ''}
          <span className="gps__dica">{DICA_DE_SEGUNDO_PLANO}</span>
        </p>
      );
  }
}
