import { useState } from 'react'
import { get, post } from '../api/cliente'
import type { MensagemComFalha, Usuario } from '../api/tipos'
import { Aviso, Carregando } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { dataHora } from '../util/formato'

export function Admin() {
  const { dados: pendentes, erro, carregando, recarregar } = useCarga(
    () => get<Usuario[]>('/api/admin/organizadores/pendentes'),
    [],
  )
  const [mensagem, setMensagem] = useState<string | null>(null)
  const [falha, setFalha] = useState<string | null>(null)

  const aprovar = async (u: Usuario) => {
    setFalha(null)
    try {
      await post(`/api/admin/organizadores/${u.id}/aprovar`)
      setMensagem(`${u.nome} aprovado.`)
      recarregar()
    } catch (e) {
      setFalha((e as Error).message)
    }
  }

  return (
    <>
      <h1>Organizadores aguardando aprovação</h1>
      <Aviso>{erro ?? falha}</Aviso>
      <Aviso tipo="ok">{mensagem}</Aviso>
      {carregando && !pendentes && <Carregando />}
      {pendentes?.length === 0 && <p>Nenhuma conta pendente.</p>}
      {pendentes && pendentes.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Nome</th>
              <th>E-mail</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {pendentes.map((u) => (
              <tr key={u.id}>
                <td>{u.nome}</td>
                <td>{u.email}</td>
                <td>
                  <button type="button" className="primario" onClick={() => aprovar(u)}>
                    Aprovar
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <FalhasDeMensageria />
    </>
  )
}

/** Mensagens que não conseguiram ser publicadas no RabbitMQ depois de várias tentativas. */
export function FalhasDeMensageria() {
  const { dados: falhas, erro, recarregar } = useCarga(() => get<MensagemComFalha[]>('/api/admin/outbox/falhas'), [])
  const [mensagem, setMensagem] = useState<string | null>(null)

  const reprocessar = async (id: number) => {
    await post(`/api/admin/outbox/${id}/reprocessar`)
    setMensagem(`Mensagem ${id} devolvida para publicação.`)
    recarregar()
  }

  return (
    <>
      <h2>Mensagens com falha de publicação</h2>
      <Aviso>{erro}</Aviso>
      <Aviso tipo="ok">{mensagem}</Aviso>
      {falhas?.length === 0 && <p className="meta">Nenhuma mensagem com falha.</p>}
      {falhas && falhas.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Tipo</th>
              <th>Quando</th>
              <th>Último erro</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {falhas.map((f) => (
              <tr key={f.id}>
                <td className="codigo">{f.tipo}</td>
                <td>{dataHora(f.ocorridoEm)}</td>
                <td>{f.ultimoErro}</td>
                <td>
                  <button type="button" onClick={() => reprocessar(f.id)}>
                    Reprocessar
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}
