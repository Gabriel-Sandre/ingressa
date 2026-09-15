// Ingressa — interface mínima em JavaScript puro (versão Júnior).
// Objetivo: demonstrar o consumo da API. A versão Pleno troca por React + TypeScript.
"use strict";

const app = document.getElementById("app");
const menu = document.getElementById("menu");

// ---------- Sessão ----------
// sessionStorage: o token some ao fechar a aba. Simplificação da versão Júnior;
// a Pleno troca por refresh token em cookie HttpOnly.
const sessao = {
  get() {
    try { return JSON.parse(sessionStorage.getItem("ingressa.sessao")); } catch { return null; }
  },
  salvar(dados) { sessionStorage.setItem("ingressa.sessao", JSON.stringify(dados)); },
  sair() { sessionStorage.removeItem("ingressa.sessao"); }
};

// ---------- Utilitários ----------
// Todo texto vindo da API passa por aqui antes de ir para o HTML (evita XSS).
const esc = (valor) => String(valor ?? "").replace(/[&<>"']/g, (c) => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
}[c]));

const moeda = (v) => Number(v).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
const data = (v) => new Date(v).toLocaleString("pt-BR", { dateStyle: "medium", timeStyle: "short" });

async function api(caminho, opcoes = {}) {
  const headers = { "Content-Type": "application/json", ...(opcoes.headers || {}) };
  const s = sessao.get();
  if (s) headers.Authorization = `Bearer ${s.accessToken}`;

  const resposta = await fetch(caminho, { ...opcoes, headers });
  if (resposta.status === 204) return null;

  const corpo = await resposta.json().catch(() => null);
  if (!resposta.ok) {
    if (resposta.status === 401 && s) { sessao.sair(); desenharMenu(); }
    const erros = corpo?.errors ? Object.values(corpo.errors).flat().join(" ") : "";
    throw new Error(erros || corpo?.detail || corpo?.title || `Erro ${resposta.status}`);
  }
  return corpo;
}

const aviso = (texto, tipo = "erro") => `<div class="aviso ${tipo}" role="alert">${esc(texto)}</div>`;

// ---------- Menu ----------
function desenharMenu() {
  const s = sessao.get();
  menu.innerHTML = s
    ? `<span>Olá, ${esc(s.usuario.nome)}</span>
       ${s.usuario.perfil === "Cliente" ? '<a href="#/pedidos">Meus pedidos</a>' : ""}
       <button id="sair" type="button">Sair</button>`
    : `<a href="#/entrar">Entrar</a><a href="#/cadastro">Criar conta</a>`;
  document.getElementById("sair")?.addEventListener("click", () => {
    sessao.sair();
    desenharMenu();
    location.hash = "#/";
  });
}

// ---------- Telas ----------
async function telaVitrine(params) {
  const busca = params.get("busca") || "";
  const ordem = params.get("ordem") || "Data";
  const pagina = Number(params.get("pagina") || 1);

  app.innerHTML = `
    <h1>Próximos eventos</h1>
    <form class="filtros" id="filtros">
      <input name="busca" placeholder="Buscar por nome ou descrição" value="${esc(busca)}" aria-label="Buscar">
      <select name="ordem" aria-label="Ordenar">
        <option value="Data" ${ordem === "Data" ? "selected" : ""}>Mais próximos</option>
        <option value="Titulo" ${ordem === "Titulo" ? "selected" : ""}>Nome (A–Z)</option>
      </select>
      <button class="primario">Buscar</button>
    </form>
    <div id="resultado">Carregando…</div>`;

  document.getElementById("filtros").addEventListener("submit", (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    location.hash = `#/?${new URLSearchParams({ busca: f.get("busca"), ordem: f.get("ordem"), pagina: 1 })}`;
  });

  const alvo = document.getElementById("resultado");
  try {
    const q = new URLSearchParams({ busca, ordem, pagina, tamanhoPagina: 6 });
    const r = await api(`/api/eventos?${q}`);
    if (r.itens.length === 0) {
      alvo.innerHTML = "<p>Nenhum evento encontrado.</p>";
      return;
    }
    const link = (p) => `#/?${new URLSearchParams({ busca, ordem, pagina: p })}`;
    alvo.innerHTML = `
      <div class="grade">
        ${r.itens.map((e) => `
          <article class="cartao">
            <h2><a class="titulo" href="#/eventos/${e.id}">${esc(e.titulo)}</a></h2>
            <p class="meta">${esc(data(e.dataInicio))}</p>
            <p class="meta">${esc(e.local)} · ${esc(e.cidade)}</p>
            <p class="preco">${e.esgotado ? '<span class="selo">Esgotado</span>'
              : e.precoAPartirDe === 0 ? "Gratuito"
              : `a partir de ${moeda(e.precoAPartirDe)}`}</p>
          </article>`).join("")}
      </div>
      <nav class="paginacao" aria-label="Paginação">
        ${r.pagina > 1 ? `<a href="${link(r.pagina - 1)}">← Anterior</a>` : ""}
        <span>Página ${r.pagina} de ${r.totalPaginas} (${r.totalItens} eventos)</span>
        ${r.pagina < r.totalPaginas ? `<a href="${link(r.pagina + 1)}">Próxima →</a>` : ""}
      </nav>`;
  } catch (erro) {
    alvo.innerHTML = aviso(erro.message);
  }
}

