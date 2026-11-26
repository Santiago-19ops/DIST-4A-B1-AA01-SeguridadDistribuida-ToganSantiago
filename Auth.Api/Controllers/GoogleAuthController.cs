
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/Auth")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _usuarios;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GoogleAuthController> _logger;

        public GoogleAuthController(
            UserManager<IdentityUser> usuarios,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<GoogleAuthController> logger)
        {
            _usuarios = usuarios;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // GET: api/Auth/google-config
        // Entregar el ClientId publico a nuestra pagina HTML
        [HttpGet("google-config")]
        public IActionResult GoogleConfig()
        {
            var clientId = _configuration["Google:ClientId"];

            if (string.IsNullOrWhiteSpace(clientId))
                return StatusCode(503, "Falta Google:ClientId.");

            return Ok(new { ClientId = clientId });
        }

        // Comprobar que el token realmente viene de Google
        private async Task<GoogleJsonWebSignature.Payload?>
            ValidarGoogle(string idToken)
        {
            var clientId = _configuration["Google:ClientId"];

            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException(
                    "Falta Google:ClientId.");

            try
            {
                var configuracion =
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { clientId }
                    };

                var datos = await GoogleJsonWebSignature
                    .ValidateAsync(idToken, configuracion);

                // El correo debe estar verificado por Google
                // y debemos tener el identificador unico (sub).
                if (!datos.EmailVerified ||
                    string.IsNullOrWhiteSpace(datos.Email) ||
                    string.IsNullOrWhiteSpace(datos.Subject))
                {
                    return null;
                }

                return datos;
            }
            catch (InvalidJwtException)
            {
                return null;
            }
        }

        // POST: api/Auth/google-vincular
        // Vincular Google con una cuenta local existente.
        // El usuario primero debe autenticarse con su JWT.
        [Authorize]
        [HttpPost("google-vincular")]
        public async Task<IActionResult> VincularGoogle(
            [FromBody] GoogleTokenRequest datos)
        {
            var google = await ValidarGoogle(datos.IdToken);

            if (google == null)
                return Unauthorized("Token de Google invalido.");

            var usuarioId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(usuarioId))
                return Unauthorized();

            var usuario = await _usuarios.FindByIdAsync(usuarioId);

            if (usuario == null)
                return Unauthorized();

            // No permitir vincular un correo Google diferente
            if (!string.Equals(
                usuario.Email,
                google.Email,
                StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "El correo de Google no coincide con tu cuenta.");
            }

            // Identificar la cuenta por el ID estable de Google
            var vinculada = await _usuarios.FindByLoginAsync(
                "Google", google.Subject);

            if (vinculada != null)
            {
                if (vinculada.Id == usuario.Id)
                    return Ok(new
                    {
                        Mensaje = "Google ya estaba vinculado."
                    });

                return Conflict(
                    "Esta cuenta Google pertenece a otro usuario.");
            }

            var resultado = await _usuarios.AddLoginAsync(
                usuario,
                new UserLoginInfo(
                    "Google", google.Subject, "Google"));

            if (!resultado.Succeeded)
                return BadRequest(
                    "No se pudo vincular Google.");

            return Ok(new
            {
                Mensaje = "Cuenta Google vinculada correctamente."
            });
        }

        // POST: api/Auth/google-login
        // Iniciar sesion o registrar una cuenta nueva de Google
        [HttpPost("google-login")]
        public async Task<IActionResult> LoginGoogle(
            [FromBody] GoogleTokenRequest datos)
        {
            // PASO 1: Verificar el token con Google
            var google = await ValidarGoogle(datos.IdToken);

            if (google == null)
                return Unauthorized("Token de Google invalido.");

            // PASO 2: Buscar una cuenta ya vinculada
            var usuario = await _usuarios.FindByLoginAsync(
                "Google", google.Subject);

            // PASO 3: Si no existe, crear una nueva cuenta Cliente
            if (usuario == null)
            {
                var correo = google.Email.Trim();

                // SEGURIDAD:
                // Si el correo ya existe en una cuenta local,
                // NO la vinculamos automaticamente.
                // Debe utilizar google-vincular con su JWT.
                var cuentaExistente =
                    await _usuarios.FindByEmailAsync(correo);

                if (cuentaExistente != null)
                {
                    return Conflict(new
                    {
                        Mensaje =
                            "Este correo ya tiene una cuenta AA01. " +
                            "Inicia sesion y vincula Google."
                    });
                }

                // Comprobar que la clave para Usuarios.Api exista
                var claveInterna =
                    _configuration["Security:InternalApiKey"];

                if (string.IsNullOrWhiteSpace(claveInterna))
                {
                    return StatusCode(503,
                        "Falta configurar la clave interna.");
                }

                // Crear la cuenta bloqueada mientras se crea
                // su perfil en Usuarios.Api.
                // No necesita contraseña local porque
                // su identidad se verifica mediante Google.
                usuario = new IdentityUser
                {
                    UserName = correo,
                    Email = correo,
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                    LockoutEnd = DateTimeOffset.MaxValue
                };

                var creacion = await _usuarios.CreateAsync(usuario);

                if (!creacion.Succeeded)
                {
                    return BadRequest(
                        creacion.Errors.Select(e => e.Description));
                }

                // Todos los registros publicos de Google
                // reciben el rol Cliente, nunca Administrador.
                var rol = await _usuarios.AddToRoleAsync(
                    usuario, "Cliente");

                if (!rol.Succeeded)
                {
                    await _usuarios.DeleteAsync(usuario);

                    return StatusCode(500,
                        "No se pudo asignar el rol Cliente.");
                }

                // Guardar el identificador estable de Google.
                // No utilizamos el correo como identidad externa.
                var vinculacion = await _usuarios.AddLoginAsync(
                    usuario,
                    new UserLoginInfo(
                        "Google", google.Subject, "Google"));

                if (!vinculacion.Succeeded)
                {
                    await _usuarios.DeleteAsync(usuario);

                    return StatusCode(500,
                        "No se pudo registrar la identidad Google.");
                }

                // Preparar el nombre para crear el perfil
                var nombre = string.IsNullOrWhiteSpace(google.Name)
                    ? correo.Split('@')[0]
                    : google.Name.Trim();

                if (nombre.Length > 100)
                    nombre = nombre.Substring(0, 100);

                // PASO 4: Crear el perfil en Usuarios.Api
                try
                {
                    var cliente = _httpClientFactory
                        .CreateClient("UsuariosApi");

                    using var solicitud = new HttpRequestMessage(
                        HttpMethod.Post, "api/Perfiles/interno");

                    solicitud.Headers.Add(
                        "X-Internal-Key", claveInterna);

                    solicitud.Content = JsonContent.Create(new
                    {
                        UsuarioId = usuario.Id,
                        Nombre = nombre,
                        Correo = correo
                    });

                    using var respuesta =
                        await cliente.SendAsync(solicitud);

                    if (!respuesta.IsSuccessStatusCode)
                    {
                        // La cuenta permanece bloqueada
                        // para evitar una identidad sin perfil.
                        return StatusCode(503,
                            "No se pudo confirmar el perfil. " +
                            "Cuenta pendiente.");
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex,
                        "Usuarios.Api no disponible durante Google.");

                    return StatusCode(503,
                        "Usuarios.Api no esta disponible. " +
                        "Cuenta pendiente.");
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex,
                        "Timeout al crear el perfil Google.");

                    return StatusCode(503,
                        "Tiempo de espera agotado. " +
                        "Cuenta pendiente.");
                }

                // PASO 5: Activar la cuenta solo si
                // el perfil fue creado correctamente.
                var activacion =
                    await _usuarios.SetLockoutEndDateAsync(
                        usuario, null);

                if (!activacion.Succeeded)
                {
                    return StatusCode(503,
                        "La cuenta sigue pendiente de activacion.");
                }
            }

            // No permitir ingresar con una cuenta bloqueada.
            if (await _usuarios.IsLockedOutAsync(usuario))
                return Unauthorized("Cuenta no disponible.");

            // PASO 6: Consultar los roles locales
            var roles = await _usuarios.GetRolesAsync(usuario);

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub, usuario.Id),

                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString()),

                new Claim(
                    ClaimTypes.NameIdentifier, usuario.Id),

                new Claim(
                    ClaimTypes.Email, usuario.Email ?? ""),

                new Claim(
                    ClaimTypes.Name, usuario.UserName ?? "")
            };

            foreach (var nombreRol in roles)
            {
                claims.Add(new Claim(
                    ClaimTypes.Role, nombreRol));
            }

            // PASO 7: Generar el JWT de nuestra aplicacion.
            // NO utilizamos el ID token de Google
            // para acceder a los otros microservicios.
            var claveJwt = _configuration["Jwt:Key"]
                ?? throw new Exception("Falta Jwt:Key.");

            var llave = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(claveJwt));

            var firma = new SigningCredentials(
                llave, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: firma
            );

            return Ok(new
            {
                Mensaje = "Inicio de sesion con Google correcto.",
                Token = new JwtSecurityTokenHandler()
                    .WriteToken(token),
                Tipo = "Bearer",
                Expira = token.ValidTo,
                Roles = roles
            });
        }
    }

    // DTO: recibir el ID token enviado por Google
    public class GoogleTokenRequest
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;
    }
}
