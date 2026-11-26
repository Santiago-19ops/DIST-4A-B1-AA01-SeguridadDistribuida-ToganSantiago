# AA01 — Sistema distribuido de autenticación y seguridad

Actividad de Aplicaciones Distribuidas. Tres microservicios independientes en **ASP.NET Core Web API (.NET 10)**, **SQL Server LocalDB** y **Entity Framework Core**.

## Organización (igual que AD01)

- `Auth.Api/`, `Usuarios.Api/` y `Notificaciones.Api/`: código y migraciones de cada servicio.
- `01_Base_de_Datos/`: instrucciones para crear las tres bases con las migraciones incluidas.
- `03_Documentacion/`: diagrama de arquitectura, seis procesos distribuidos y matriz de 16 pruebas.
- `02_Postman/`: colección de 28 solicitudes y entorno sin valores secretos.
- `.gitignore`: excluye compilaciones y archivos privados.

## Servicios y bases de datos

| Proyecto | HTTPS (desarrollo) | Base de datos | Función |
|---|---|---|---|
| `Auth.Api` | `https://localhost:7272` | `AuthDB` | Identity, registro, login, JWT, Google, recuperación, roles y administración |
| `Usuarios.Api` | `https://localhost:7025` | `UsuariosDB` | Perfiles propios, consulta y edición protegida |
| `Notificaciones.Api` | `https://localhost:7124` | `NotificacionesDB` | SMTP Gmail, bienvenida, recuperación y persistencia del estado de correos |

La comunicación entre servicios se realiza con **IHttpClientFactory**, mediante HTTP/JSON y una clave interna de servicio. Cada API conserva su propio `DbContext`; no existen consultas directas a las tablas de otra API.

## Requisitos de ejecución

- Visual Studio compatible con .NET 10 o .NET SDK 10.
- SQL Server LocalDB `(localdb)\MSSQLLocalDB` o adaptar las cadenas de conexión locales.
- Certificado HTTPS de desarrollo confiable (`dotnet dev-certs https --trust`, si fuera necesario).
- Herramienta `dotnet ef` y paquetes restaurados mediante `dotnet restore`.
- Configurar los **User Secrets** de cada proyecto antes de ejecutar. **Nunca publicar sus valores.**

## Configuración privada (ejemplos de claves, sin valores reales)

En el directorio `Auth.Api` configurar con `dotnet user-secrets set`:

- `Jwt:Key`: firma JWT robusta (igual en Auth.Api y Usuarios.Api).
- `Security:InternalApiKey`: misma credencial interna que Usuarios.Api.
- `Security:NotificationsApiKey`: misma credencial interna que Notificaciones.Api.
- `Google:ClientId`: identificador público del cliente OAuth de prueba.

En `Usuarios.Api`:

- `Jwt:Key`: igual a Auth.Api.
- `Security:InternalApiKey`: igual a Auth.Api.

En `Notificaciones.Api`:

- `Security:NotificationsApiKey`: igual a Auth.Api.
- `Smtp:User`: cuenta de correo autorizada.
- `Smtp:AppPassword`: contraseña de aplicación SMTP (no publicar).
- `Servicios:AuthApi`: opcional, URL de Auth.Api para el enlace de recuperación; valor predeterminado en código `https://localhost:7272`.

**No se incluyen los valores secretos en este repositorio.** El `UserSecretsId` de los `.csproj` no es una contraseña.

## Crear bases de datos a partir de las migraciones

Ejecutar desde una terminal, dentro de **cada** proyecto, con la conexión SQL Server configurada:

```bash
cd Auth.Api
dotnet ef database update
cd ../Usuarios.Api
dotnet ef database update
cd ../Notificaciones.Api
dotnet ef database update
```

Las migraciones de EF Core se incluyen en las respectivas carpetas `Migrations/`.

## Ejecutar localmente

Cada API es una solución independiente (`.slnx`). Abrirlas en Visual Studio, o iniciar cada proyecto en una terminal distinta:

```bash
cd Auth.Api
dotnet run --launch-profile https
```

```bash
cd Usuarios.Api
dotnet run --launch-profile https
```

```bash
cd Notificaciones.Api
dotnet run --launch-profile https
```

Los puertos corresponden a los `launchSettings.json` actuales. En desarrollo, cada Swagger se encuentra en `/swagger`.

## Pruebas y documentación

- Colección: `02_Postman/AA01-Seguridad-Distribuida.postman_collection.json` (28 solicitudes).
- Entorno sin secretos: `02_Postman/AA01-Local.postman_environment.json`.
- Diagrama: `03_Documentacion/Arquitectura.md` (Mermaid visible en GitHub).
- Flujos con método, endpoint, DTO y errores: `03_Documentacion/Flujos.md`.
- Matriz de 16 casos del enunciado: `03_Documentacion/Pruebas.md`.
- Acceso Google de prueba por navegador: `https://localhost:7272/google-login.html`.
- Enlace de recuperación: `https://localhost:7272/restablecer-contrasena.html` (requiere abrirlo desde el equipo donde se ejecuta la API, salvo despliegue adicional).

**Atención:** la colección exportada no contiene JWT ni contraseñas reales; para pruebas nuevas debe configurarse un entorno **privado/local**. No ejecutar automáticamente toda la colección: varios tests cambian roles, deshabilitan cuentas o requieren apagar servicios, y el caso de timeout requiere simular latencia en `Usuarios.Api`.

## Actualizaciones de autenticacion

- Google: permite iniciar sesion con cuentas vinculadas y registrar nuevas cuentas verificadas con rol Cliente.
- Seguridad: no vincula automaticamente una cuenta local existente solamente por coincidencia del correo.
- Administrador inicial: puede aprovisionarse mediante User Secrets cuando se habilita la configuracion correspondiente.
- Verificacion: Auth.Api compilo y arranco correctamente. La creacion inicial en una base nueva sigue pendiente de prueba.
## Puntos que requieren verificación antes de entregar

1. **Administrador inicial:** `Auth.Api/Program.cs` incluye aprovisionamiento opcional y seguro mediante User Secrets. En la base actual esta desactivado porque ya existe un administrador. Falta comprobar la creacion inicial en una base de datos nueva.
2. Verificar que se puede restaurar y compilar cada solución en el equipo con .NET 10; este ZIP fue inspeccionado estáticamente, no ejecutado contra LocalDB/Gmail.
3. Revisar que no se publiquen capturas con tokens activos, secretos o contraseñas reales.
4. Elaborar el **PDF de declaración de uso de IA**, con prompts reales, fuentes, modificaciones propias y pruebas; es un entregable externo al código.
5. Añadir evidencias concretas de las pruebas si las solicita el docente; el JSON de Postman contiene solicitudes, no resultados guardados.

## Alcance

`Catalogo.Api`, `Inventario.Api` y `Pedidos.Api` corresponden a la actividad anterior AD01; no es necesario reconstruirlos en esta entrega AA01. No se necesita Docker, API Gateway, RabbitMQ ni despliegue público.

