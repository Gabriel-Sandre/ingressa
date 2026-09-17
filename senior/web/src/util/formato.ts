const moedaBr = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const dataBr = new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'America/Sao_Paulo',
})

export const moeda = (valor: number): string => moedaBr.format(valor)

export const dataHora = (iso: string): string => dataBr.format(new Date(iso))

export const preco = (valor: number): string => (valor === 0 ? 'Gratuito' : moeda(valor))

/** "09:41" a partir de milissegundos restantes; nunca negativo. */
export function cronometro(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000))
  const minutos = Math.floor(total / 60)
  const segundos = total % 60
  return `${String(minutos).padStart(2, '0')}:${String(segundos).padStart(2, '0')}`
}

/** Converte o valor de um <input type="datetime-local"> (horário local) para ISO UTC. */
export const localParaIso = (valor: string): string => new Date(valor).toISOString()

export const rotuloStatus: Record<string, string> = {
  AguardandoPagamento: 'Aguardando pagamento',
  Pago: 'Pago',
  Expirado: 'Expirado',
  Cancelado: 'Cancelado',
}