async function telaEvento(id) {
  app.innerHTML = "Carregando…";
  let evento;
  try {
    evento = await api(`/api/eventos/${id}`);
  } catch (erro) {
    app.innerHTML = aviso(erro.message);
    return;
  }
  const s = sessao.get();
  const podeComprar = s?.usuario.perfil === "Cliente";

  app.innerHTML = `
    <p><a href="#/">← Voltar</a></p>
    <h1>${esc(evento.titulo)}</h1>
    <p class="meta">${esc(data(evento.dataInicio))} · ${esc(evento.local)} · ${esc(evento.cidade)}</p>
    <p>${esc(evento.descricao)}</p>
    <form id="compra">
      <table>
        <thead><tr><th>Setor</th><th>Preço</th><th>Disponíveis</th><th>Quantidade</th></tr></thead>
        <tbody>
          ${evento.setores.map((s) => `
            <tr>
              <td>${esc(s.nome)}</td>
              <td>${s.preco === 0 ? "Gratuito" : moeda(s.preco)}</td>
              <td>${s.disponiveis > 0 ? s.disponiveis : '<span class="selo">Esgotado</span>'}</td>
              <td><input type="number" min="0" max="${Math.min(6, s.disponiveis)}" value="0"
                   name="setor-${s.id}" aria-label="Quantidade para ${esc(s.nome)}" ${s.disponiveis === 0 ? "disabled" : ""}></td>
            </tr>`).join("")}
        </tbody>
      </table>
      <div id="mensagem"></div>
      ${podeComprar
        ? '<button class="primario">Comprar ingressos</button>'
        : `<p class="meta">${s ? "Apenas clientes podem comprar." : '<a href="#/entrar">Entre</a> para comprar.'}</p>`}
    </form>`;

  document.getElementById("compra").addEventListener("submit", async (e) => {
    e.preventDefault();
    const botao = e.submitter;
    const mensagem = document.getElementById("mensagem");
    const itens = [...new FormData(e.target)]
      .map(([nome, valor]) => ({ setorId: Number(nome.replace("setor-", "")), quantidade: Number(valor) }))
      .filter((i) => i.quantidade > 0);

    if (itens.length === 0) {
      mensagem.innerHTML = aviso("Escolha pelo menos um ingresso.");
      return;
    }

    botao.disabled = true;
    try {
      const pedido = await api("/api/pedidos", {
        method: "POST",
        body: JSON.stringify({ eventoId: evento.id, itens })
      });
      location.hash = `#/pedidos?novo=${pedido.id}`;
    } catch (erro) {
      mensagem.innerHTML = aviso(erro.message);
      botao.disabled = false;
    }
  });
}

