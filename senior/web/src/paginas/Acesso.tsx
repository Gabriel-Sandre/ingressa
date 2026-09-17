import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import type { Perfil, Usuario } from '../api/tipos'
import { Aviso } from '../componentes/Comum'
import { useSessao } from '../sessao/contexto'

const inicial = (u: Usuario) =>
  u.perfil === 'Organizador' ? '/organizador' : u.perfil === 'Admin' ? '/admin' : '/'

export function Entrar() {
  const { entrar } = useSessao()
  const navegar = useNavigate()
  const destino = (useLocation().state as { de?: string } | null)?.de
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  const enviar = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const dados = new FormData(e.currentTarget)
    setErro(null)
    setEnviando(true)
    try {
      const usuario = await entrar(String(dados.get('email')), String(dados.get('senha')))
      navegar(destino ?? inicial(usuario), { replace: true })
    } catch (err) {
      setErro((err as Error).message)
      setEnviando(false)
    }
  }

  return (
    <>
      <h1>Entrar</h1>
      <form className="formulario" onSubmit={enviar}>
        <label>
          E-mail <input name="email" type="email" required autoComplete="username" />
        </label>
        <label>
          Senha <input name="senha" type="password" required autoComplete="current-password" />
        </label>
        <Aviso>{erro}</Aviso>
        <button className="primario" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
        <p className="meta">
          Contas de demonstração (senha <b>Senha@123</b>): cliente@ingressa.dev, organizador@ingressa.dev,
          admin@ingressa.dev.
        </p>
        <p>
          Ainda não tem conta? <Link to="/cadastro">Criar conta</Link>
        </p>
      </form>
    </>
  )
}

export function Cadastro() {
  const { registrar, entrar } = useSessao()
  const navegar = useNavigate()
  const [erro, setErro] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  const enviar = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const dados = {
      nome: String(f.get('nome')),
      email: String(f.get('email')),
      senha: String(f.get('senha')),
      perfil: String(f.get('perfil')) as Perfil,
    }
    if (dados.senha !== f.get('confirmacao')) {
      setErro('As senhas não conferem.')
      return
    }

    setErro(null)
    setEnviando(true)
    try {
      await registrar(dados)
      const usuario = await entrar(dados.email, dados.senha)
      if (usuario.status === 'AguardandoAprovacao') {
        setAviso('Conta criada! Um administrador precisa aprovar sua conta antes de você publicar eventos.')
        setEnviando(false)
        return
      }
      navegar(inicial(usuario), { replace: true })
    } catch (err) {
      setErro((err as Error).message)
      setEnviando(false)
    }
  }

  return (
    <>
      <h1>Criar conta</h1>
      <form className="formulario" onSubmit={enviar}>
        <label>
          Nome <input name="nome" required minLength={3} maxLength={100} autoComplete="name" />
        </label>
        <label>
          E-mail <input name="email" type="email" required autoComplete="email" />
        </label>
        <label>
          Senha (mínimo 8 caracteres)
          <input name="senha" type="password" required minLength={8} autoComplete="new-password" />
        </label>
        <label>
          Confirme a senha
          <input name="confirmacao" type="password" required minLength={8} autoComplete="new-password" />
        </label>
        <label>
          Tipo de conta
          <select name="perfil" defaultValue="Cliente">
            <option value="Cliente">Quero comprar ingressos</option>
            <option value="Organizador">Quero vender ingressos (requer aprovação)</option>
          </select>
        </label>
        <Aviso>{erro}</Aviso>
        <Aviso tipo="ok">{aviso}</Aviso>
        <button className="primario" disabled={enviando}>
          {enviando ? 'Criando…' : 'Criar conta'}
        </button>
      </form>
    </>
  )
}
