# Matriz de pruebas — 16 casos del enunciado

Este archivo documenta los **resultados esperados**. La colección Postman permite ejecutar las solicitudes, pero la exportación no incluye respuestas guardadas ni capturas. Los casos que requieren apagar APIs o usar Google/Gmail deben demostrarse manualmente.

| # | Prueba | Resultado esperado |
|---|---|---|
| 1 | Registro válido | 201; identidad AuthDB, perfil UsuariosDB, rol Cliente |
| 2 | Correo duplicado o datos inválidos | 400 y ausencia de duplicados |
| 3 | Login válido, incorrecto e intentos fallidos | JWT o 401; lockout tras intentos repetidos |
| 4 | Consultar perfil sin JWT | 401 |
| 5 | Cliente accede a ruta administrativa de perfiles | 403 |
| 6 | Admin cambia rol o estado | 200 y persistencia de cambios |
| 7 | Google real desde navegador | ID token verificado y JWT local |
| 8 | ID token Google falso | 401 sin sesión |
| 9 | Recuperación por email real | Enlace recibido en correo |
| 10 | Restablecer token válido | Contraseña actualizada y login nuevo exitoso |
| 11 | Token de recuperación alterado | 400 |
| 12 | Registro con Usuarios.Api apagada | 503 y cuenta pendiente |
| 13 | Recuperación con Notificaciones.Api apagada | Sin filtración de token; respuesta genérica |
| 14 | Latencia mayor que HttpClient.Timeout | Error controlado; distinguir timeout de HTTP 504 |
| 15 | Correo interno sin X-Internal-Key | 401 |
| 16 | Cambio de contraseña con contraseña actual incorrecta | 400 sin cambiar hash |

**Precaución:** no ejecutar masivamente estas pruebas: alterar roles, deshabilitar usuarios, consumir enlaces de recuperación y crear cuentas tiene efectos reales en las bases de datos.
