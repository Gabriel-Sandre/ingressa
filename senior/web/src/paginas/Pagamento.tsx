import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { get, novaChave, post, postIdempotente } from '../api/cliente'
import type { Pedido } from '../api/tipos'
import { Aviso, Carregando } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { cronometro, dataHora, moeda } from '../util/formato'

/**
 * Tela de pagamento com gateway SIMULADO. Num gateway real, os dados do cartão
 * seriam digitados num campo fornecido por ele, e a aplicação receberia só um token.
 */
const OPCOES = [
  { token: 'tok_aprovado', rotulo: 'Cartão de teste — aprovado' },
  { token: 'tok_recusado', rotulo: 'Cartão de teste — recusado pelo banco' },
  { token: 'tok_sem_saldo', rotulo: 'Cartão de teste — saldo insuficiente' },
]

export function Pagamento() {
  const { id } = useParams()
  const navegar = useNavigate()
  const { dados: pedido, erro, carregando } = useCarga(() => get<Pedido>(`/api/pedidos/${id}`), [id])
  const [token, setToken] = useState(OPCOES[0].token)
  const [falha, setFalha] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [agora, setAgora] = useState(() => Date.now())
  // Mesma chave enquanto a operação não mudar: um clique duplo não gera duas cobranças.
  const [chave, setChave] = useState(novaChave)

  useEffect(() => {
    const t = window.setInterval(() => setAgora(Date.now()), 1000)
    return () => window.clearInterval(t)
  }, [])

  if (carregando) return <Carregando />
  if (!pedido) return <Aviso>{erro}</Aviso>

  if (pedido.status !== 'AguardandoPagamento') {
    return (
      <Aviso tipo="info">
        Este pedido não está aguardando pagamento. <Link to="/pedidos">Ver meus pedidos</Link>
      </Aviso>
    )
  }

  const restante = new Date(pedido.expiraEm).getTime() - agora
  const expirou = restante <= 0

  const pagar = async (e: FormEvent) => {
    e.preventDefault()
    setFalha(null)
    setEnviando(true)
    try {
      await postIdempotente<Pedido>(`/api/pedidos/${pedido.id}/pagamento`, { tokenDePagamento: token }, chave)
      navegar(`/pedidos?pago=${pedido.id}`)
    } catch (err) {
      setFalha((err as Error).message)
      setChave(novaChave())
      setEnviando(false)
    }
  }

  const desistir = async () => {
    setEnviando(true)
    try {
      await post(`/api/pedidos/${pedido.id}/cancelar`)
      navegar('/pedidos')
    } catch (err) {
      setFalha((err as Error).message)
      setEnviando(false)
    }
  }

  return (
    <>
      <h1>Pagamento</h1>
      <div className="cartao">
        <h2>{pedido.evento}</h2>
        <p className="meta">{dataHora(pedido.dataEvento)}</p>
        <ul className="lista">
          {pedido.itens.map((i) => (
            <li key={i.setorId}>
              {i.quantidade}× {i.setor} — {moeda(i.subtotal)}
            </li>
          ))}
        </ul>
        <p className="preco">Total: {moeda(pedido.total)}</p>
        <p className={`cronometro ${restante < 60_000 ? 'urgente' : ''}`} aria-live="polite">
          {expirou ? 'Reserva expirada' : `Reserva garantida por ${cronometro(restante)}`}
        </p>
      </div>

      <form className="formulario" onSubmit={pagar}>
        <fieldset disabled={enviando || expirou}>
          <legend>Forma de pagamento (ambiente de demonstração)</legend>
          {OPCOES.map((o) => (
            <label key={o.token} className="opcao">
              <input type="radio" name="token" value={o.token} checked={token === o.token} onChange={() => {
                  setToken(o.token)
                  setChave(novaChave())
                }} />
              {o.rotulo}
            </label>
          ))}
        </fieldset>
        <Aviso>{falha}</Aviso>
        <Aviso tipo="info">{expirou ? 'O prazo terminou e os lugares foram liberados. Faça um novo pedido.' : null}</Aviso>
        <div className="acoes">
          <button className="primario" disabled={enviando || expirou}>
            {enviando ? 'Processando…' : `Pagar ${moeda(pedido.total)}`}
          </button>
          <button type="button" onClick={desistir} disabled={enviando || expirou}>
            Desistir
          </button>
        </div>
      </form>
    </>
  )
}
