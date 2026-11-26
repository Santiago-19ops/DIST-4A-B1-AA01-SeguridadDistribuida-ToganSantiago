
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

// Crear los roles iniciales
using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

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
}

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
