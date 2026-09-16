import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { get } from '../api/cliente'
import type { EventoResumo, OrdemEventos, Pagina } from '../api/tipos'
import { Aviso, Carregando, Selo } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { Paginacao } from '../componentes/Paginacao'
import { dataHora, preco } from '../util/formato'

export function Vitrine() {
  const [params, setParams] = useSearchParams()
  const busca = params.get('busca') ?? ''
  const cidade = params.get('cidade') ?? ''
  const ordem = (params.get('ordem') as OrdemEventos | null) ?? 'Data'
  const pagina = Number(params.get('pagina') ?? '1')

  const [rascunho, setRascunho] = useState({ busca, cidade, ordem })

  const { dados, erro, carregando } = useCarga(() => {
    const q = new URLSearchParams({ busca, cidade, ordem, pagina: String(pagina), tamanhoPagina: '9' })
    return get<Pagina<EventoResumo>>(`/api/eventos?${q}`)
  }, [busca, cidade, ordem, pagina])

  const aplicar = (e: FormEvent) => {
    e.preventDefault()
    setParams({ ...rascunho, pagina: '1' })
  }

  return (
    <>
      <h1>Próximos eventos</h1>
      <form className="filtros" onSubmit={aplicar} role="search">
        <input
          aria-label="Buscar"
          placeholder="Buscar por nome ou descrição"
          value={rascunho.busca}
          onChange={(e) => setRascunho({ ...rascunho, busca: e.target.value })}
        />
        <input
          aria-label="Cidade"
          placeholder="Cidade"
          value={rascunho.cidade}
          onChange={(e) => setRascunho({ ...rascunho, cidade: e.target.value })}
        />
        <select
          aria-label="Ordenar por"
          value={rascunho.ordem}
          onChange={(e) => setRascunho({ ...rascunho, ordem: e.target.value as OrdemEventos })}
        >
          <option value="Data">Mais próximos</option>
          <option value="Preco">Menor preço</option>
          <option value="Titulo">Nome (A–Z)</option>
        </select>
        <button className="primario">Buscar</button>
      </form>

      {carregando && <Carregando />}
      <Aviso>{erro}</Aviso>

      {dados && dados.itens.length === 0 && <p>Nenhum evento encontrado.</p>}
      {dados && dados.itens.length > 0 && (
        <>
          <div className="grade">
            {dados.itens.map((e) => (
              <article className="cartao" key={e.id}>
                <h2>
                  <Link className="titulo" to={`/eventos/${e.id}`}>
                    {e.titulo}
                  </Link>
                </h2>
                <p className="meta">{dataHora(e.dataInicio)}</p>
                <p className="meta">
                  {e.local} · {e.cidade}
                </p>
                <p className="preco">
                  {e.esgotado ? (
                    <Selo>Esgotado</Selo>
                  ) : e.precoAPartirDe === null ? null : e.precoAPartirDe === 0 ? (
                    'Gratuito'
                  ) : (
                    `a partir de ${preco(e.precoAPartirDe)}`
                  )}
                </p>
              </article>
            ))}
          </div>
          <Paginacao
            pagina={dados.pagina}
            totalPaginas={dados.totalPaginas}
            totalItens={dados.totalItens}
            aoMudar={(p) => setParams({ busca, cidade, ordem, pagina: String(p) })}
          />
        </>
      )}
    </>
  )
}
