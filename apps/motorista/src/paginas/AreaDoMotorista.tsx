import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Outlet } from 'react-router';

import { EstadoDaSincronizacao } from '../componentes/EstadoDaSincronizacao';
import { EstadoDoGps } from '../componentes/EstadoDoGps';
import { useRastreamento } from '../gps/useRastreamento';
import { aguardandoEnvio } from '../offline/fila';
import { ProvedorDeSincronizacao, useRotas, useSincronizacao } from '../offline/ProvedorDeSincronizacao';
import { useSessao } from '../sessao/ProvedorDeSessao';

/**
 * Moldura das telas com sessão: quem está no aplicativo, sair, o estado da localização e o da fila de ações.
 * A localização só é coletada com uma rota em andamento.
 */
export function AreaDoMotorista() {
  const { estado } = useSessao();

  if (estado.situacao !== 'autenticada') {
    return null;
  }

  return (
    <ProvedorDeSincronizacao usuarioId={estado.motorista.id}>
      <Moldura nome={estado.motorista.nome} />
    </ProvedorDeSincronizacao>
  );
}

function Moldura({ nome }: { readonly nome: string }) {
  const { sair } = useSessao();
  const { usuarioId, operacoes } = useSincronizacao();
  const cliente = useQueryClient();
  const [saindo, setSaindo] = useState(false);
  const [confirmandoSaida, setConfirmandoSaida] = useState(false);

  const rotas = useRotas();
  const emRota = rotas.dados?.some((rota) => rota.status === 'EmAndamento') ?? false;
  const gps = useRastreamento(emRota, usuarioId);
  const naoEnviadas = operacoes.filter(aguardandoEnvio).length;

  async function encerrar() {
    setSaindo(true);
    try {
      await sair();
    } finally {
      // Nada da rota do motorista fica em cache para a próxima pessoa que pegar o aparelho.
      cliente.clear();
      setSaindo(false);
    }
  }

  return (
    <>
      <div className="motorista">
        <span>{nome}</span>
        <button
          type="button"
          className="botao-discreto"
          disabled={saindo}
          onClick={() => {
            if (naoEnviadas > 0 && !confirmandoSaida) {
              setConfirmandoSaida(true);
              return;
            }
            void encerrar();
          }}
        >
          {saindo ? 'Saindo…' : confirmandoSaida ? 'Sair mesmo assim' : 'Sair'}
        </button>
      </div>

      {confirmandoSaida && naoEnviadas > 0 ? (
        <p className="gps gps--falhando" role="alert">
          {naoEnviadas === 1 ? 'Há 1 ação ainda não enviada.' : `Há ${naoEnviadas} ações ainda não enviadas.`}
          <span className="gps__dica">Elas ficam guardadas neste aparelho e seguem quando você entrar de novo.</span>
        </p>
      ) : null}

      <EstadoDoGps estado={gps} />
      <EstadoDaSincronizacao />

      <Outlet />
    </>
  );
}
