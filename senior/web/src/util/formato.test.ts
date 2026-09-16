import { cronometro, moeda, preco } from './formato'

describe('formato', () => {
  it('formata o cronômetro sem valores negativos', () => {
    expect(cronometro(9 * 60_000 + 41_000)).toBe('09:41')
    expect(cronometro(500)).toBe('00:01')
    expect(cronometro(-3_000)).toBe('00:00')
  })

  it('formata valores em reais e trata preço zero como gratuito', () => {
    expect(moeda(1234.5).replace(/\s/g, ' ')).toBe('R$ 1.234,50')
    expect(preco(0)).toBe('Gratuito')
  })
})
