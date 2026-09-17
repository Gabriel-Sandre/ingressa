import { definirSessao, novaChave, postIdempotente } from './cliente'

describe('postIdempotente', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
    definirSessao(null)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('envia a chave de idempotência e o passe da fila', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ id: 1 }), { status: 201 }))

    await postIdempotente('/api/pedidos', { eventoId: 1 }, 'chave-123', { 'X-Passe-Fila': 'passe-abc' })

    const cabecalhos = new Headers(fetchMock.mock.calls[0][1]?.headers)
    expect(cabecalhos.get('Idempotency-Key')).toBe('chave-123')
    expect(cabecalhos.get('X-Passe-Fila')).toBe('passe-abc')
    expect(cabecalhos.get('Content-Type')).toBe('application/json')
  })

  it('gera chaves diferentes a cada operação', () => {
    expect(novaChave()).not.toBe(novaChave())
  })
})
