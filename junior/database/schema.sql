CREATE TABLE "Usuarios" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Usuarios" PRIMARY KEY AUTOINCREMENT,
    "Nome" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "SenhaHash" TEXT NOT NULL,
    "Perfil" TEXT NOT NULL,
    "CriadoEm" TEXT NOT NULL
);


CREATE TABLE "Eventos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Eventos" PRIMARY KEY AUTOINCREMENT,
    "Titulo" TEXT NOT NULL,
    "Descricao" TEXT NOT NULL,
    "Local" TEXT NOT NULL,
    "Cidade" TEXT NOT NULL,
    "DataInicio" TEXT NOT NULL,
    "Publicado" INTEGER NOT NULL,
    "OrganizadorId" INTEGER NOT NULL,
    "CriadoEm" TEXT NOT NULL,
    CONSTRAINT "FK_Eventos_Usuarios_OrganizadorId" FOREIGN KEY ("OrganizadorId") REFERENCES "Usuarios" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "Pedidos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Pedidos" PRIMARY KEY AUTOINCREMENT,
    "UsuarioId" INTEGER NOT NULL,
    "EventoId" INTEGER NOT NULL,
    "CriadoEm" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "Total" TEXT NOT NULL,
    CONSTRAINT "FK_Pedidos_Eventos_EventoId" FOREIGN KEY ("EventoId") REFERENCES "Eventos" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Pedidos_Usuarios_UsuarioId" FOREIGN KEY ("UsuarioId") REFERENCES "Usuarios" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "Setores" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Setores" PRIMARY KEY AUTOINCREMENT,
    "EventoId" INTEGER NOT NULL,
    "Nome" TEXT NOT NULL,
    "Preco" TEXT NOT NULL,
    "Capacidade" INTEGER NOT NULL,
    "Vendidos" INTEGER NOT NULL,
    CONSTRAINT "FK_Setores_Eventos_EventoId" FOREIGN KEY ("EventoId") REFERENCES "Eventos" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Ingressos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Ingressos" PRIMARY KEY AUTOINCREMENT,
    "PedidoId" INTEGER NOT NULL,
    "SetorId" INTEGER NOT NULL,
    "Codigo" TEXT NOT NULL,
    "PrecoPago" TEXT NOT NULL,
    CONSTRAINT "FK_Ingressos_Pedidos_PedidoId" FOREIGN KEY ("PedidoId") REFERENCES "Pedidos" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Ingressos_Setores_SetorId" FOREIGN KEY ("SetorId") REFERENCES "Setores" ("Id") ON DELETE RESTRICT
);


CREATE INDEX "IX_Eventos_OrganizadorId" ON "Eventos" ("OrganizadorId");


CREATE INDEX "IX_Eventos_Publicado_DataInicio" ON "Eventos" ("Publicado", "DataInicio");


CREATE UNIQUE INDEX "IX_Ingressos_Codigo" ON "Ingressos" ("Codigo");


CREATE INDEX "IX_Ingressos_PedidoId" ON "Ingressos" ("PedidoId");


CREATE INDEX "IX_Ingressos_SetorId" ON "Ingressos" ("SetorId");


CREATE INDEX "IX_Pedidos_EventoId" ON "Pedidos" ("EventoId");


CREATE INDEX "IX_Pedidos_UsuarioId" ON "Pedidos" ("UsuarioId");


CREATE INDEX "IX_Setores_EventoId" ON "Setores" ("EventoId");


CREATE UNIQUE INDEX "IX_Usuarios_Email" ON "Usuarios" ("Email");


