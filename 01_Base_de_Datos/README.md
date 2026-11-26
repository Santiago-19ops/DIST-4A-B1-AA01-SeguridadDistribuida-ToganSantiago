# Bases de datos de la AA01

Se utilizan tres bases de datos SQL Server LocalDB independientes:

| API | Base de datos | Migraciones originales |
| --- | --- | --- |
| Auth.Api | `AuthDB` | `../Auth.Api/Migrations/` |
| Usuarios.Api | `UsuariosDB` | `../Usuarios.Api/Migrations/` |
| Notificaciones.Api | `NotificacionesDB` | `../Notificaciones.Api/Migrations/` |

**No se requieren scripts SQL adicionales:** el enunciado admite **scripts SQL o migraciones** y los tres proyectos ya incluyen las migraciones de Entity Framework Core.

Desde la raíz del repositorio y con LocalDB instalado, secretos locales configurados y `dotnet-ef` disponible, ejecutar por separado:

```powershell
dotnet ef database update --project Auth.Api/Auth.Api.csproj
dotnet ef database update --project Usuarios.Api/Usuarios.Api.csproj
dotnet ef database update --project Notificaciones.Api/Notificaciones.Api.csproj
```

Estas migraciones generan las tablas definidas por cada API en su propia base de datos; no hay claves foráneas entre las bases.
