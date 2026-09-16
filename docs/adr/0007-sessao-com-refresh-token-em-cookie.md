# ADR 0007 — Sessão com refresh token rotativo em cookie HttpOnly

**Status:** aceita · **Versão:** Pleno · **Data:** 2026-09

## Contexto

Na Júnior, um JWT de 60 minutos ficava no `sessionStorage`: qualquer script injetado na página conseguiria lê-lo, e não havia como encerrar uma sessão antes de expirar.

## Alternativas

| Alternativa | Prós | Contras |
|---|---|---|
| JWT longo no `localStorage` | Simples | Exposto a XSS; não pode ser revogado |
| Cookie de sessão tradicional (servidor guarda a sessão) | Revogável, simples no navegador | Toda requisição consulta o estado; API menos reutilizável por outros clientes |
| **Access token curto em memória + refresh token rotativo em cookie** | Token de acesso nunca persistido; sessão revogável; API continua *stateless* nas chamadas comuns | Mais lógica no cliente (renovação) |
| BFF (backend-for-frontend) com cookies | Nenhum token no navegador | Mais uma aplicação para manter |

## Decisão

Access token de 15 minutos em memória + refresh token de 7 dias em cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`, guardado no banco só como hash, com rotação a cada uso e revogação da família em caso de reutilização (recomendação do OAuth 2.0 Security BCP).

## Consequências

- ✅ Um XSS não encontra token persistido para roubar.
- ✅ Logout e roubo de token encerram a sessão de fato.
- ❌ Interface e API precisam estar na mesma origem (resolvido com nginx e proxy do Vite).
- ❌ O cliente precisa lidar com 401 e renovar — implementado e testado em `web/src/api/cliente.ts`.
