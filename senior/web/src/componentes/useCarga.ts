import { useCallback, useEffect, useState, type DependencyList } from 'react'

interface EstadoDeCarga<T> {
  dados: T | null
  erro: string | null
  carregando: boolean
  recarregar: () => void
}

/** Carrega dados assíncronos e ignora respostas que chegam depois de a tela mudar. */
export function useCarga<T>(carregar: () => Promise<T>, deps: DependencyList): EstadoDeCarga<T> {
  const [estado, setEstado] = useState<{ dados: T | null; erro: string | null; carregando: boolean }>({
    dados: null,
    erro: null,
    carregando: true,
  })
  const [versao, setVersao] = useState(0)

  // As dependências vêm de quem chama (como no useEffect); `carregar` muda a cada render de propósito.
  // oxlint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    let ativo = true
    // oxlint-disable-next-line react/set-state-in-effect
    setEstado((e) => ({ ...e, carregando: true, erro: null }))
    carregar()
      .then((dados) => ativo && setEstado({ dados, erro: null, carregando: false }))
      .catch((e: Error) => ativo && setEstado({ dados: null, erro: e.message, carregando: false }))
    return () => {
      ativo = false
    }
  }, [...deps, versao]) // oxlint-disable-line react-hooks/exhaustive-deps

  const recarregar = useCallback(() => setVersao((v) => v + 1), [])
  return { ...estado, recarregar }
}
