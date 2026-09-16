import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { get, post } from '../api/cliente'
import type { Pedido } from '../api/tipos'
import { Aviso, Carregando, Selo } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { dataHora, moeda, rotuloStatus } from '../util/formato'

const tom = { Pago: 'ok', AguardandoPagamento: 'neutro', Expirado: 'perigo', Cancelado: 'perigo' } as const

export function MeusPedidos() {
  const [params] = useSearchParams()
  const pago = params.get('pago')
  const { dados: pedidos, erro, carregando, recarregar } = useCarga(() => get<Pedido[]>('/api/pedidos'), [])
  const [falha, setFalha] = useState<string | null>(null)

  // Os ingressos são emitidos em segundo plano: enquanto houver pedido pago sem ingressos, consulta de novo.
  const aguardandoEmissao = pedidos?.some((p) => p.status === 'Pago' && !p.ingressosEmitidos) ?? false
  useEffect(() => {
    if (!aguardandoEmissao) return
    const t = window.setTimeout(recarregar, 2000)
    return () => window.clearTimeout(t)
  }, [aguardandoEmissao, pedidos, recarregar])

  const cancelar = async (id: number) => {
    if (!window.confirm('Cancelar este pedido?')) return
    setFalha(null)
    try {
      await post(`/api/pedidos/${id}/cancelar`)
      recarregar()
    } catch (e) {
      setFalha((e as Error).message)
    }
  }

  if (carregando && !pedidos) return <Carregando />

  return (
    <>
      <h1>Meus pedidos</h1>
      <Aviso tipo="ok">{pago ? `Pagamento confirmado! Pedido nº ${pago}.` : null}</Aviso>
      <Aviso>{erro ?? falha}</Aviso>
      {pedidos?.length === 0 && (
        <p>
          Você ainda não tem pedidos. <Link to="/">Ver eventos</Link>
        </p>
      )}
      {pedidos?.map((p) => (
        <article className="cartao pedido" key={p.id}>
          <header>
            <h2>{p.evento}</h2>
            <Selo tom={tom[p.status]}>{rotuloStatus[p.status]}</Selo>
          </header>
          <p className="meta">
            Pedido nº {p.id} · {dataHora(p.criadoEm)} · evento em {dataHora(p.dataEvento)}
          </p>

          {p.ingressos.length > 0 ? (
            <table>
              <thead>
                <tr>
                  <th>Setor</th>
                  <th>Código do ingresso</th>
                  <th>Valor</th>
                </tr>
              </thead>
              <tbody>
                {p.ingressos.map((i) => (
                  <tr key={i.id}>
                    <td>{i.setor}</td>
                    <td className="codigo">{i.codigo}</td>
                    <td>{moeda(i.precoPago)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <ul className="lista">
              {p.itens.map((i) => (
                <li key={i.setorId}>
                  {i.quantidade}× {i.setor} — {moeda(i.subtotal)}
                </li>
              ))}
            </ul>
          )}
          {p.status === 'Pago' && !p.ingressosEmitidos && <p className="meta">Emitindo seus ingressos…</p>}

          <footer className="acoes">
            <strong>Total: {moeda(p.total)}</strong>
            {p.status === 'AguardandoPagamento' && (
              <Link className="botao primario" to={`/pedidos/${p.id}/pagamento`}>
                Pagar
              </Link>
            )}
            {(p.status === 'AguardandoPagamento' || p.status === 'Pago') && (
              <button type="button" onClick={() => cancelar(p.id)}>
                Cancelar
              </button>
            )}
          </footer>
        </article>
      ))}
    </>
  )
}
