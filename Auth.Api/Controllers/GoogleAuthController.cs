
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
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

        public GoogleAuthController(
            UserManager<IdentityUser> usuarios,
            IConfiguration configuration)
        {
            _usuarios = usuarios;
            _configuration = configuration;
        }

        // GET: api/Auth/google-config
        [HttpGet("google-config")]
        public IActionResult GoogleConfig()
        {
            var clientId = _configuration["Google:ClientId"];

            if (string.IsNullOrWhiteSpace(clientId))
                return StatusCode(503, "Falta Google:ClientId.");

            return Ok(new { ClientId = clientId });
        }

        // Comprobar que el token viene realmente de Google
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
        // Vincula Google con una cuenta ya autenticada
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

            // No vincular una cuenta Google de otra persona
            if (!string.Equals(
                usuario.Email,
                google.Email,
                StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "El correo de Google no coincide con tu cuenta.");
            }

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
        [HttpPost("google-login")]
        public async Task<IActionResult> LoginGoogle(
            [FromBody] GoogleTokenRequest datos)
        {
            var google = await ValidarGoogle(datos.IdToken);

            if (google == null)
                return Unauthorized("Token de Google invalido.");

            // Identificar por el ID exclusivo de Google
            var usuario = await _usuarios.FindByLoginAsync(
                "Google", google.Subject);

            if (usuario == null)
                return Conflict(new
                {
                    Mensaje = "Primero debes vincular esta cuenta " +
                              "Google con tu usuario de AA01."
                });

            if (await _usuarios.IsLockedOutAsync(usuario))
                return Unauthorized("Cuenta no disponible.");

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

            foreach (var rol in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, rol));
            }

            // Generar nuestro propio JWT de AA01
            var clave = _configuration["Jwt:Key"]
                ?? throw new Exception("Falta Jwt:Key.");

            var llave = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(clave));

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

    public class GoogleTokenRequest
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;
    }
}
