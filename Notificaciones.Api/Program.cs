
using Microsoft.EntityFrameworkCore;
using Notificaciones.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Controladores
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Base de datos NotificacionesDB
builder.Services.AddDbContext<NotificacionesDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("NotificacionesConnection")
    )
);

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
