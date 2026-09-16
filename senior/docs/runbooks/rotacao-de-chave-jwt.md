# Rotação da chave JWT

**Quando:** a cada 90 dias, ou imediatamente se houver suspeita de vazamento.

A API aceita tokens assinados pela chave atual (`Jwt:Chave`) e pelas antigas
(`Jwt:ChavesAnteriores`), mas **assina** só com a atual. Isso permite trocar sem derrubar
ninguém. O teste `OperacaoTests` cobre esse comportamento.

## Troca planejada (sem impacto)

1. Gere uma chave nova: `openssl rand -base64 48`.
2. No segredo `ingressa-producao/aplicacao`, mova o valor atual para
   `Jwt__ChavesAnteriores__0` e grave a nova em `Jwt__Chave`.
   (Adicione `Jwt__ChavesAnteriores__0` à lista `segredos` no `containers.tf` na primeira vez.)
3. Force um novo deploy da API: `aws ecs update-service --cluster ingressa-producao --service api --force-new-deployment`.
4. Espere **15 minutos** (validade do access token).
5. Remova `Jwt__ChavesAnteriores__0` e faça outro deploy.

## Vazamento (com impacto)

1. Gere e grave a nova chave **sem** colocar a antiga em `ChavesAnteriores`.
2. Deploy imediato. Todos os access tokens deixam de valer; os clientes renovam a sessão
   pelo refresh token (que não depende da chave JWT) sem precisar digitar a senha.
3. Se houver suspeita de que refresh tokens também vazaram, revogue todas as sessões:
   `UPDATE "RefreshTokens" SET "RevogadoEm" = now() WHERE "RevogadoEm" IS NULL;`

## Confirmação

- Login e compra funcionam após o deploy.
- Nenhum aumento de respostas 401 além do esperado no painel.
