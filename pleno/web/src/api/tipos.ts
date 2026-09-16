// Contratos da API (espelham os DTOs de Ingressa.Application).

export type Perfil = 'Cliente' | 'Organizador' | 'Admin'
export type StatusConta = 'Ativa' | 'AguardandoAprovacao'
export type StatusPedido = 'AguardandoPagamento' | 'Pago' | 'Expirado' | 'Cancelado'
export type OrdemEventos = 'Data' | 'Titulo' | 'Preco'

export interface Usuario {
  id: number
  nome: string
  email: string
  perfil: Perfil
  status: StatusConta
}

export interface Sessao {
  accessToken: string
  expiraEm: string
  usuario: Usuario
}

export interface Pagina<T> {
  itens: T[]
  pagina: number
  tamanhoPagina: number
  totalItens: number
  totalPaginas: number
}

export interface EventoResumo {
  id: number
  titulo: string
  local: string
  cidade: string
  dataInicio: string
  precoAPartirDe: number | null
  esgotado: boolean
}

export interface Setor {
  id: number
  nome: string
  preco: number
  capacidade: number
  disponiveis: number
}

export interface EventoDetalhe {
  id: number
  organizadorId: number
  titulo: string
  descricao: string
  local: string
  cidade: string
  dataInicio: string
  publicado: boolean
  setores: Setor[]
}

export interface ItemPedido {
  setorId: number
  setor: string
  quantidade: number
  precoUnitario: number
  subtotal: number
}

export interface Ingresso {
  id: number
  codigo: string
  setor: string
  precoPago: number
}

export interface Pedido {
  id: number
  usuarioId: number
  eventoId: number
  evento: string
  dataEvento: string
  status: StatusPedido
  total: number
  criadoEm: string
  expiraEm: string
  pagoEm: string | null
  ingressosEmitidos: boolean
  itens: ItemPedido[]
  ingressos: Ingresso[]
}

export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}
