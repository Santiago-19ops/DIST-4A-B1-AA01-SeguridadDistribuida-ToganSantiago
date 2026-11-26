
using Auth.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Controladores
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Base de datos AuthDB
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("AuthConnection")
    )
);

// Comunicacion con Usuarios.Api
builder.Services.AddHttpClient("UsuariosApi", cliente =>
{
    var direccion = builder.Configuration["Servicios:UsuariosApi"]
        ?? throw new Exception("Falta configurar UsuariosApi.");

    cliente.BaseAddress = new Uri(direccion);
    cliente.Timeout = TimeSpan.FromSeconds(30);
});

// Comunicacion con Notificaciones.Api
builder.Services.AddHttpClient("NotificacionesApi", cliente =>
{
    var direccion = builder.Configuration["Servicios:NotificacionesApi"]
        ?? throw new Exception("Falta configurar NotificacionesApi.");

    cliente.BaseAddress = new Uri(direccion);
    cliente.Timeout = TimeSpan.FromSeconds(30);
});

// Proteccion de datos
builder.Services.AddDataProtection();

// ASP.NET Core Identity
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;

    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;

    options.Lockout.DefaultLockoutTimeSpan =
        TimeSpan.FromMinutes(5);

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AuthDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Configurar autenticacion JWT
var claveJwt = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("Falta la clave JWT en User Secrets.");

var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new Exception("Falta Jwt:Issuer.");

var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new Exception("Falta Jwt:Audience.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(claveJwt)),

                ClockSkew = TimeSpan.FromMinutes(1)
            };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ========================================================
// CREACION DE ROLES Y ADMINISTRADOR INICIAL
// ========================================================

// Crear un alcance para utilizar Identity y la base de datos
using (var scope = app.Services.CreateScope())
{
    // Servicio que administra los roles
    var roles = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

    // Servicio que administra los usuarios
    var usuarios = scope.ServiceProvider
        .GetRequiredService<UserManager<IdentityUser>>();

    // PASO 1: Crear los roles si todavia no existen
    string[] nombresRoles = { "Cliente", "Administrador" };

    foreach (var nombre in nombresRoles)
    {
        if (!await roles.RoleExistsAsync(nombre))
        {
            var resultado = await roles.CreateAsync(
                new IdentityRole(nombre));

            if (!resultado.Succeeded)
                throw new Exception(
                    "No se pudo crear el rol " + nombre);
        }
    }

    // PASO 2: Comprobar si se habilito la creacion inicial
    // Esta configuracion se guardara en User Secrets.
    // No se coloca la contraseña en appsettings.json.
    bool crearAdminInicial = builder.Configuration
        .GetValue<bool>("AdminInicial:Habilitado");

    if (crearAdminInicial)
    {
        // PASO 3: Consultar los administradores existentes
        var administradores = await usuarios
            .GetUsersInRoleAsync("Administrador");

        // Solo crear el administrador si no existe ninguno
        // Esto evita duplicarlo al reiniciar Auth.Api.
        if (administradores.Count == 0)
        {
            // PASO 4: Leer las credenciales desde User Secrets
            string? correoAdmin = builder.Configuration[
                "AdminInicial:Correo"]?.Trim();

            string? contrasenaAdmin = builder.Configuration[
                "AdminInicial:Contrasena"];

            // Comprobar que esten configuradas
            if (string.IsNullOrWhiteSpace(correoAdmin) ||
                string.IsNullOrWhiteSpace(contrasenaAdmin))
            {
                throw new Exception(
                    "Faltan las credenciales del administrador " +
                    "inicial en User Secrets.");
            }

            // SEGURIDAD:
            // Si ya existe una cuenta con ese correo,
            // no le otorgamos privilegios automaticamente.
            var cuentaExistente = await usuarios
                .FindByEmailAsync(correoAdmin);

            if (cuentaExistente != null)
            {
                throw new Exception(
                    "Ya existe una cuenta con el correo " +
                    "configurado para el administrador inicial. " +
                    "No se modificaron sus permisos.");
            }

            // PASO 5: Preparar la identidad del administrador
            var administrador = new IdentityUser
            {
                UserName = correoAdmin,
                Email = correoAdmin,
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            // PASO 6: Crear el usuario con contraseña protegida
            // Identity genera el hash automaticamente.
            // La contraseña NO se guarda en texto plano.
            var resultadoUsuario = await usuarios.CreateAsync(
                administrador, contrasenaAdmin);

            if (!resultadoUsuario.Succeeded)
            {
                throw new Exception(
                    "No se pudo crear el administrador inicial: " +
                    string.Join(", ",
                        resultadoUsuario.Errors
                            .Select(e => e.Description)));
            }

            // PASO 7: Asignar el rol Administrador
            var resultadoRol = await usuarios.AddToRoleAsync(
                administrador, "Administrador");

            if (!resultadoRol.Succeeded)
            {
                // Si falla el rol, eliminar la cuenta incompleta
                await usuarios.DeleteAsync(administrador);

                throw new Exception(
                    "No se pudo asignar el rol Administrador.");
            }

            // Registrar solamente que se creo correctamente
            // No mostrar correo, contraseña ni claves secretas
            app.Logger.LogInformation(
                "Administrador inicial creado correctamente.");
        }
    }
}

// ========================================================
// CONFIGURACION DE LA APLICACION
// ========================================================

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Permitir la pagina HTML del inicio con Google
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
