# Senhas geradas pelo Terraform e guardadas no Secrets Manager.
# Os containers recebem os valores como variáveis de ambiente injetadas pelo ECS
# (a connection string também vai por aqui porque contém o endereço interno do banco);
# nenhuma senha aparece em imagens, código ou arquivos de configuração.

resource "random_password" "jwt" {
  length  = 64
  special = false
}

resource "random_password" "postgres" {
  length  = 40
  special = false
}

resource "random_password" "redis" {
  length  = 48
  special = false
}

resource "random_password" "rabbitmq" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "aplicacao" {
  name                    = "${local.nome}/aplicacao"
  description             = "Segredos da API e do Worker"
  kms_key_id              = aws_kms_key.principal.arn
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "aplicacao" {
  secret_id = aws_secretsmanager_secret.aplicacao.id
  secret_string = jsonencode({
    Jwt__Chave                  = random_password.jwt.result
    ConnectionStrings__Redis    = "${aws_elasticache_replication_group.redis.primary_endpoint_address}:6379,ssl=true,password=${random_password.redis.result}"
    RabbitMq__Senha             = random_password.rabbitmq.result
    ConnectionStrings__Ingressa = "Host=${aws_db_instance.postgres.address};Database=ingressa;Username=ingressa;SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/rds-global-bundle.pem"
    # O Npgsql lê a senha da variável PGPASSWORD quando ela não está na connection string.
    PGPASSWORD = random_password.postgres.result
    # Credenciais SMTP do Amazon SES (a AWS deriva a senha SMTP da chave de acesso).
    Email__Usuario = aws_iam_access_key.smtp.id
    Email__Senha   = aws_iam_access_key.smtp.ses_smtp_password_v4
  })
}
