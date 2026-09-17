import type { ReactNode } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import type { Perfil } from '../api/tipos'
import { useSessao } from '../sessao/contexto'

export function Aviso({ tipo = 'erro', children }: { tipo?: 'erro' | 'ok' | 'info'; children: ReactNode }) {
  if (!children) return null
  return (
    <div className={`aviso ${tipo}`} role={tipo === 'erro' ? 'alert' : 'status'}>
      {children}
    </div>
  )
}

export function Carregando() {
  return <p className="meta" aria-busy="true">Carregando…</p>
}

export function Selo({ children, tom = 'perigo' }: { children: ReactNode; tom?: 'perigo' | 'ok' | 'neutro' }) {
  return <span className={`selo ${tom}`}>{children}</span>
}

/** Rota que exige login (e, opcionalmente, um perfil). */
export function Protegida({ perfil, children }: { perfil?: Perfil; children: ReactNode }) {
  const { usuario, carregando } = useSessao()
  const local = useLocation()

  if (carregando) return <Carregando />
  if (!usuario) return <Navigate to="/entrar" replace state={{ de: local.pathname }} />
  if (perfil && usuario.perfil !== perfil) {
    return (
      <Aviso>
        Esta página é exclusiva para contas do tipo {perfil}. <Link to="/">Voltar à vitrine</Link>
      </Aviso>
    )
  }
  return <>{children}</>
}
