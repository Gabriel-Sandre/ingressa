# ADR 0002 — SQLite como banco da versão Júnior

**Status:** aceita (substituída na versão Pleno) · **Data:** 2026-09

## Contexto

Quem avalia o projeto (recrutador, colega) precisa conseguir rodar em minutos, sem instalar servidor de banco.

## Alternativas

- **SQLite:** arquivo local, zero instalação.
- **PostgreSQL:** mais realista, exige instalação ou Docker.
- **SQL Server LocalDB:** só funciona no Windows.

## Decisão

SQLite.

## Consequências

- ✅ `git clone` + `dotnet run` e pronto.
- ❌ O SQLite não agrega nem ordena colunas `decimal` no banco: o preço mínimo da vitrine é calculado em memória (aceitável para uma página de até 50 eventos).
- ❌ O SQLite serializa escritas, o que **esconde** problemas de concorrência que apareceriam em um banco de produção. Por isso a versão Pleno migra para PostgreSQL antes de tratar concorrência.
