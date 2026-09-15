import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Outlet } from 'react-router';

import { EstadoDoGps } from '../componentes/EstadoDoGps';
import { useRastreamento } from '../gps/useRastreamento';
import { apiDoMotorista } from '../infra/api';
import { useSessao } from '../sessao/ProvedorDeSessao';

/**
 * Moldura das telas com sessão: quem está no aplicativo, sair e o estado da localização. A localização só é
 * coletada com uma rota em andamento.
 */
export function AreaDoMotorista() {
  const { estado, sair } = useSessao();
  const cliente = useQueryClient();
  const [saindo, setSaindo] = useState(false);

  const rotas = useQuery({ queryKey: ['rotas'], queryFn: apiDoMotorista.listarRotas });
  const emRota = rotas.data?.some((rota) => rota.status === 'EmAndamento') ?? false;
  const gps = useRastreamento(emRota);

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
        <span>{estado.situacao === 'autenticada' ? estado.motorista.nome : ''}</span>
        <button
          type="button"
          className="botao-discreto"
          disabled={saindo}
          onClick={() => {
            void encerrar();
          }}
        >
          {saindo ? 'Saindo…' : 'Sair'}
        </button>
      </div>

      <EstadoDoGps estado={gps} />

      <Outlet />
    </>
  );
}
