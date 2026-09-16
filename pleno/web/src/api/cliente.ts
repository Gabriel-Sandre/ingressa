import type { ProblemDetails, Sessao } from './tipos'

/**
 * Cliente HTTP da aplicação.
 *
 * - O access token fica só em memória (uma variável), nunca em localStorage:
 *   um script injetado na página não encontra o token guardado em lugar nenhum.
 * - A sessão sobrevive a um F5 graças ao refresh token, que está num cookie
 *   HttpOnly que o JavaScript não consegue ler.
 * - Quando a API responde 401, o cliente renova a sessão uma única vez e repete a chamada.
 *   Várias chamadas simultâneas compartilham a mesma renovação.
 */

export class ErroDaApi extends Error {
  readonly status: number
  readonly detalhes: ProblemDetails | null

  constructor(status: number, detalhes: ProblemDetails | null) {
    super(ErroDaApi.mensagem(status, detalhes))
    this.name = 'ErroDaApi'
    this.status = status
    this.detalhes = detalhes
  }

  private static mensagem(status: number, d: ProblemDetails | null): string {
    const validacao = d?.errors ? Object.values(d.errors).flat().join(' ') : ''
    if (validacao) return validacao
    if (d?.detail) return d.detail
    if (status === 0) return 'Não foi possível falar com o servidor. Verifique sua conexão.'
    return d?.title ?? `Erro inesperado (${status}).`
  }
}

type OuvinteDeSessao = (sessao: Sessao | null) => void

let accessToken: string | null = null
let renovacaoEmAndamento: Promise<Sessao | null> | null = null
const ouvintes = new Set<OuvinteDeSessao>()

export function definirSessao(sessao: Sessao | null): void {
  accessToken = sessao?.accessToken ?? null
  ouvintes.forEach((ouvinte) => ouvinte(sessao))
}

export function aoMudarSessao(ouvinte: OuvinteDeSessao): () => void {
  ouvintes.add(ouvinte)
  return () => ouvintes.delete(ouvinte)
}

async function enviar(caminho: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body !== undefined && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  try {
    return await fetch(caminho, { ...init, headers, credentials: 'same-origin' })
  } catch {
    throw new ErroDaApi(0, null)
  }
}

async function ler<T>(resposta: Response): Promise<T> {
  if (resposta.status === 204) return undefined as T
  const texto = await resposta.text()
  const corpo = texto ? JSON.parse(texto) : null
  if (!resposta.ok) throw new ErroDaApi(resposta.status, corpo as ProblemDetails | null)
  return corpo as T
}

/** Tenta renovar a sessão pelo cookie. Retorna null se não houver sessão válida. */
export function renovarSessao(): Promise<Sessao | null> {
  renovacaoEmAndamento ??= (async () => {
    try {
      const resposta = await fetch('/api/auth/renovar', { method: 'POST', credentials: 'same-origin' })
      const sessao = resposta.ok ? ((await resposta.json()) as Sessao) : null
      definirSessao(sessao)
      return sessao
    } catch {
      return null
    } finally {
      renovacaoEmAndamento = null
    }
  })()
  return renovacaoEmAndamento
}

export async function api<T>(caminho: string, init: RequestInit = {}): Promise<T> {
  let resposta = await enviar(caminho, init)

  const podeRenovar = resposta.status === 401 && accessToken !== null && !caminho.startsWith('/api/auth/')
  if (podeRenovar && (await renovarSessao())) {
    resposta = await enviar(caminho, init)
  }

  return ler<T>(resposta)
}

export const get = <T>(caminho: string) => api<T>(caminho)

export const post = <T>(caminho: string, corpo?: unknown) =>
  api<T>(caminho, { method: 'POST', body: corpo === undefined ? undefined : JSON.stringify(corpo) })

export const put = <T>(caminho: string, corpo: unknown) =>
  api<T>(caminho, { method: 'PUT', body: JSON.stringify(corpo) })

export const del = (caminho: string) => api<void>(caminho, { method: 'DELETE' })
