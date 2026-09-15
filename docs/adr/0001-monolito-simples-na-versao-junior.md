# ADR 0001 — Monólito em um único projeto na versão Júnior

**Status:** aceita · **Data:** 2026-09

## Contexto

A versão Júnior tem três áreas (autenticação, eventos, pedidos) e cerca de 20 arquivos de código. A equipe é uma pessoa.

## Alternativas

1. **Um projeto, pastas por responsabilidade** (Controllers, Services, Models...).
2. **Clean Architecture** com quatro projetos desde o início.
3. **Microsserviços** (auth, catálogo, pedidos).

## Decisão

Opção 1.

## Consequências

- ✅ Fácil de ler e navegar; qualquer pessoa entende o fluxo em minutos.
- ✅ Um só `dotnet run`.
- ❌ Nada impede um controller de usar o `DbContext` diretamente — a disciplina depende de convenção.
- ❌ As regras de negócio dependem do EF Core, o que dificulta testá-las isoladamente.

A opção 2 passa a valer a pena na versão Pleno, quando entram reserva, pagamento e mensageria. A opção 3 foi descartada: dividir em serviços traria rede, consistência eventual e implantação distribuída sem nenhum ganho para este volume.
