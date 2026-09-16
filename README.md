# Ingressa

**Plataforma de venda de ingressos construída em três versões — Júnior, Pleno e Sênior — para mostrar, na prática, como um sistema evolui quando os requisitos ficam mais difíceis.**

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-blue)](LICENSE)

## O problema

Toda grande venda de ingressos vira notícia: site fora do ar, fila que não anda, ingresso vendido duas vezes, cambistas com robôs comprando tudo em segundos.

Por trás disso há problemas técnicos clássicos:

- **Concorrência:** duas pessoas tentando comprar o último lugar no mesmo milissegundo.
- **Picos de tráfego:** 100 mil pessoas chegando às 10h00 em ponto.
- **Consistência:** o pagamento aprovou, mas o servidor caiu antes de emitir o ingresso.
- **Justiça:** impedir que robôs passem na frente de pessoas.

O Ingressa resolve esses problemas **em etapas**, e cada etapa fica registrada no repositório.

## As três versões

| | [Júnior](junior/) | [Pleno](pleno/) | Sênior |
|---|---|---|---|
| **Pergunta que responde** | "Sei construir uma API correta?" | "Sei estruturar um sistema profissional?" | "Sei decidir arquitetura para escala e falhas?" |
| **Arquitetura** | Monólito em um projeto | Monólito em camadas | Monólito modular + serviços extraídos com justificativa |
| **Banco** | SQLite | PostgreSQL + migrations | PostgreSQL + Redis |
| **Concorrência** | Não tratada (limitação documentada) | Concorrência otimista + reserva com expiração | Fila virtual + idempotência |
| **Mensageria** | — | RabbitMQ + Outbox | Eventos entre módulos |
| **Interface** | JavaScript puro | React + TypeScript | + testes ponta a ponta |
| **Qualidade** | Testes de unidade/serviço | + integração com banco real, CI | + carga (k6), segurança (SAST/DAST) |
| **Operação** | `dotnet run` | Docker Compose, logs estruturados | OpenTelemetry, IaC (Terraform/AWS) |
| **Status** | ✅ Concluída | ✅ Concluída | ⏳ Planejada |

Cada pasta é um projeto independente, com README próprio explicando o que foi feito, como rodar e **quais limitações motivaram a versão seguinte**.

## Começando pela versão Júnior

```bash
git clone https://github.com/Gabriel-Sandre/ingressa.git
cd ingressa/junior
dotnet run --project src/Ingressa.Api --launch-profile http
# abra http://localhost:5080  (cliente@ingressa.dev / Senha@123)
```

Para a versão Pleno (precisa do Docker):

```bash
cd ingressa/pleno
docker compose up --build
# abra http://localhost:8080
```

| Júnior | Pleno |
|---|---|
| ![Vitrine da versão Júnior](junior/docs/screenshots/01-vitrine.png) | ![Vitrine da versão Pleno](pleno/docs/screenshots/01-vitrine.png) |

### O mesmo problema, duas respostas

200 compras simultâneas para um setor com 10 lugares ([experimento](pleno/docs/experimentos/concorrencia-no-estoque.md)):

| | Compras aceitas | Lugares registrados |
|---|---|---|
| Júnior (ler → calcular → gravar) | **200** | 9 |
| Pleno (UPDATE atômico) | **10** | 10 |

## Estrutura do repositório

```
ingressa/
├── junior/   → API REST + EF Core + SQLite + JWT + vitrine em JavaScript
├── pleno/    → camadas, PostgreSQL, reserva e pagamento, RabbitMQ + outbox, React, Docker
├── senior/   → (planejada)
├── docs/     → decisões de arquitetura (ADRs) e roadmap
└── pdf/      → estudo de caso completo (análise, decisões e evolução)
```

## Documentação

- [Estudo de caso em PDF](pdf/INGRESSA_ANALISE_E_DESENVOLVIMENTO.pdf)
- [Roadmap](docs/roadmap.md)
- [Decisões de arquitetura (ADRs)](docs/adr/)

## Autor

**Gabriel Sandre** — estudante de Análise e Desenvolvimento de Sistemas (INFNET)
[LinkedIn](https://www.linkedin.com/in/sandregabriel) · [GitHub](https://github.com/Gabriel-Sandre)

Licenciado sob a [licença MIT](LICENSE).
