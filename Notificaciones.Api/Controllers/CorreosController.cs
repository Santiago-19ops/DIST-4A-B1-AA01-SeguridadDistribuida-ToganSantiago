
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MimeKit.Text;
using Notificaciones.Api.Data;
using Notificaciones.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Notificaciones.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CorreosController : ControllerBase
    {
        private readonly NotificacionesDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CorreosController> _logger;

        public CorreosController(
            NotificacionesDbContext context,
            IConfiguration configuration,
            ILogger<CorreosController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        // POST: api/Correos/interno/bienvenida
        [HttpPost("interno/bienvenida")]
        public async Task<IActionResult> EnviarBienvenida(
            [FromBody] BienvenidaRequest datos,
            [FromHeader(Name = "X-Internal-Key")] string? clave)
        {
            var validacion = ValidarClaveInterna(clave);

            if (validacion != null)
                return validacion;

            string texto =
                "Hola " + datos.Nombre.Trim() + "," +
                "\n\nBienvenido al sistema AA01." +
                "\nTu cuenta ha sido registrada." +
                "\n\nSaludos.";

            return await EnviarCorreo(
                datos.CorreoDestino,
                "Bienvenido a AA01",
                texto);
        }

        // POST: api/Correos/interno/recuperacion
        [HttpPost("interno/recuperacion")]
        public async Task<IActionResult> EnviarRecuperacion(
            [FromBody] RecuperacionCorreoRequest datos,
            [FromHeader(Name = "X-Internal-Key")] string? clave)
        {
            var validacion = ValidarClaveInterna(clave);

            if (validacion != null)
                return validacion;

            // Direccion de Auth.Api para abrir la pagina
            string baseAuth = _configuration["Servicios:AuthApi"]
                ?? "https://localhost:7272";

            // Codificar el correo y el codigo dentro del enlace
            string enlace = baseAuth.TrimEnd('/') +
                "/restablecer-contrasena.html?correo=" +
                Uri.EscapeDataString(datos.CorreoDestino.Trim()) +
                "&codigo=" + Uri.EscapeDataString(datos.Codigo);

            string texto =
                "Hola," +
                "\n\nRecibimos una solicitud para restablecer " +
                "la contraseña de tu cuenta AA01." +
                "\n\nAbre este enlace desde tu computadora:" +
                "\n\n" + enlace +
                "\n\nSi no solicitaste este cambio, " +
                "puedes ignorar este correo.";

            string html =
                "<p>Hola,</p>" +
                "<p>Recibimos una solicitud para restablecer " +
                "la contraseña de tu cuenta AA01.</p>" +
                "<p><a href=\"" + WebUtility.HtmlEncode(enlace) +
                "\">Restablecer mi contraseña</a></p>" +
                "<p>Abre el enlace desde la computadora " +
                "donde se ejecuta Auth.Api.</p>" +
                "<p>Si no solicitaste este cambio, " +
                "puedes ignorar este correo.</p>";

            return await EnviarCorreo(
                datos.CorreoDestino,
                "Recuperacion de contraseña AA01",
                texto,
                html);
        }

        // Validar que la peticion venga de una API autorizada
        private IActionResult? ValidarClaveInterna(string? clave)
        {
            var claveEsperada =
                _configuration["Security:NotificationsApiKey"];

            if (string.IsNullOrWhiteSpace(claveEsperada))
                return StatusCode(503,
                    "Falta configurar la clave interna.");

            var recibida = Encoding.UTF8.GetBytes(clave ?? "");
            var esperada = Encoding.UTF8.GetBytes(claveEsperada);

            if (recibida.Length != esperada.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    recibida, esperada))
            {
                return Unauthorized("Servicio no autorizado.");
            }

            return null;
        }

        // Enviar correo y guardar el resultado en SQL Server
        private async Task<IActionResult> EnviarCorreo(
            string correoDestino,
            string asunto,
            string texto,
            string? html = null)
        {
            var usuario = _configuration["Smtp:User"];
            var contrasena = _configuration["Smtp:AppPassword"];

            if (string.IsNullOrWhiteSpace(usuario) ||
                string.IsNullOrWhiteSpace(contrasena))
            {
                return StatusCode(503,
                    "Falta configurar Gmail.");
            }

            var registro = new EnvioCorreo
            {
                CorreoDestino = correoDestino.Trim(),
                Asunto = asunto,
                Estado = "Pendiente",
                FechaEnvio = DateTime.UtcNow
            };

            _context.EnviosCorreo.Add(registro);
            await _context.SaveChangesAsync();

            try
            {
                var mensaje = new MimeMessage();

                mensaje.From.Add(
                    MailboxAddress.Parse(usuario));

                mensaje.To.Add(
                    MailboxAddress.Parse(correoDestino.Trim()));

                mensaje.Subject = asunto;

                // Recuperacion: texto y enlace HTML
                // Bienvenida: solo texto, como anteriormente
                if (html != null)
                {
                    var contenido = new BodyBuilder
                    {
                        TextBody = texto,
                        HtmlBody = html
                    };

                    mensaje.Body = contenido.ToMessageBody();
                }
                else
                {
                    mensaje.Body = new TextPart(TextFormat.Plain)
                    {
                        Text = texto
                    };
                }

                using var smtp = new SmtpClient();

                await smtp.ConnectAsync(
                    "smtp.gmail.com",
                    587,
                    SecureSocketOptions.StartTls);

                await smtp.AuthenticateAsync(
                    usuario,
                    contrasena.Replace(" ", ""));

                await smtp.SendAsync(mensaje);
                await smtp.DisconnectAsync(true);

                registro.Estado = "Enviado";
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    Mensaje = "Correo enviado correctamente.",
                    IdEnvio = registro.IdEnvio,
                    Estado = registro.Estado
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al enviar el correo.");

                registro.Estado = "Fallido";
                await _context.SaveChangesAsync();

                return StatusCode(503, new
                {
                    Mensaje = "No se pudo enviar el correo.",
                    IdEnvio = registro.IdEnvio,
                    Estado = registro.Estado
                });
            }
        }
    }

    public class BienvenidaRequest
    {
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string CorreoDestino { get; set; } = string.Empty;
    }

    public class RecuperacionCorreoRequest
    {
        [Required]
        [EmailAddress]
        public string CorreoDestino { get; set; } = string.Empty;

        [Required]
        public string Codigo { get; set; } = string.Empty;
    }
}
