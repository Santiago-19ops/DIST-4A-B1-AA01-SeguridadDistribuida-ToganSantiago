
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
