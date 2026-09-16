import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { del, get, post } from '../api/cliente'
import type { EventoDetalhe, Setor } from '../api/tipos'
import { Aviso, Carregando, Selo } from '../componentes/Comum'
import { useCarga } from '../componentes/useCarga'
import { useSessao } from '../sessao/contexto'
import { dataHora, localParaIso, preco } from '../util/formato'

export function Organizador() {
  const { usuario } = useSessao()
  const { dados: eventos, erro, carregando, recarregar } = useCarga(
    () => get<EventoDetalhe[]>('/api/eventos/meus'),
    [],
  )
  const [falha, setFalha] = useState<string | null>(null)
  const [sucesso, setSucesso] = useState<string | null>(null)

  const executar = async (acao: () => Promise<unknown>, mensagem: string) => {
    setFalha(null)
    setSucesso(null)
    try {
      await acao()
      setSucesso(mensagem)
      recarregar()
      return true
    } catch (e) {
      setFalha((e as Error).message)
      return false
    }
  }

  if (usuario?.status === 'AguardandoAprovacao') {
    return (
      <Aviso tipo="info">
        Sua conta de organizador está aguardando a aprovação de um administrador. Assim que for aprovada, você poderá
        cadastrar eventos. (Se você acabou de ser aprovado, saia e entre de novo.)
      </Aviso>
    )
  }

  const criarEvento = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = e.currentTarget
    const f = new FormData(form)
    const ok = await executar(
      () =>
        post('/api/eventos', {
          titulo: f.get('titulo'),
          descricao: f.get('descricao'),
          local: f.get('local'),
          cidade: f.get('cidade'),
          dataInicio: localParaIso(String(f.get('dataInicio'))),
        }),
      'Evento criado como rascunho. Adicione setores e publique.',
    )
    if (ok) form.reset()
  }

  return (
    <>
      <h1>Meus eventos</h1>
      <Aviso>{erro ?? falha}</Aviso>
      <Aviso tipo="ok">{sucesso}</Aviso>

      <details className="cartao">
        <summary>
          <strong>+ Novo evento</strong>
        </summary>
        <form className="formulario largo" onSubmit={criarEvento}>
          <label>
            Título <input name="titulo" required minLength={3} maxLength={150} />
          </label>
          <label>
            Descrição <textarea name="descricao" maxLength={4000} rows={3} />
          </label>
          <div className="linha">
            <label>
              Local <input name="local" required maxLength={150} />
            </label>
            <label>
              Cidade <input name="cidade" required maxLength={100} />
            </label>
            <label>
              Data e hora <input name="dataInicio" type="datetime-local" required />
            </label>
          </div>
          <button className="primario">Criar rascunho</button>
        </form>
      </details>

      {carregando && !eventos && <Carregando />}
      {eventos?.length === 0 && <p>Você ainda não cadastrou eventos.</p>}
      {eventos?.map((evento) => (
        <article className="cartao" key={evento.id}>
          <header className="acoes">
            <h2>
              <Link className="titulo" to={`/eventos/${evento.id}`}>
                {evento.titulo}
              </Link>
            </h2>
            {evento.publicado ? <Selo tom="ok">Publicado</Selo> : <Selo tom="neutro">Rascunho</Selo>}
          </header>
          <p className="meta">
            {dataHora(evento.dataInicio)} · {evento.local} · {evento.cidade}
          </p>

          <TabelaDeSetores
            setores={evento.setores}
            aoRemover={(s) => executar(() => del(`/api/eventos/${evento.id}/setores/${s.id}`), `Setor "${s.nome}" removido.`)}
          />

          <NovoSetor
            aoAdicionar={(dados) => executar(() => post(`/api/eventos/${evento.id}/setores`, dados), 'Setor adicionado.')}
          />

          <footer className="acoes">
            {!evento.publicado && (
              <button
                type="button"
                className="primario"
                onClick={() => executar(() => post(`/api/eventos/${evento.id}/publicar`), 'Evento publicado na vitrine!')}
              >
                Publicar
              </button>
            )}
            <button
              type="button"
              onClick={() =>
                window.confirm('Excluir este evento?') &&
                executar(() => del(`/api/eventos/${evento.id}`), 'Evento excluído.')
              }
            >
              Excluir
            </button>
          </footer>
        </article>
      ))}
    </>
  )
}

function TabelaDeSetores({ setores, aoRemover }: { setores: Setor[]; aoRemover: (s: Setor) => void }) {
  if (setores.length === 0) return <p className="meta">Nenhum setor cadastrado.</p>
  return (
    <table>
      <thead>
        <tr>
          <th>Setor</th>
          <th>Preço</th>
          <th>Vendidos / capacidade</th>
          <th />
        </tr>
      </thead>
      <tbody>
        {setores.map((s) => (
          <tr key={s.id}>
            <td>{s.nome}</td>
            <td>{preco(s.preco)}</td>
            <td>
              {s.capacidade - s.disponiveis} / {s.capacidade}
            </td>
            <td>
              <button type="button" className="link" onClick={() => aoRemover(s)}>
                Remover
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function NovoSetor({ aoAdicionar }: { aoAdicionar: (d: { nome: string; preco: number; capacidade: number }) => Promise<boolean> }) {
  const enviar = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = e.currentTarget
    const f = new FormData(form)
    const ok = await aoAdicionar({
      nome: String(f.get('nome')),
      preco: Number(f.get('preco')),
      capacidade: Number(f.get('capacidade')),
    })
    if (ok) form.reset()
  }

  return (
    <form className="linha compacta" onSubmit={enviar} aria-label="Adicionar setor">
      <input name="nome" placeholder="Nome do setor" required minLength={2} maxLength={80} aria-label="Nome do setor" />
      <input name="preco" type="number" min={0} max={100000} step="0.01" placeholder="Preço" required aria-label="Preço" />
      <input name="capacidade" type="number" min={1} max={100000} placeholder="Capacidade" required aria-label="Capacidade" />
      <button>Adicionar setor</button>
    </form>
  )
}
