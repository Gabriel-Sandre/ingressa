import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { get, post } from '../api/cliente'
import type { EventoDetalhe, PosicaoNaFila } from '../api/tipos'
import { Aviso, Carregando } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { guardarPasse } from '../sessao/passes'
import { dataHora } from '../util/formato'

const INTERVALO_MS = 3000

/** Sala de espera: entra na fila e consulta a posição até ser liberado. */
export function SalaDeEspera() {
  const { id } = useParams()
  const eventoId = Number(id)
  const navegar = useNavigate()
  const { dados: evento } = useCarga(() => get<EventoDetalhe>(`/api/eventos/${eventoId}`), [eventoId])
  const [posicao, setPosicao] = useState<PosicaoNaFila | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [primeiraPosicao, setPrimeiraPosicao] = useState<number | null>(null)

  useEffect(() => {
    let ativo = true
    let temporizador = 0

    const atualizar = async (entrar: boolean) => {
      try {
        const p = entrar
          ? await post<PosicaoNaFila>(`/api/eventos/${eventoId}/fila`)
          : await get<PosicaoNaFila>(`/api/eventos/${eventoId}/fila`)
        if (!ativo) return
        if (p.situacao === 'Liberado' && p.passe) {
          guardarPasse(eventoId, p.passe)
          navegar(`/eventos/${eventoId}`, { replace: true })
          return
        }
        if (p.situacao === 'Fora') {
          // O passe venceu sem compra: volta para a fila.
          temporizador = window.setTimeout(() => atualizar(true), 0)
          return
        }
        if (p.situacao === 'Comprando') {
          // Recarregou a página depois de reservar: o passe já foi usado e a vaga continua
          // reservada. Voltar para a fila aqui mandaria o comprador para o fim dela.
          setPosicao(p)
          return
        }
        setPrimeiraPosicao((atual) => atual ?? p.posicao)
        setPosicao(p)
        temporizador = window.setTimeout(() => atualizar(false), INTERVALO_MS)
      } catch (e) {
        if (!ativo) return
        setErro((e as Error).message)
        temporizador = window.setTimeout(() => atualizar(false), INTERVALO_MS * 2)
      }
    }

    void atualizar(true)
    return () => {
      ativo = false
      window.clearTimeout(temporizador)
    }
  }, [eventoId, navegar])

  const inicial = primeiraPosicao ?? 1
  const progresso = posicao ? Math.round(100 * (1 - (posicao.posicao - 1) / Math.max(inicial, 1))) : 0

  return (
    <>
      <p>
        <Link to="/">← Voltar</Link>
      </p>
      <h1>Sala de espera</h1>
      {evento && (
        <p className="meta">
          {evento.titulo} · {dataHora(evento.dataInicio)}
        </p>
      )}
      <div className="cartao espera">
        {!posicao && !erro && <Carregando />}
        {posicao?.situacao === 'Comprando' && (
          <>
            <p className="posicao">Sua vez já chegou</p>
            <p className="meta">
              A compra deste evento está em andamento. Continue por <Link to={`/eventos/${eventoId}`}>esta página</Link>{' '}
              ou veja o que já foi reservado em <Link to="/pedidos">Meus pedidos</Link>.
            </p>
          </>
        )}
        {posicao?.situacao === 'Aguardando' && (
          <>
            <p className="posicao" aria-live="polite">
              Você é o <strong>{posicao.posicao}º</strong> da fila
            </p>
            <progress className="barra" value={progresso} max={100} aria-label="Andamento da fila" />
            <p className="meta">
              Não feche esta página. Quando chegar a sua vez, você será levado para a compra e terá alguns minutos para
              escolher os ingressos.
            </p>
          </>
        )}
        <Aviso>{erro}</Aviso>
      </div>
    </>
  )
}
