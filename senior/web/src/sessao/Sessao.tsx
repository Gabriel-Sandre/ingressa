import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { aoMudarSessao, definirSessao, post, renovarSessao } from '../api/cliente'
import type { Sessao, Usuario } from '../api/tipos'
import { ContextoSessao, type ValorSessao } from './contexto'

export function ProvedorDeSessao({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<Usuario | null>(null)
  const [carregando, setCarregando] = useState(true)

  useEffect(() => {
    const cancelar = aoMudarSessao((s) => setUsuario(s?.usuario ?? null))
    // Ao abrir a página, tenta recuperar a sessão pelo cookie.
    renovarSessao().finally(() => setCarregando(false))
    return cancelar
  }, [])

  // Renova o access token pouco antes de ele expirar.
  useEffect(() => {
    if (!usuario) return
    const id = window.setInterval(() => void renovarSessao(), 12 * 60 * 1000)
    return () => window.clearInterval(id)
  }, [usuario])

  const entrar = useCallback(async (email: string, senha: string) => {
    const sessao = await post<Sessao>('/api/auth/login', { email, senha })
    definirSessao(sessao)
    return sessao.usuario
  }, [])

  const registrar = useCallback<ValorSessao['registrar']>(async (dados) => {
    await post('/api/auth/registrar', dados)
  }, [])

  const sair = useCallback(async () => {
    try {
      await post('/api/auth/sair')
    } finally {
      definirSessao(null)
    }
  }, [])

  const valor = useMemo(
    () => ({ usuario, carregando, entrar, registrar, sair }),
    [usuario, carregando, entrar, registrar, sair],
  )

  return <ContextoSessao.Provider value={valor}>{children}</ContextoSessao.Provider>
}
