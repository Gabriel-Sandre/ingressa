# ADR 0008 — Repositórios para escrita, consultas projetadas para leitura

**Status:** aceita · **Versão:** Pleno · **Data:** 2026-09

## Contexto

Na Júnior, a ADR 0001 dispensou repositórios: os serviços usavam o `DbContext`. Com a camada Application separada, os casos de uso não podem depender do EF Core.

## Alternativas

1. Application referencia EF Core e usa o `DbContext` diretamente.
2. Repositório genérico (`IRepository<T>` com `GetAll`, `Update`...).
3. **Repositórios específicos por agregado para escrita + interfaces de consulta que devolvem DTOs.**

## Decisão

Opção 3.

## Justificativa

- Repositórios específicos expõem só o que o domínio precisa (`ListarIdsDeReservasVencidasAsync`), sem vazar `IQueryable`.
- Um repositório genérico esconde o EF Core sem acrescentar nada e incentiva carregar entidades inteiras para exibir uma lista.
- Leituras (vitrine, pedidos) não precisam de entidades: projeções direto para DTO são mais rápidas e deixam o banco calcular preço mínimo e "esgotado".
- Com as portas, os casos de uso são testados com um banco em memória simples (`BancoEmMemoria`), sem mocks frágeis.

## Consequências

- ✅ Application sem dependência de infraestrutura; testes rápidos.
- ✅ Leituras eficientes, sem `N+1`.
- ❌ Mais interfaces; a regra "rascunho só para o dono" precisa ser aplicada no serviço, sobre o DTO.
