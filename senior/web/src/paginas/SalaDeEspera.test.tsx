import { act, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { obterPasse } from '../sessao/passes'
import { SalaDeEspera } from './SalaDeEspera'

const json = (corpo: unknown) => new Response(JSON.stringify(corpo))

describe('SalaDeEspera', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('mostra a posição e leva à compra quando é liberado', async () => {
    let consultas = 0
    fetchMock.mockImplementation(async (url, init) => {
      const u = String(url)
      if (u === '/api/eventos/5') return json({ id: 5, titulo: 'Show', dataInicio: '2026-10-15T22:00:00Z', setores: [] })
      if (init?.method === 'POST') return json({ eventoId: 5, situacao: 'Aguardando', posicao: 3, passe: null })
      consultas++
      return consultas < 2
        ? json({ eventoId: 5, situacao: 'Aguardando', posicao: 1, passe: null })
        : json({ eventoId: 5, situacao: 'Liberado', posicao: 0, passe: 'passe-xyz' })
    })

    render(
      <MemoryRouter initialEntries={['/eventos/5/fila']}>
        <Routes>
          <Route path="/eventos/:id/fila" element={<SalaDeEspera />} />
          <Route path="/eventos/:id" element={<p>Página de compra</p>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText(/3º/)).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(3100) })
    expect(await screen.findByText(/1º/)).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(3100) })
    expect(await screen.findByText('Página de compra')).toBeInTheDocument()
    expect(obterPasse(5)).toBe('passe-xyz')
  })

  it('não volta para o fim da fila quando a compra já começou', async () => {
    fetchMock.mockImplementation(async (url) => {
      const u = String(url)
      if (u === '/api/eventos/7') return json({ id: 7, titulo: 'Show', dataInicio: '2026-10-15T22:00:00Z', setores: [] })
      return json({ eventoId: 7, situacao: 'Comprando', posicao: 0, passe: null })
    })

    render(
      <MemoryRouter initialEntries={['/eventos/7/fila']}>
        <Routes>
          <Route path="/eventos/:id/fila" element={<SalaDeEspera />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Sua vez já chegou')).toBeInTheDocument()

    // Nenhuma nova consulta é agendada: o comprador continua com a vaga dele.
    const chamadas = fetchMock.mock.calls.length
    await act(async () => { await vi.advanceTimersByTimeAsync(9000) })
    expect(fetchMock.mock.calls.length).toBe(chamadas)
  })
})
