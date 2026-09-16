data "aws_availability_zones" "disponiveis" {
  state = "available"
}

locals {
  nome  = "ingressa-${var.ambiente}"
  zonas = slice(data.aws_availability_zones.disponiveis.names, 0, 2)
}

resource "aws_vpc" "principal" {
  cidr_block           = var.cidr_vpc
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = { Name = local.nome }
}

# Sub-redes públicas: só o load balancer e os NAT gateways.
resource "aws_subnet" "publica" {
  count                   = length(local.zonas)
  vpc_id                  = aws_vpc.principal.id
  availability_zone       = local.zonas[count.index]
  cidr_block              = cidrsubnet(var.cidr_vpc, 8, count.index)
  map_public_ip_on_launch = false

  tags = { Name = "${local.nome}-publica-${count.index}" }
}

# Sub-redes privadas: containers, banco, cache e mensageria. Sem acesso direto da internet.
resource "aws_subnet" "privada" {
  count             = length(local.zonas)
  vpc_id            = aws_vpc.principal.id
  availability_zone = local.zonas[count.index]
  cidr_block        = cidrsubnet(var.cidr_vpc, 8, count.index + 10)

  tags = { Name = "${local.nome}-privada-${count.index}" }
}

resource "aws_internet_gateway" "principal" {
  vpc_id = aws_vpc.principal.id
  tags   = { Name = local.nome }
}

resource "aws_eip" "nat" {
  count  = length(local.zonas)
  domain = "vpc"
  tags   = { Name = "${local.nome}-nat-${count.index}" }
}

# Um NAT por zona: se uma zona cair, a outra continua com saída para a internet.
resource "aws_nat_gateway" "principal" {
  count         = length(local.zonas)
  allocation_id = aws_eip.nat[count.index].id
  subnet_id     = aws_subnet.publica[count.index].id
  tags          = { Name = "${local.nome}-${count.index}" }

  depends_on = [aws_internet_gateway.principal]
}

resource "aws_route_table" "publica" {
  vpc_id = aws_vpc.principal.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.principal.id
  }

  tags = { Name = "${local.nome}-publica" }
}

resource "aws_route_table_association" "publica" {
  count          = length(local.zonas)
  subnet_id      = aws_subnet.publica[count.index].id
  route_table_id = aws_route_table.publica.id
}

resource "aws_route_table" "privada" {
  count  = length(local.zonas)
  vpc_id = aws_vpc.principal.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.principal[count.index].id
  }

  tags = { Name = "${local.nome}-privada-${count.index}" }
}

resource "aws_route_table_association" "privada" {
  count          = length(local.zonas)
  subnet_id      = aws_subnet.privada[count.index].id
  route_table_id = aws_route_table.privada[count.index].id
}

# Logs de tráfego da VPC, para investigação de incidentes.
resource "aws_flow_log" "vpc" {
  vpc_id               = aws_vpc.principal.id
  traffic_type         = "REJECT"
  log_destination_type = "cloud-watch-logs"
  log_destination      = aws_cloudwatch_log_group.flow_logs.arn
  iam_role_arn         = aws_iam_role.flow_logs.arn
}

resource "aws_cloudwatch_log_group" "flow_logs" {
  name              = "/ingressa/${var.ambiente}/vpc-flow-logs"
  retention_in_days = 30
  kms_key_id        = aws_kms_key.principal.arn
}

resource "aws_iam_role" "flow_logs" {
  name = "${local.nome}-flow-logs"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "vpc-flow-logs.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "flow_logs" {
  role = aws_iam_role.flow_logs.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams"]
      Resource = "${aws_cloudwatch_log_group.flow_logs.arn}:*"
    }]
  })
}
