
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Usuarios.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Controladores
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Base de datos UsuariosDB
builder.Services.AddDbContext<UsuariosDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("UsuariosConnection")
    )
);

// NUEVO: Comunicacion con Auth.Api.
// Sirve para consultar el rol y el estado ACTUALES del usuario.
builder.Services.AddHttpClient("AuthApi", cliente =>
{
    var direccion = builder.Configuration["Servicios:AuthApi"]
        ?? throw new Exception("Falta configurar Servicios:AuthApi.");

    cliente.BaseAddress = new Uri(direccion);

    // Evitar esperas demasiado largas si Auth.Api no responde.
    cliente.Timeout = TimeSpan.FromSeconds(10);
});

// Configurar autenticacion JWT
var claveJwt = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("Falta Jwt:Key en User Secrets.");

var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new Exception("Falta Jwt:Issuer.");

var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new Exception("Falta Jwt:Audience.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // VALIDACION ORIGINAL DEL JWT
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateLifetime = true,
                RequireExpirationTime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(claveJwt)),

                ClockSkew = TimeSpan.FromMinutes(1)
            };

        // NUEVO: comprobar los permisos actuales despues
        // de verificar que el JWT sea valido.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Obtener la identidad del JWT validado.
                if (context.Principal?.Identity
                    is not ClaimsIdentity identidad)
                {
                    context.Fail("Identidad no valida.");
                    return;
                }

                // Obtener el identificador del usuario.
                var usuarioId = context.Principal.FindFirst(
                    ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrWhiteSpace(usuarioId))
                {
                    context.Fail("Falta el identificador del usuario.");
                    return;
                }

                // Clave privada para comunicacion entre APIs.
                // Ya se utiliza en nuestro proyecto.
                var configuracion = context.HttpContext
                    .RequestServices
                    .GetRequiredService<IConfiguration>();

                var claveInterna =
                    configuracion["Security:InternalApiKey"];

                if (string.IsNullOrWhiteSpace(claveInterna))
                {
                    context.Fail("Falta la clave interna.");
                    return;
                }

                try
                {
                    // Crear el cliente HTTP configurado.
                    var fabrica = context.HttpContext
                        .RequestServices
                        .GetRequiredService<IHttpClientFactory>();

                    var cliente = fabrica.CreateClient("AuthApi");

                    // Consultar AuthDB a traves de Auth.Api.
                    // Usuarios.Api NO accede directamente
                    // a la base de datos de Auth.Api.
                    using var solicitud = new HttpRequestMessage(
                        HttpMethod.Get,
                        "api/interno/permisos/" +
                        Uri.EscapeDataString(usuarioId));

                    // Proteger la comunicacion interna.
                    solicitud.Headers.Add(
                        "X-Internal-Key", claveInterna);

                    using var respuesta = await cliente.SendAsync(
                        solicitud,
                        context.HttpContext.RequestAborted);

                    // Si Auth.Api rechaza la consulta,
                    // no confiamos en los permisos antiguos.
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        context.Fail(
                            "No se pudieron verificar los permisos.");
                        return;
                    }

                    // Leer el estado y los roles actuales.
                    var estado = await respuesta.Content
                        .ReadFromJsonAsync<EstadoActualResponse>(
                            cancellationToken:
                            context.HttpContext.RequestAborted);

                    // Bloquear usuarios eliminados, pendientes,
                    // deshabilitados o bloqueados.
                    if (estado == null ||
                        !estado.Habilitado ||
                        estado.Roles == null)
                    {
                        context.Fail("Cuenta no habilitada.");
                        return;
                    }

                    // IMPORTANTE:
                    // Eliminar los roles ANTIGUOS del JWT.
                    var tipoRol = identidad.RoleClaimType;

                    var rolesAnteriores = identidad
                        .FindAll(tipoRol)
                        .ToList();

                    foreach (var rolAnterior in rolesAnteriores)
                    {
                        identidad.RemoveClaim(rolAnterior);
                    }

                    // Agregar los roles ACTUALES de AuthDB.
                    foreach (var rolActual in estado.Roles.Distinct())
                    {
                        identidad.AddClaim(
                            new Claim(tipoRol, rolActual));
                    }

                    // Si el JWT antiguo dice Administrador,
                    // pero AuthDB dice Cliente, ahora se
                    // aplican los permisos de Cliente.
                }
                catch (Exception)
                {
                    // Si Auth.Api no responde, ocurre timeout
                    // o la respuesta es incorrecta, rechazamos
                    // el acceso en lugar de confiar en el JWT.
                    context.Fail(
                        "No fue posible validar los permisos actuales.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Primero autenticacion y despues autorizacion
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// DTO: recibir el estado actual desde Auth.Api.
public class EstadoActualResponse
{
    public bool Habilitado { get; set; }

    public string[] Roles { get; set; } =
        Array.Empty<string>();
}
