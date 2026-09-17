# Banco de dados — versão Sênior

**PostgreSQL 17** com migrations do EF Core (`src/Ingressa.Infrastructure/Persistencia/Migrations`).

| Migration | Conteúdo |
|---|---|
| `Inicial` | Esquema da Pleno (usuários, sessões, eventos, setores, pedidos, ingressos, outbox) |
| `SeniorFilaEIdempotencia` | Coluna `Eventos.FilaVirtual` e tabela `RequisicoesIdempotentes` (índice único usuário + chave) |

| Arquivo | Conteúdo |
|---|---|
| `ingressa-senior.sql` | Script **idempotente** com todas as migrations — só aplica o que falta |

Em produção a API **não** altera o esquema ao iniciar (`Banco__InicializarAoIniciar=false`):
o pipeline executa a tarefa de migração antes do deploy.

```bash
# mesmo efeito da tarefa de migração na AWS
docker run --rm --env-file <arquivo-com-as-variaveis> ingressa-api --migrar-e-sair
# ou aplicando o script
psql "host=... dbname=ingressa user=ingressa sslmode=verify-full" -f database/ingressa-senior.sql
```

Para gerar o script novamente:

```bash
cd senior
dotnet tool restore
dotnet ef migrations script --idempotent -p src/Ingressa.Infrastructure -s src/Ingressa.Api -o database/ingressa-senior.sql
```

O que **não** fica no PostgreSQL: posição na fila, passes, contadores de limite e cache (Redis,
dados descartáveis). Backups e restauração: [runbook](../docs/runbooks/restauracao.md).

Acesso ao banco do compose: `docker compose exec postgres psql -U ingressa -d ingressa`.
