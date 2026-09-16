import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { Vitrine } from './Vitrine'

const pagina = (titulos: string[], esgotado = false) => ({
  itens: titulos.map((titulo, i) => ({
    id: i + 1,
    titulo,
    local: 'Arena',
    cidade: 'Rio',
    dataInicio: '2026-10-15T22:00:00Z',
    precoAPartirDe: 80,
    esgotado,
  })),
  pagina: 1,
  tamanhoPagina: 9,
  totalItens: titulos.length,
  totalPaginas: 1,
})

describe('Vitrine', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('mostra os eventos com preço e data em horário de Brasília', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(pagina(['Festival de Rock']))))

    render(<MemoryRouter><Vitrine /></MemoryRouter>)

    expect(await screen.findByRole('link', { name: 'Festival de Rock' })).toHaveAttribute('href', '/eventos/1')
    expect(screen.getByText(/a partir de R\$\s?80,00/)).toBeInTheDocument()
    expect(screen.getByText(/15 de out\. de 2026, 19:00/)).toBeInTheDocument()
  })

  it('envia a busca e a ordenação para a API', async () => {
    fetchMock.mockImplementation(async () => new Response(JSON.stringify(pagina([]))))
    render(<MemoryRouter><Vitrine /></MemoryRouter>)
    await screen.findByText('Nenhum evento encontrado.')

    await userEvent.type(screen.getByRole('textbox', { name: 'Buscar' }), 'samba')
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Ordenar por' }), 'Preco')
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }))

    const ultimaUrl = String(fetchMock.mock.calls.at(-1)?.[0])
    expect(ultimaUrl).toContain('busca=samba')
    expect(ultimaUrl).toContain('ordem=Preco')
    expect(ultimaUrl).toContain('pagina=1')
  })

  it('mostra erro amigável quando a API falha', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ title: 'Erro interno', detail: 'Tente mais tarde.' }), { status: 500 }))

    render(<MemoryRouter><Vitrine /></MemoryRouter>)

    expect(await screen.findByRole('alert')).toHaveTextContent('Tente mais tarde.')
  })
})
