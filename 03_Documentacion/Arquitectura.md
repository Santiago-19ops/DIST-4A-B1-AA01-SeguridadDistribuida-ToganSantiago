# Diagrama de arquitectura — AA01

```mermaid
flowchart TB
    U[Usuario / Postman / Navegador] --> A[Auth.Api — HTTPS :7272]
    U --> P[Usuarios.Api — HTTPS :7025]
    G[Google Identity / OpenID Connect] --> A
    A -->|HTTP JSON · IHttpClientFactory · X-Internal-Key| P
    A -->|HTTP JSON · IHttpClientFactory · X-Internal-Key| N[Notificaciones.Api — HTTPS :7124]
    A --> DB1[(AuthDB / ASP.NET Identity)]
    P --> DB2[(UsuariosDB / Perfiles)]
    N --> DB3[(NotificacionesDB / EnviosCorreo)]
    N -->|SMTP TLS| S[Gmail / Correo real]
    A -->|JWT de la aplicación| U
    U -->|Bearer JWT| P
```

- Las tres APIs funcionan como proyectos independientes, con bases de datos aisladas.
- `Auth.Api` genera y firma JWT; `Usuarios.Api` verifica la firma, el emisor, la audiencia y la expiración.
- `Notificaciones.Api` no expone el envío libremente: requiere una clave interna de servicio.
- Este diagrama muestra las conexiones funcionales de la implementación; no sustituye las pruebas realizadas en clase.
