# Imagem única para a API e o Worker: escolha com --build-arg PROJETO=Ingressa.Api | Ingressa.Worker
ARG PROJETO=Ingressa.Api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJETO
WORKDIR /src

# Restaura primeiro, copiando só os arquivos de projeto: a camada fica em cache
# enquanto as dependências não mudarem.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Ingressa.Domain/Ingressa.Domain.csproj src/Ingressa.Domain/
COPY src/Ingressa.Application/Ingressa.Application.csproj src/Ingressa.Application/
COPY src/Ingressa.Infrastructure/Ingressa.Infrastructure.csproj src/Ingressa.Infrastructure/
COPY src/Ingressa.Api/Ingressa.Api.csproj src/Ingressa.Api/
COPY src/Ingressa.Worker/Ingressa.Worker.csproj src/Ingressa.Worker/
RUN dotnet restore src/${PROJETO}/${PROJETO}.csproj

COPY src/ src/
RUN dotnet publish src/${PROJETO}/${PROJETO}.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG PROJETO
# curl só para o health check do container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
# Certificados da AWS para validar o TLS do RDS (SSL Mode=VerifyFull).
ADD --chmod=644 https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem /etc/ssl/certs/rds-global-bundle.pem
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_ENTRYPOINT=${PROJETO}.dll
WORKDIR /app
COPY --from=build /app .
# Usuário sem privilégios de administrador, já presente nas imagens oficiais.
USER $APP_UID
EXPOSE 8080
# "$@" repassa argumentos extras, ex.: docker run ... --migrar-e-sair
ENTRYPOINT ["sh", "-c", "exec dotnet \"$DOTNET_ENTRYPOINT\" \"$@\"", "--"]
