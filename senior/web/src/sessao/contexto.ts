import { createContext, useContext } from 'react'
import type { Perfil, Usuario } from '../api/tipos'

export interface ValorSessao {
  usuario: Usuario | null
  carregando: boolean
  entrar: (email: string, senha: string) => Promise<Usuario>
  registrar: (dados: { nome: string; email: string; senha: string; perfil: Perfil }) => Promise<void>
  sair: () => Promise<void>
}

export const ContextoSessao = createContext<ValorSessao | null>(null)

export function useSessao(): ValorSessao {
  const valor = useContext(ContextoSessao)
  if (!valor) throw new Error('useSessao precisa estar dentro de <ProvedorDeSessao>.')
  return valor
}
