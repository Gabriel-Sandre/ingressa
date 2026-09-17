resource "aws_s3_bucket" "logs_alb" {
  bucket        = "${local.nome}-logs-alb-${data.aws_caller_identity.atual.account_id}"
  force_destroy = var.ambiente != "producao"
}

resource "aws_s3_bucket_public_access_block" "logs_alb" {
  bucket                  = aws_s3_bucket.logs_alb.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_lifecycle_configuration" "logs_alb" {
  bucket = aws_s3_bucket.logs_alb.id

  rule {
    id     = "expirar"
    status = "Enabled"
    filter {}
    expiration {
      days = 90
    }
  }
}

data "aws_elb_service_account" "regiao" {}

resource "aws_s3_bucket_policy" "logs_alb" {
  bucket = aws_s3_bucket.logs_alb.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { AWS = data.aws_elb_service_account.regiao.arn }
      Action    = "s3:PutObject"
      Resource  = "${aws_s3_bucket.logs_alb.arn}/*"
    }]
  })
}

resource "aws_lb" "principal" {
  name                       = local.nome
  load_balancer_type         = "application"
  subnets                    = aws_subnet.publica[*].id
  security_groups            = [aws_security_group.alb.id]
  drop_invalid_header_fields = true
  enable_deletion_protection = var.ambiente == "producao"
  idle_timeout               = 60

  access_logs {
    bucket  = aws_s3_bucket.logs_alb.id
    enabled = true
  }

  depends_on = [aws_s3_bucket_policy.logs_alb]
}

resource "aws_lb_target_group" "alvos" {
  for_each = { api = "/health/ready", web = "/" }

  name                 = "${local.nome}-${each.key}"
  port                 = 8080
  protocol             = "HTTP"
  target_type          = "ip"
  vpc_id               = aws_vpc.principal.id
  deregistration_delay = 30

  health_check {
    path                = each.value
    matcher             = "200"
    interval            = 15
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.principal.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"
    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.principal.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.certificado_acm_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.alvos["web"].arn
  }
}

# /api/* vai direto para a API; o resto, para a interface.
resource "aws_lb_listener_rule" "api" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 10

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.alvos["api"].arn
  }

  condition {
    path_pattern {
      values = ["/api/*"]
    }
  }
}
