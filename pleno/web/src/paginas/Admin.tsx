import { useState } from 'react'
import { get, post } from '../api/cliente'
import type { Usuario } from '../api/tipos'
import { Aviso, Carregando } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'

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
    </>
  )
}
