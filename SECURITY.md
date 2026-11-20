# Guía de Seguridad y Operación — OmniStock API

## 1. Gestión de `Security:EncryptionKey` (Cifrado SMTP AES-256-GCM)
- **Generación:** Generar una clave aleatoria de al menos 32 bytes (256 bits), independiente de `Jwt:Key`:
  ```powershell
  [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Minimum 0 -Maximum 256) }))