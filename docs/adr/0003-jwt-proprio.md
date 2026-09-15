# ADR 0003 — Autenticação JWT própria em vez do ASP.NET Core Identity

**Status:** aceita · **Data:** 2026-09

## Contexto

A API precisa autenticar clientes e organizadores e é consumida por uma interface JavaScript.

## Alternativas

- **ASP.NET Core Identity + endpoints prontos:** completo (bloqueio, confirmação de e-mail, 2FA), mas esconde o funcionamento.
- **JWT próprio:** tabela de usuários, hash de senha e emissão de token escritos no projeto.
- **Provedor externo** (Auth0, Keycloak): exige conta ou infraestrutura adicional.

## Decisão

JWT próprio, usando apenas primitivas consagradas: `Rfc2898DeriveBytes.Pbkdf2`, `RandomNumberGenerator`, `CryptographicOperations.FixedTimeEquals` e `JsonWebTokenHandler`. Nenhum algoritmo criptográfico é implementado à mão.

## Consequências

- ✅ Cada etapa da autenticação fica visível e testada.
- ❌ Recursos como bloqueio por tentativas, renovação de token e 2FA precisam ser construídos — o que é feito nas versões seguintes e no projeto Sentinela.
