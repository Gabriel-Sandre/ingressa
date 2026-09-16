variable "regiao" {
  description = "Região da AWS (sa-east-1 = São Paulo)."
  type        = string
  default     = "sa-east-1"
}

variable "ambiente" {
  description = "Nome do ambiente: homologacao ou producao."
  type        = string

  validation {
    condition     = contains(["homologacao", "producao"], var.ambiente)
    error_message = "Use 'homologacao' ou 'producao'."
  }
}

variable "dominio" {
  description = "Domínio público da aplicação (ex.: ingressa.exemplo.com.br)."
  type        = string
}

variable "certificado_acm_arn" {
  description = "ARN do certificado TLS no ACM, na mesma região do load balancer."
  type        = string
}

variable "versao_imagem" {
  description = "Tag das imagens no ECR a implantar (normalmente o SHA do commit)."
  type        = string
}

variable "cidr_vpc" {
  description = "Faixa de IPs da VPC."
  type        = string
  default     = "10.20.0.0/16"
}

variable "api" {
  description = "Dimensionamento da API."
  type = object({
    cpu     = number
    memoria = number
    minimo  = number
    maximo  = number
  })
  default = { cpu = 512, memoria = 1024, minimo = 2, maximo = 20 }
}

variable "worker" {
  description = "Dimensionamento do Worker."
  type = object({
    cpu     = number
    memoria = number
    minimo  = number
    maximo  = number
  })
  default = { cpu = 256, memoria = 512, minimo = 2, maximo = 6 }
}

variable "banco" {
  description = "Configuração do PostgreSQL (RDS)."
  type = object({
    classe           = string
    armazenamento_gb = number
    multi_az         = bool
    retencao_backup  = number
  })
  default = {
    classe           = "db.t4g.medium"
    armazenamento_gb = 50
    multi_az         = true
    retencao_backup  = 14
  }
}

variable "redis_classe" {
  description = "Tipo de nó do ElastiCache (Redis/Valkey)."
  type        = string
  default     = "cache.t4g.small"
}

variable "mensageria_classe" {
  description = "Tipo de instância do Amazon MQ (RabbitMQ)."
  type        = string
  default     = "mq.m7g.medium"
}

variable "compradores_simultaneos" {
  description = "Limite de compradores simultâneos por evento na fila virtual."
  type        = number
  default     = 500
}

variable "email_alertas" {
  description = "E-mail que recebe os alarmes."
  type        = string
}

variable "limite_requisicoes_por_ip" {
  description = "Requisições permitidas por IP a cada 5 minutos no WAF."
  type        = number
  default     = 2000
}

variable "regiao_de_recuperacao" {
  description = "Região que recebe as cópias dos backups (us-east-1 = Virgínia do Norte)."
  type        = string
  default     = "us-east-1"
}
