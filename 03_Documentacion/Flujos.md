# Seis procesos de negocio distribuidos

Cada flujo identifica la API iniciadora, receptor, método, JSON principal, resultado y falla prevista.

## 1. Registro de Cliente y bienvenida

- **Iniciador:** cliente → `Auth.Api`, `POST /api/Auth/registro`.
- **DTO de entrada:** `{ "nombre": "Ejemplo", "correo": "cliente@example.com", "contrasena": "[valor privado]", "confirmarContrasena": "[valor privado]" }`.
- **Receptor 1:** `Usuarios.Api`, `POST /api/Perfiles/interno` desde Auth con `IHttpClientFactory` y `X-Internal-Key`.
- **JSON interno:** `{ "usuarioId": "[id de Identity]", "nombre": "Ejemplo", "correo": "cliente@example.com" }`.
- **Receptor 2:** `Notificaciones.Api`, `POST /api/Correos/interno/bienvenida` desde Auth con clave interna.
- **JSON correo:** `{ "nombre": "Ejemplo", "correoDestino": "cliente@example.com" }`.
- **Esperado:** `201` después de crear identidad y perfil; rol `Cliente`; correo real y registro del estado de envío.
- **Errores:** validación o correo duplicado → `400`; `Usuarios.Api` inaccesible o timeout → `503` y cuenta pendiente (no activa); fallo de notificaciones → registro persiste y se indica que el correo no se envió.

## 2. Inicio de sesión local y acceso al perfil

- **Iniciador:** cliente → `Auth.Api`, `POST /api/Auth/login`.
- **DTO:** `{ "correo": "cliente@example.com", "contrasena": "[valor privado]" }`.
- **Esperado:** `200` con JWT local firmado, tipo Bearer y expiración.
- **Receptor posterior:** cliente → `Usuarios.Api`, `GET /api/Perfiles/mi-perfil` con `Authorization: Bearer [JWT]`.
- **Resultado:** `200` con el perfil propio.
- **Errores:** credenciales inválidas/cuenta bloqueada → `401`; acceso a perfil sin JWT → `401`, o acceso a ruta administrativa sin rol → `403`.

## 3. Google y vinculación segura

- **Iniciador:** navegador (`Auth.Api/wwwroot/google-login.html`) autenticado ante Google.
- **Proveedor:** Google Identity entrega un ID token; `Auth.Api` lo valida con `GoogleJsonWebSignature.ValidateAsync` y comprueba `EmailVerified` y `Subject`.
- **Vinculación:** `POST /api/Auth/google-vincular`, con JWT propio previo y `{ "idToken": "[token real, no publicar]" }`.
- **Inicio Google:** `POST /api/Auth/google-login`, cuerpo `{ "idToken": "[token real, no publicar]" }`.
- **Esperado:** identidad externa asociada por `sub`; JWT **propio** de AA01, no el ID token de Google.
- **Errores:** Google token falso → `401`; cuenta Google no vinculada → `409`; correo de vinculación que no coincide → `400`.

## 4. Recuperación y restablecimiento de contraseña

- **Iniciador:** cliente → `Auth.Api`, `POST /api/Auth/olvide-contrasena` con `{ "correo": "cliente@example.com" }`.
- **Receptor:** `Notificaciones.Api`, `POST /api/Correos/interno/recuperacion`, JSON `{ "correoDestino": "cliente@example.com", "codigo": "[token temporal]" }` más clave interna.
- **Esperado:** `200` con mensaje genérico, independientemente de si el correo existe; envío real del enlace desde SMTP.
- **Segundo paso:** cliente → `Auth.Api`, `POST /api/Auth/restablecer-contrasena`, cuerpo `{ "correo": "cliente@example.com", "codigo": "[temporal]", "nuevaContrasena": "[privada]", "confirmarContrasena": "[privada]" }`.
- **Esperado:** `200` con contraseña actualizada por Identity; token alterado/consumido → `400`.
- **Errores:** fallo de notificaciones → se conserva respuesta genérica y no se expone el token en la respuesta ni en logs.

## 5. Gestión del perfil propio

- **Iniciador:** cliente autenticado → `Usuarios.Api`, `GET /api/Perfiles/mi-perfil`.
- **Actualización:** `PUT /api/Perfiles/mi-perfil`, con `{ "nombre": "Nombre Nuevo" }` y Bearer JWT.
- **Esperado:** `200` con perfil actualizado; se identifica al usuario mediante claims del JWT, sin recibir un id arbitrario que permita modificar otro perfil.
- **Errores:** falta de JWT → `401`; perfil inexistente → `404`; ruta de todos los perfiles protegida por `Administrador`, otro rol → `403`.

## 6. Administración de usuarios y roles

- **Iniciador:** usuario Administrador → `Auth.Api` con Bearer JWT y rol `Administrador`.
- **Listar:** `GET /api/Administracion/usuarios` → `200` con usuarios, roles y estados.
- **Cambiar rol:** `PUT /api/Administracion/usuarios/{id}/rol`, JSON `{ "rol": "Administrador" }` o `"Cliente"`.
- **Habilitar/deshabilitar:** `PUT /api/Administracion/usuarios/{id}/estado`, JSON `{ "habilitado": false }` o `true`.
- **Errores:** Cliente → `403`; usuario no existente → `404`; estado o rol inválido y último administrador activo → `400`; no activar una cuenta pendiente sin perfil.
