import { api, definirSessao, ErroDaApi } from './cliente'
import type { Sessao } from './tipos'

const resposta = (status: number, corpo?: unknown) =>
  new Response(corpo === undefined ? null : JSON.stringify(corpo), { status })

const sessao = (token: string): Sessao => ({
  accessToken: token,
  expiraEm: new Date().toISOString(),
  usuario: { id: 1, nome: 'Ana', email: 'ana@teste.dev', perfil: 'Cliente', status: 'Ativa' },
})

describe('cliente da API', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
    definirSessao(sessao('token-velho'))
  })

  afterEach(() => vi.unstubAllGlobals())

  it('envia o access token no cabeçalho Authorization', async () => {
    fetchMock.mockResolvedValueOnce(resposta(200, { ok: true }))

    await api('/api/pedidos')

    const [, init] = fetchMock.mock.calls[0]
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer token-velho')
  })

  it('renova a sessão uma vez quando recebe 401 e repete a chamada', async () => {
    fetchMock
      .mockResolvedValueOnce(resposta(401))
      .mockResolvedValueOnce(resposta(200, sessao('token-novo')))
      .mockResolvedValueOnce(resposta(200, [1, 2]))

    const dados = await api<number[]>('/api/pedidos')

    expect(dados).toEqual([1, 2])
    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/renovar')
    expect(new Headers(fetchMock.mock.calls[2][1]?.headers).get('Authorization')).toBe('Bearer token-novo')
  })

  it('chamadas simultâneas compartilham uma única renovação', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/renovar') return resposta(200, sessao('token-novo'))
      const auth = new Headers(init?.headers).get('Authorization')
      return auth === 'Bearer token-novo' ? resposta(200, {}) : resposta(401)
    })

    await Promise.all([api('/api/a'), api('/api/b'), api('/api/c')])

    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/auth/renovar')).toHaveLength(1)
  })

  it('transforma Problem Details em mensagem legível', async () => {
    fetchMock.mockResolvedValueOnce(resposta(409, { title: 'Conflito', detail: "O setor 'VIP' está esgotado." }))

    await expect(api('/api/pedidos', { method: 'POST' })).rejects.toThrow("O setor 'VIP' está esgotado.")
  })

  it('junta as mensagens de validação', async () => {
    fetchMock.mockResolvedValueOnce(
      resposta(400, { title: 'Erro', errors: { Nome: ['Informe o nome.'], Email: ['E-mail inválido.'] } }),
    )

    const erro = await api('/api/auth/registrar', { method: 'POST' }).catch((e: ErroDaApi) => e)

    expect(erro).toBeInstanceOf(ErroDaApi)
    expect((erro as ErroDaApi).message).toBe('Informe o nome. E-mail inválido.')
    expect((erro as ErroDaApi).status).toBe(400)
  })

  it('não tenta renovar quando o próprio login falha', async () => {
    fetchMock.mockResolvedValueOnce(resposta(401, { detail: 'E-mail ou senha inválidos.' }))

    await expect(api('/api/auth/login', { method: 'POST' })).rejects.toThrow('E-mail ou senha inválidos.')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
