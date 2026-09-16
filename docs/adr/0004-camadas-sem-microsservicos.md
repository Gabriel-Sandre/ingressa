# ADR 0004 — Monólito em camadas com um Worker, e não microsserviços

**Status:** aceita · **Versão:** Pleno · **Data:** 2026-09

## Contexto

A versão Pleno ganha reserva, pagamento, emissão assíncrona, e-mail e aprovação de organizadores. O projeto único da Júnior começou a misturar HTTP, regras e banco.

## Alternativas

1. **Monólito em camadas** (Domain, Application, Infrastructure, Api) + um processo Worker.
2. **Microsserviços**: catálogo, pedidos, pagamentos e notificações, cada um com seu banco.
3. **Continuar em um projeto** com pastas.

## Decisão

Opção 1.

## Justificativa

- As regras de compra envolvem evento, setor e pedido **na mesma transação**. Separar em serviços obrigaria a trocar uma transação por uma saga distribuída, com compensações — complexidade sem ganho neste volume.
- Um time de uma pessoa não se beneficia da independência de implantação que microsserviços oferecem.
- O que realmente tem carga e ritmo diferentes (emissão, e-mail, expiração) já foi separado no **Worker**, que escala sozinho.
- As camadas deixam o domínio testável sem banco e sem HTTP (77 testes rodam em memória).

## Consequências

- ✅ Uma transação garante a consistência da compra.
- ✅ Fronteiras claras: o domínio não referencia EF Core; a API não conhece SQL.
- ❌ Mais projetos e arquivos do que na Júnior.
- ❌ API e Worker compartilham o mesmo banco — aceitável enquanto forem do mesmo time.

A extração de um serviço será reavaliada na versão Sênior, com base em medição.
