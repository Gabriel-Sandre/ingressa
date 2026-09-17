import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { get, novaChave, postIdempotente } from '../api/cliente'
import type { EventoDetalhe as Detalhe, Pedido } from '../api/tipos'
import { Aviso, Carregando, Selo } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { useSessao } from '../sessao/contexto'
import { esquecerPasse, obterPasse } from '../sessao/passes'
import { dataHora, moeda, preco } from '../util/formato'

const LIMITE = 6

export function EventoDetalhe() {
  const { id } = useParams()
  const { usuario } = useSessao()
  const navegar = useNavigate()
  const { dados: evento, erro, carregando } = useCarga(() => get<Detalhe>(`/api/eventos/${id}`), [id])
  const [quantidades, setQuantidades] = useState<Record<number, number>>({})
  const [falha, setFalha] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Mesma chave enquanto o pedido não mudar: um clique duplo não cria dois pedidos.
  const [chave, setChave] = useState(novaChave)

  if (carregando) return <Carregando />
  if (!evento) return <Aviso>{erro}</Aviso>

  const itens = Object.entries(quantidades)
    .map(([setorId, quantidade]) => ({ setorId: Number(setorId), quantidade }))
    .filter((i) => i.quantidade > 0)
  const totalIngressos = itens.reduce((s, i) => s + i.quantidade, 0)
  const total = itens.reduce((s, i) => s + i.quantidade * (evento.setores.find((x) => x.id === i.setorId)?.preco ?? 0), 0)

  const passe = evento.filaVirtual ? obterPasse(evento.id) : undefined
  const precisaDaFila = evento.filaVirtual && !passe

  const reservar = async (e: FormEvent) => {
    e.preventDefault()
    setFalha(null)
    setEnviando(true)
    try {
      const pedido = await postIdempotente<Pedido>(
        '/api/pedidos',
        { eventoId: evento.id, itens },
        chave,
        passe ? { 'X-Passe-Fila': passe } : {},
      )
      if (passe) esquecerPasse(evento.id)
      navegar(`/pedidos/${pedido.id}/pagamento`)
    } catch (err) {
      setFalha((err as Error).message)
      setChave(novaChave())
      setEnviando(false)
    }
  }

  return (
    <>
      <p>
        <Link to="/">← Voltar</Link>
      </p>
      <h1>{evento.titulo}</h1>
      {!evento.publicado && <Selo tom="neutro">Rascunho — visível só para você</Selo>}
      <p className="meta">
        {dataHora(evento.dataInicio)} · {evento.local} · {evento.cidade}
      </p>
      <p>{evento.descricao}</p>

      <form onSubmit={reservar}>
        <table>
          <thead>
            <tr>
              <th>Setor</th>
              <th>Preço</th>
              <th>Disponíveis</th>
              <th>Quantidade</th>
            </tr>
          </thead>
          <tbody>
            {evento.setores.map((s) => (
              <tr key={s.id}>
                <td>{s.nome}</td>
                <td>{preco(s.preco)}</td>
                <td>{s.disponiveis > 0 ? s.disponiveis : <Selo>Esgotado</Selo>}</td>
                <td>
                  <input
                    type="number"
                    min={0}
                    max={Math.min(LIMITE, s.disponiveis)}
                    value={quantidades[s.id] ?? 0}
                    disabled={s.disponiveis === 0}
                    aria-label={`Quantidade para ${s.nome}`}
                    onChange={(e) => {
                      setQuantidades({ ...quantidades, [s.id]: Number(e.target.value) })
                      setChave(novaChave())
                    }}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <Aviso>{totalIngressos > LIMITE ? `Máximo de ${LIMITE} ingressos por pedido.` : null}</Aviso>
        <Aviso>{falha}</Aviso>

        {usuario?.perfil === 'Cliente' && precisaDaFila ? (
          <div className="acoes">
            <span className="meta">Evento de alta procura: a compra é feita por ordem de chegada.</span>
            <Link className="botao primario" to={`/eventos/${evento.id}/fila`}>
              Entrar na fila
            </Link>
          </div>
        ) : usuario?.perfil === 'Cliente' ? (
          <div className="acoes">
            <strong>Total: {moeda(total)}</strong>
            <button className="primario" disabled={enviando || totalIngressos === 0 || totalIngressos > LIMITE}>
              {enviando ? 'Reservando…' : 'Reservar ingressos'}
            </button>
          </div>
        ) : (
          <p className="meta">
            {usuario ? 'Apenas clientes podem comprar.' : <><Link to="/entrar">Entre</Link> para comprar.</>}
          </p>
        )}
        <p className="meta">Os ingressos ficam reservados por 10 minutos enquanto você paga.</p>
      </form>
    </>
  )
}
