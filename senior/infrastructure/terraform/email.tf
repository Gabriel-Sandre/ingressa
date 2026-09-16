# E-mails transacionais (confirmação de compra, ingressos) pelo Amazon SES via SMTP.
# O domínio precisa ser verificado no DNS (registros DKIM exibidos no console do SES).

resource "aws_sesv2_email_identity" "dominio" {
  email_identity = var.dominio
}

# Usuário técnico com uma única permissão: enviar e-mail pelo domínio verificado.
# A senha SMTP derivada da chave vai para o Secrets Manager (segredos.tf).
resource "aws_iam_user" "smtp" {
  name = "${local.nome}-smtp"
}

resource "aws_iam_user_policy" "smtp" {
  user = aws_iam_user.smtp.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "ses:SendRawEmail"
      Resource = aws_sesv2_email_identity.dominio.arn
    }]
  })
}

resource "aws_iam_access_key" "smtp" {
  user = aws_iam_user.smtp.name
}
