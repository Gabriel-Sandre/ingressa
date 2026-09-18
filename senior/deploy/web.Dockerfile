FROM node:25-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ .
RUN npm run build

# Imagem sem root: escuta na 8080 e roda como usuário "nginx".
FROM nginxinc/nginx-unprivileged:1.29-alpine
ENV API_UPSTREAM=api:8080
COPY deploy/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