async function telaPedidos(params) {
  if (!sessao.get()) { location.hash = "#/entrar"; return; }
  app.innerHTML = "Carregando…";
  try {
    const pedidos = await api("/api/pedidos");
    const novo = params.get("novo");
    app.innerHTML = `
      <h1>Meus pedidos</h1>
      ${novo ? aviso(`Compra confirmada! Pedido nº ${novo}.`, "ok") : ""}
      ${pedidos.length === 0 ? "<p>Você ainda não comprou ingressos.</p>" : ""}
      ${pedidos.map((p) => `
        <article class="cartao" style="margin-bottom:1rem">
          <h2>${esc(p.evento)}</h2>
          <p class="meta">Pedido nº ${p.id} · ${esc(data(p.criadoEm))} · ${p.status === "Cancelado" ? '<span class="selo">Cancelado</span>' : "Confirmado"}</p>
          <table>
            <thead><tr><th>Setor</th><th>Código</th><th>Valor</th></tr></thead>
            <tbody>${p.ingressos.map((i) => `
              <tr><td>${esc(i.setor)}</td><td class="codigo">${esc(i.codigo)}</td><td>${moeda(i.precoPago)}</td></tr>`).join("")}
            </tbody>
          </table>
          <p class="preco">Total: ${moeda(p.total)}</p>
          ${p.status === "Confirmado" ? `<button type="button" data-cancelar="${p.id}">Cancelar pedido</button>` : ""}
        </article>`).join("")}
      <div id="mensagem"></div>`;

    app.querySelectorAll("[data-cancelar]").forEach((botao) => botao.addEventListener("click", async () => {
      if (!window.confirm("Cancelar este pedido?")) return;
      botao.disabled = true;
      try {
        await api(`/api/pedidos/${botao.dataset.cancelar}/cancelar`, { method: "POST" });
        telaPedidos(new URLSearchParams());
      } catch (erro) {
        document.getElementById("mensagem").innerHTML = aviso(erro.message);
        botao.disabled = false;
      }
    }));
  } catch (erro) {
    app.innerHTML = aviso(erro.message);
  }
}

function telaEntrar() {
  app.innerHTML = `
    <h1>Entrar</h1>
    <form class="formulario" id="entrar">
      <label>E-mail <input name="email" type="email" required autocomplete="username"></label>
      <label>Senha <input name="senha" type="password" required autocomplete="current-password"></label>
      <button class="primario">Entrar</button>
      <p class="meta">Contas de exemplo: <b>cliente@ingressa.dev</b> ou <b>organizador@ingressa.dev</b>, senha <b>Senha@123</b>.</p>
      <div id="mensagem"></div>
    </form>`;
  document.getElementById("entrar").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
      const dados = Object.fromEntries(new FormData(e.target));
      sessao.salvar(await api("/api/auth/login", { method: "POST", body: JSON.stringify(dados) }));
      desenharMenu();
      location.hash = "#/";
    } catch (erro) {
      document.getElementById("mensagem").innerHTML = aviso(erro.message);
    }
  });
}

function telaCadastro() {
  app.innerHTML = `
    <h1>Criar conta</h1>
    <form class="formulario" id="cadastro">
      <label>Nome <input name="nome" required minlength="3" maxlength="100" autocomplete="name"></label>
      <label>E-mail <input name="email" type="email" required autocomplete="email"></label>
      <label>Senha (mínimo 8 caracteres) <input name="senha" type="password" required minlength="8" autocomplete="new-password"></label>
      <label>Tipo de conta
        <select name="perfil">
          <option value="Cliente">Quero comprar ingressos</option>
          <option value="Organizador">Quero vender ingressos</option>
        </select>
      </label>
      <button class="primario">Criar conta</button>
      <div id="mensagem"></div>
    </form>`;
  document.getElementById("cadastro").addEventListener("submit", async (e) => {
    e.preventDefault();
    const dados = Object.fromEntries(new FormData(e.target));
    try {
      await api("/api/auth/registrar", { method: "POST", body: JSON.stringify(dados) });
      sessao.salvar(await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email: dados.email, senha: dados.senha })
      }));
      desenharMenu();
      location.hash = "#/";
    } catch (erro) {
      document.getElementById("mensagem").innerHTML = aviso(erro.message);
    }
  });
}

// ---------- Roteador por hash ----------
function rotear() {
  const [caminho, query = ""] = location.hash.slice(1).split("?");
  const params = new URLSearchParams(query);
  const evento = /^\/eventos\/(\d+)$/.exec(caminho || "");

  if (evento) return telaEvento(evento[1]);
  if (caminho === "/pedidos") return telaPedidos(params);
  if (caminho === "/entrar") return telaEntrar();
  if (caminho === "/cadastro") return telaCadastro();
  return telaVitrine(params);
}

window.addEventListener("hashchange", rotear);
desenharMenu();
rotear();
