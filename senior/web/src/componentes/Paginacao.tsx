interface Props {
  pagina: number
  totalPaginas: number
  totalItens: number
  aoMudar: (pagina: number) => void
}

export function Paginacao({ pagina, totalPaginas, totalItens, aoMudar }: Props) {
  if (totalPaginas <= 1) return <p className="meta centro">{totalItens} evento(s)</p>
  return (
    <nav className="paginacao" aria-label="Paginação">
      <button type="button" disabled={pagina <= 1} onClick={() => aoMudar(pagina - 1)}>
        ← Anterior
      </button>
      <span>
        Página {pagina} de {totalPaginas} ({totalItens} eventos)
      </span>
      <button type="button" disabled={pagina >= totalPaginas} onClick={() => aoMudar(pagina + 1)}>
        Próxima →
      </button>
    </nav>
  )
}
