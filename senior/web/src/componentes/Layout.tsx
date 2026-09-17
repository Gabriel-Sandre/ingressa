import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useSessao } from '../sessao/contexto'

export function Layout() {
  const { usuario, sair } = useSessao()
  const navegar = useNavigate()

  return (
    <>
      <header className="topo">
        <Link to="/" className="marca">
          Ingressa<span>.</span>
        </Link>
        <nav aria-label="Principal">
          {usuario?.perfil === 'Cliente' && <NavLink to="/pedidos">Meus pedidos</NavLink>}
          {usuario?.perfil === 'Organizador' && <NavLink to="/organizador">Meus eventos</NavLink>}
          {usuario?.perfil === 'Admin' && <NavLink to="/admin">Administração</NavLink>}
          {usuario ? (
            <>
              <span className="ola">Olá, {usuario.nome.split(' ')[0]}</span>
              <button
                type="button"
                className="link"
                onClick={() => {
                  // Sai da página atual antes de encerrar a sessão; do contrário, uma página
                  // protegida redirecionaria para o login lembrando o endereço anterior.
                  navegar('/')
                  void sair()
                }}
              >
                Sair
              </button>
            </>
          ) : (
            <>
              <NavLink to="/entrar">Entrar</NavLink>
              <NavLink to="/cadastro">Criar conta</NavLink>
            </>
          )}
        </nav>
      </header>
      <main>
        <Outlet />
      </main>
      <footer className="rodape">Ingressa · versão Sênior</footer>
    </>
  )
}
