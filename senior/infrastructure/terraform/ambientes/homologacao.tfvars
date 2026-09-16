ambiente            = "homologacao"
dominio             = "homologacao.ingressa.exemplo.com.br"
certificado_acm_arn = "arn:aws:acm:sa-east-1:000000000000:certificate/substituir"
email_alertas       = "alertas@exemplo.com.br"

# Homologação menor e mais barata.
api    = { cpu = 256, memoria = 512, minimo = 1, maximo = 3 }
worker = { cpu = 256, memoria = 512, minimo = 1, maximo = 2 }
banco = {
  classe           = "db.t4g.micro"
  armazenamento_gb = 20
  multi_az         = false
  retencao_backup  = 3
}
redis_classe      = "cache.t4g.micro"
mensageria_classe = "mq.t3.micro"
