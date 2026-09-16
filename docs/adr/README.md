# Registros de decisão de arquitetura (ADRs)

Cada arquivo registra **uma** decisão: o contexto, as alternativas, a escolha e as consequências. Decisões não são apagadas; quando mudam, um novo ADR substitui o anterior.

| Nº | Decisão | Versão | Status |
|---|---|---|---|
| [0001](0001-monolito-simples-na-versao-junior.md) | Monólito em um único projeto na versão Júnior | Júnior | Aceita |
| [0002](0002-sqlite-na-versao-junior.md) | SQLite como banco da versão Júnior | Júnior | Aceita (substituída na Pleno) |
| [0003](0003-jwt-proprio.md) | Autenticação JWT própria em vez do ASP.NET Core Identity | Júnior | Aceita |
| [0004](0004-camadas-sem-microsservicos.md) | Monólito em camadas com um Worker, e não microsserviços | Pleno | Aceita |
| [0005](0005-postgresql-e-update-atomico.md) | PostgreSQL e UPDATE condicional para o estoque | Pleno | Aceita |
| [0006](0006-outbox-e-rabbitmq.md) | Transactional Outbox + RabbitMQ | Pleno | Aceita |
| [0007](0007-sessao-com-refresh-token-em-cookie.md) | Sessão com refresh token rotativo em cookie HttpOnly | Pleno | Aceita |
| [0008](0008-repositorios-e-consultas-separadas.md) | Repositórios para escrita, consultas projetadas para leitura | Pleno | Aceita |
