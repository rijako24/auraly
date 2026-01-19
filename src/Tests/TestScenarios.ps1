# Scripts de prueba para simular escenarios de clientes
# Ejecutar después de configurar el webhook y las credenciales

param(
    [string]$WebhookUrl = "http://localhost:7071/api/WhatsAppWebhook",
    [string]$TestPhoneNumber = "1234567890"
)

$headers = @{
    "Content-Type" = "application/json"
}

# Escenario 1: Cliente curioso - Saludo inicial
Write-Host "`n=== Escenario 1: Cliente curioso ===" -ForegroundColor Green
$body1 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-1"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-1"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "Hola, quiero información para mi bebé"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body1
Start-Sleep -Seconds 2

# Escenario 2: Cliente indeciso - Pregunta por edad
Write-Host "`n=== Escenario 2: Cliente indeciso ===" -ForegroundColor Green
$body2 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-2"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-2"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "Mi bebé tiene 4 meses"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body2
Start-Sleep -Seconds 2

# Escenario 3: Cliente directo - Pregunta precios
Write-Host "`n=== Escenario 3: Cliente directo ===" -ForegroundColor Green
$body3 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-3"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-3"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "¿Cuánto cuestan los planes?"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body3
Start-Sleep -Seconds 2

# Escenario 4: Objeción - Dudas de seguridad
Write-Host "`n=== Escenario 4: Objeción ===" -ForegroundColor Green
$body4 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-4"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-4"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "¿Es seguro para bebés tan pequeños?"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body4
Start-Sleep -Seconds 2

# Escenario 5: Reserva directa
Write-Host "`n=== Escenario 5: Reserva directa ===" -ForegroundColor Green
$body5 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-5"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-5"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "Quiero reservar el Plan Premium"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body5
Start-Sleep -Seconds 2

# Escenario 6: Solicitud de humano
Write-Host "`n=== Escenario 6: Solicitud de humano ===" -ForegroundColor Green
$body6 = @{
    object = "whatsapp_business_account"
    entry = @(
        @{
            id = "test-entry-6"
            changes = @(
                @{
                    field = "messages"
                    value = @{
                        messaging_product = "whatsapp"
                        messages = @(
                            @{
                                from = $TestPhoneNumber
                                id = "test-msg-6"
                                timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds().ToString()
                                type = "text"
                                text = @{
                                    body = "Quiero hablar con un humano"
                                }
                            }
                        )
                        contacts = @(
                            @{
                                profile = @{
                                    name = "María González"
                                }
                                wa_id = $TestPhoneNumber
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri $WebhookUrl -Method Post -Headers $headers -Body $body6

Write-Host "`n=== Pruebas completadas ===" -ForegroundColor Yellow
Write-Host "Revisa los logs y la base de datos para verificar los resultados" -ForegroundColor Cyan
